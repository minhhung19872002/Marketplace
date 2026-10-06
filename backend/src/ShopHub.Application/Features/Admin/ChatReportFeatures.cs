using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Engage;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Admin;

// Hội thoại bị người mua báo cáo (II.11 "chặn / báo cáo shop"): the platform reads the conversation and either
// dismisses the report or gives the shop penalty points. Reading a private conversation is logged every time.

public record ChatReportRowDto(Guid Id, Guid ConversationId, Guid ShopId, string ShopName, string ReporterName, string Reason,
    ChatReportStatus Status, string? Resolution, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt, int ReportsOnConversation);

public record AdminChatReportsQuery(ChatReportStatus? Status, int Page = 1, int PageSize = 20) : IRequest<PagedResult<ChatReportRowDto>>, IPagedRequest;

public sealed class AdminChatReportsHandler(IApplicationDbContext db) : IRequestHandler<AdminChatReportsQuery, PagedResult<ChatReportRowDto>>
{
    public async Task<PagedResult<ChatReportRowDto>> Handle(AdminChatReportsQuery request, CancellationToken ct)
    {
        var query = from r in db.ChatReports.AsNoTracking()
                    join c in db.Conversations.AsNoTracking() on r.ConversationId equals c.Id
                    join s in db.Shops.AsNoTracking() on c.ShopId equals s.Id
                    join u in db.Users.AsNoTracking() on r.ReporterId equals u.Id
                    where request.Status == null || r.Status == request.Status
                    select new { r.Id, r, c.ShopId, ShopName = s.Name, u.FullName, Count = db.ChatReports.Count(x => x.ConversationId == r.ConversationId) };
        return await query.OrderByDescending(x => x.r.CreatedAt).ThenBy(x => x.Id).ToPagedResultAsync(x => new ChatReportRowDto(
            x.r.Id, x.r.ConversationId, x.ShopId, x.ShopName, x.FullName, x.r.Reason, x.r.Status, x.r.Resolution, x.r.CreatedAt, x.r.ResolvedAt,
            x.Count), request, ct);
    }
}

public record ChatReviewMessageDto(Guid Id, ChatRole SenderRole, MessageType Type, string Body, bool Flagged, DateTimeOffset CreatedAt);

public record ChatReportDetailDto(ChatReportRowDto Report, IReadOnlyList<ChatReviewMessageDto> Messages, int TotalMessages);

public record AdminChatReportDetailQuery(Guid Id) : IRequest<ChatReportDetailDto>;

/// <summary>The report with the last 200 messages of the conversation (oldest first); writes an audit row "VIEW".</summary>
public sealed class AdminChatReportDetailHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<AdminChatReportDetailQuery, ChatReportDetailDto>
{
    private const int Take = 200;

    public async Task<ChatReportDetailDto> Handle(AdminChatReportDetailQuery request, CancellationToken ct)
    {
        var row = await (from r in db.ChatReports.AsNoTracking()
                         join c in db.Conversations.AsNoTracking() on r.ConversationId equals c.Id
                         join s in db.Shops.AsNoTracking() on c.ShopId equals s.Id
                         join u in db.Users.AsNoTracking() on r.ReporterId equals u.Id
                         where r.Id == request.Id
                         select new ChatReportRowDto(r.Id, c.Id, s.Id, s.Name, u.FullName, r.Reason, r.Status, r.Resolution, r.CreatedAt,
                             r.ResolvedAt, db.ChatReports.Count(x => x.ConversationId == c.Id))).FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Không tìm thấy báo cáo.");
        var messages = db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == row.ConversationId);
        var total = await messages.CountAsync(ct);
        var latest = await messages.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(Take)
            .Select(m => new ChatReviewMessageDto(m.Id, m.SenderRole, m.Type, m.Body, m.Flagged, m.CreatedAt)).ToListAsync(ct);
        latest.Reverse();

        db.AuditLogs.Add(new AuditLog(currentUser.UserId, currentUser.IpAddress, currentUser.UserAgent, "VIEW", "conversations",
            row.ConversationId.ToString(), null,
            System.Text.Json.JsonSerializer.Serialize(new { note = "Xem hội thoại bị báo cáo", reportId = row.Id, messages = latest.Count }), clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return new ChatReportDetailDto(row, latest, total);
    }
}

public record ResolveChatReportCommand(Guid Id, string Resolution, int? PenaltyPoints) : IRequest<Unit>;

public sealed class ResolveChatReportValidator : AbstractValidator<ResolveChatReportCommand>
{
    public ResolveChatReportValidator()
    {
        RuleFor(x => x.Resolution).NotEmpty().WithMessage("Vui lòng ghi kết quả xử lý.").MaximumLength(500);
        RuleFor(x => x.PenaltyPoints).InclusiveBetween(1, 20).When(x => x.PenaltyPoints is not null).WithMessage("Mỗi lần ghi từ 1 đến 20 điểm.");
    }
}

/// <summary>
/// Dismiss, or penalize the shop through the regular penalty flow (recomputed points, consequences, owner notified).
/// Every open report of the conversation is claimed by one conditional UPDATE, so two admins deciding at once cannot
/// penalize twice; if the penalty fails the reports are reopened.
/// </summary>
public sealed class ResolveChatReportHandler(IApplicationDbContext db, ISender sender, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<ResolveChatReportCommand, Unit>
{
    public async Task<Unit> Handle(ResolveChatReportCommand request, CancellationToken ct)
    {
        var report = await db.ChatReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.Id, ct)
                     ?? throw new NotFoundException("Không tìm thấy báo cáo.");
        var shopId = await db.Conversations.Where(c => c.Id == report.ConversationId).Select(c => c.ShopId).FirstAsync(ct);
        var status = request.PenaltyPoints is null ? ChatReportStatus.Dismissed : ChatReportStatus.Penalized;
        var resolution = request.Resolution.Trim();
        var adminId = currentUser.UserId;
        var now = clock.UtcNow;

        // The report itself must still be open; it decides for the other open reports of the same conversation too
        var claimed = await db.ChatReports
            .Where(r => r.ConversationId == report.ConversationId && r.Status == ChatReportStatus.Open
                        && db.ChatReports.Any(x => x.Id == request.Id && x.Status == ChatReportStatus.Open))
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Resolution, resolution)
                .SetProperty(r => r.ResolvedBy, adminId)
                .SetProperty(r => r.ResolvedAt, now), ct);
        if (claimed == 0) throw new ConflictException("Báo cáo này đã được xử lý.", "REPORT_RESOLVED");

        if (request.PenaltyPoints is { } points)
        {
            try
            {
                await sender.Send(new AddShopPenaltyCommand(shopId, points, $"Vi phạm trong chat: {resolution}", null), ct);
            }
            catch
            {
                await db.ChatReports.Where(r => r.ConversationId == report.ConversationId && r.ResolvedAt == now && r.Status == status)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, ChatReportStatus.Open).SetProperty(r => r.Resolution, (string?)null)
                        .SetProperty(r => r.ResolvedBy, (Guid?)null).SetProperty(r => r.ResolvedAt, (DateTimeOffset?)null), CancellationToken.None);
                throw;
            }
        }
        return Unit.Value;
    }
}
