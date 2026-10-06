using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Identity;

public record OtpIssued(int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>
/// OTP lifecycle: 6 digits, stored hashed, TTL / resend cooldown / hourly cap / wrong-attempt cap from parameters.
/// Delivery goes through the outbox (SMS for phones, email otherwise) and the dispatcher is kicked right away.
/// </summary>
public sealed class OtpService(
    IApplicationDbContext db,
    ISecretGenerator secrets,
    ISystemParameters parameters,
    IOutbox outbox,
    IOutboxSignal outboxSignal,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<OtpIssued> IssueAsync(string target, OtpPurpose purpose, bool deliver, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ttl = (int)await parameters.GetIntAsync(ParameterKeys.AuthOtpTtlSeconds, ct);
        var resend = (int)await parameters.GetIntAsync(ParameterKeys.AuthOtpResendSeconds, ct);
        var perHour = (int)await parameters.GetIntAsync(ParameterKeys.AuthOtpMaxPerHour, ct);

        var recent = await db.OtpCodes
            .Where(o => o.Target == target && o.CreatedAt > now.AddHours(-1))
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => o.CreatedAt)
            .ToListAsync(ct);

        if (recent.Count > 0 && recent[0] > now.AddSeconds(-resend))
        {
            var wait = (int)Math.Ceiling((recent[0].AddSeconds(resend) - now).TotalSeconds);
            throw new ConflictException($"Vui lòng đợi {wait} giây trước khi gửi lại mã.", "OTP_COOLDOWN");
        }
        if (recent.Count >= perHour)
            throw new ConflictException("Bạn đã yêu cầu quá nhiều mã trong một giờ. Vui lòng thử lại sau.", "OTP_HOURLY_LIMIT");

        var code = secrets.NewOtpCode();
        db.OtpCodes.Add(new OtpCode(target, purpose, secrets.Hash(code), currentUser.IpAddress, now, now.AddSeconds(ttl)));

        // When the target must not receive a code (e.g. reset for an unknown number) the row is still written so the
        // cooldown/limits behave identically — the response never reveals whether an account exists.
        if (deliver) EnqueueDelivery(target, purpose, code, ttl);

        await db.SaveChangesAsync(ct);
        if (deliver) outboxSignal.Kick();
        return new OtpIssued(ttl, resend);
    }

    /// <summary>Checks a code; on success marks it verified and returns the newest matching OTP row.</summary>
    public async Task<OtpCode> VerifyAsync(string target, OtpPurpose purpose, string code, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var maxAttempts = (int)await parameters.GetIntAsync(ParameterKeys.AuthOtpMaxAttempts, ct);

        var otp = await db.OtpCodes
            .Where(o => o.Target == target && o.Purpose == purpose && o.ConsumedAt == null && o.VerifiedAt == null)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("Mã xác thực không đúng hoặc đã hết hạn.", "OTP_INVALID");

        if (otp.ExpiresAt <= now) throw new ConflictException("Mã xác thực đã hết hạn. Vui lòng gửi lại mã.", "OTP_EXPIRED");

        if (!string.Equals(otp.CodeHash, secrets.Hash(code.Trim()), StringComparison.Ordinal))
        {
            // Atomic, conditional increment: parallel guesses cannot exceed the cap
            var counted = await db.OtpCodes
                .Where(o => o.Id == otp.Id && o.Attempts < maxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Attempts, o => o.Attempts + 1), ct);
            throw counted == 0 || otp.Attempts + 1 >= maxAttempts
                ? new ConflictException("Bạn đã nhập sai quá số lần cho phép. Vui lòng gửi lại mã.", "OTP_LOCKED")
                : new ConflictException("Mã xác thực không đúng.", "OTP_INVALID");
        }

        if (otp.Attempts >= maxAttempts)
            throw new ConflictException("Bạn đã nhập sai quá số lần cho phép. Vui lòng gửi lại mã.", "OTP_LOCKED");

        return otp;
    }

    /// <summary>Verify, then hand out a one-time ticket for the next step (register / reset password).</summary>
    public async Task<string> VerifyForTicketAsync(string target, OtpPurpose purpose, string code, CancellationToken ct)
    {
        var otp = await VerifyAsync(target, purpose, code, ct);
        var ticket = secrets.NewOpaqueToken();

        // Conditional update: two parallel correct submissions yield one ticket
        var marked = await db.OtpCodes
            .Where(o => o.Id == otp.Id && o.VerifiedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.VerifiedAt, clock.UtcNow)
                .SetProperty(o => o.VerificationTokenHash, secrets.Hash(ticket)), ct);
        if (marked == 0) throw new ConflictException("Mã xác thực đã được sử dụng.", "OTP_USED");
        return ticket;
    }

    /// <summary>Redeem a ticket exactly once (it must be recent: within the OTP TTL of verification).</summary>
    public async Task RedeemTicketAsync(string target, OtpPurpose purpose, string ticket, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ttl = (int)await parameters.GetIntAsync(ParameterKeys.AuthOtpTtlSeconds, ct);
        var hash = secrets.Hash(ticket);

        var redeemed = await db.OtpCodes
            .Where(o => o.Target == target && o.Purpose == purpose && o.VerificationTokenHash == hash
                        && o.ConsumedAt == null && o.VerifiedAt > now.AddSeconds(-ttl))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConsumedAt, now), ct);
        if (redeemed == 0)
            throw new ConflictException("Phiên xác thực đã hết hạn. Vui lòng xác thực lại.", "TICKET_INVALID");
    }

    public async Task ConsumeAsync(OtpCode otp, CancellationToken ct)
    {
        var consumed = await db.OtpCodes
            .Where(o => o.Id == otp.Id && o.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConsumedAt, clock.UtcNow), ct);
        if (consumed == 0) throw new ConflictException("Mã xác thực đã được sử dụng.", "OTP_USED");
    }

    private void EnqueueDelivery(string target, OtpPurpose purpose, string code, int ttlSeconds)
    {
        var minutes = Math.Max(1, ttlSeconds / 60);
        var action = purpose switch
        {
            OtpPurpose.Register => "đăng ký tài khoản",
            OtpPurpose.Login => "đăng nhập",
            OtpPurpose.ResetPassword => "đặt lại mật khẩu",
            OtpPurpose.ChangePhone => "đổi số điện thoại",
            OtpPurpose.ChangeEmail => "đổi email",
            _ => "xác thực",
        };
        var text = $"ShopHub: Ma {code} de {action}. Hieu luc {minutes} phut. KHONG chia se ma nay cho bat ky ai.";

        if (Identifiers.IsPhone(target))
            outbox.Enqueue(OutboxTypes.NotifySms, new SmsPayload(target, text));
        else
            outbox.Enqueue(OutboxTypes.NotifyEmail, new EmailPayload(
                target,
                $"Mã xác thực ShopHub: {code}",
                $"<p>Mã xác thực để {action} của bạn là <strong>{code}</strong>.</p>" +
                $"<p>Mã có hiệu lực trong {minutes} phút. Không chia sẻ mã này cho bất kỳ ai.</p>"));
    }
}
