using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Orders;
using ShopHub.Domain.Sales;

namespace ShopHub.Application.Features.Returns;

public record ManualRefundLineInput(Guid OrderItemId, int Quantity);

/// <summary>
/// Hoàn tiền thủ công của sàn (spec VI.5) for a delivered / completed order: the admin picks lines and units, the amount
/// (at most what was paid for those units after every allocated discount) and who bears it.
/// </summary>
public record AdminManualRefundCommand(string Code, IReadOnlyList<ManualRefundLineInput> Lines, long Amount, bool PlatformBorne, string Reason)
    : IRequest<ReturnDto>;

public sealed class AdminManualRefundValidator : AbstractValidator<AdminManualRefundCommand>
{
    public AdminManualRefundValidator()
    {
        RuleFor(x => x.Reason).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Hoàn tiền thủ công phải ghi lý do.")
            .MinimumLength(10).WithMessage("Lý do cần ít nhất 10 ký tự.").MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Số tiền hoàn phải lớn hơn 0.");
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Chọn ít nhất một sản phẩm cần hoàn tiền.")
            .Must(l => l.Count <= 50).WithMessage("Tối đa 50 dòng mỗi lần hoàn.");
        RuleForEach(x => x.Lines).Must(l => l.Quantity > 0).WithMessage("Số lượng hoàn phải lớn hơn 0.");
    }
}

/// <summary>
/// Goes through the same pipeline as a return (<see cref="ReturnRefunder"/>): money back to the payment source, xu back
/// when the units are refunded in full, the units closed so they can never be refunded twice, the ledger following
/// through the order event. "Shop chịu" lowers the shop's earnings before they are released (the release of that part
/// is held by the refund itself); once the order is released only "sàn chịu" is possible.
/// </summary>
public sealed class AdminManualRefundHandler(IApplicationDbContext db, IObjectStorage storage, OrderLocks locks, ReturnRefunder refunder, IClock clock)
    : IRequestHandler<AdminManualRefundCommand, ReturnDto>
{
    public async Task<ReturnDto> Handle(AdminManualRefundCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var orderId = await db.Orders.Where(o => o.Code == code).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
        var now = clock.UtcNow;
        await using var tx = await db.BeginTransactionAsync(ct);
        var order = await locks.LockAsync(orderId, ct);
        // The settlement of this order waits for us (same lock as the ledger sync and the release)
        await db.LockAsync($"finance:order:{orderId}", ct);
        if (order.Status is not (OrderStatus.Delivered or OrderStatus.Completed))
            throw new ConflictException("Chỉ hoàn tiền thủ công được cho đơn đã giao hoặc đã hoàn thành.", "NOT_REFUNDABLE");
        if (!request.PlatformBorne && await db.SettlementItems.AnyAsync(s => s.OrderId == orderId, ct))
            throw new ConflictException("Đơn đã giải ngân cho shop: chỉ hoàn được với tuỳ chọn \"sàn chịu\".", "ALREADY_RELEASED");

        var lineIds = order.Items.Select(i => i.Id).ToList();
        var used = await Returnability.UsedQuantitiesAsync(db, lineIds, ct);
        var refunded = await Returnability.RefundedQuantitiesAsync(db, lineIds, ct);
        var r = ReturnRequest.ManualByPlatform(order.Id, order.BuyerId, order.ShopId, CreateReturnHandler.NewCode(now), request.Reason,
            request.PlatformBorne, now);
        long money = 0, coins = 0;
        foreach (var input in request.Lines.GroupBy(l => l.OrderItemId).Select(g => new ManualRefundLineInput(g.Key, g.Sum(x => x.Quantity))))
        {
            var line = order.Items.FirstOrDefault(i => i.Id == input.OrderItemId) ?? throw new NotFoundException("Không tìm thấy sản phẩm trong đơn.");
            if (used.GetValueOrDefault(line.Id) > refunded.GetValueOrDefault(line.Id))
                throw new ConflictException($"\"{line.NameSnapshot}\" đang có yêu cầu trả hàng chưa xử lý xong — xử lý yêu cầu ấy trước.", "RETURN_OPEN");
            var (m, c) = ReturnPricing.ForUnits(Returnability.PaidMoney(line), Returnability.PaidCoins(line), line.Quantity, refunded.GetValueOrDefault(line.Id),
                input.Quantity);
            r.Items.Add(new ReturnItem(r.Id, line.Id, input.Quantity, m, c));
            money += m;
            coins += c;
        }
        if (request.Amount > money)
            throw new ConflictException($"Số tiền hoàn tối đa cho các sản phẩm đã chọn là {Domain.Common.Money.Vnd(money)} (phần người mua đã trả sau giảm giá).",
                "REFUND_TOO_LARGE");
        r.SetAmounts(money, coins);
        db.ReturnRequests.Add(r);
        // Xu come back only with a full refund of the units; a partial amount is money only
        await refunder.RefundAsync(r, request.Amount, request.Amount == money ? coins : 0, ReturnParty.Admin, $"Sàn hoàn tiền thủ công: {request.Reason.Trim()}", ct);
        await tx.CommitAsync(ct);
        return await ReturnViews.BuildAsync(db, storage, await ReturnViews.WithDetails(db).AsNoTracking().SingleAsync(x => x.Id == r.Id, ct), ct);
    }
}
