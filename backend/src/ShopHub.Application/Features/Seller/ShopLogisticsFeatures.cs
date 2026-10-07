using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Application.Security;
using ShopHub.Domain.Common;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seller;

// ---------- Kho hàng & địa chỉ trả hàng, đa kho, đơn vị vận chuyển (III.9) ----------

public record WarehouseDto(Guid Id, string Name, string ContactName, string Phone, string ProvinceCode, string DistrictCode, string WardCode,
    string Street, string FullAddress, bool IsPickupDefault, bool IsReturnDefault, int ProductCount);

public record ShippingChannelDto(string CarrierCode, string Name, string? Description, bool CarrierActive, bool CarrierSupportsCod, bool IsEnabled,
    bool CodEnabled);

public record ShopLogisticsDto(bool MultiWarehouse, IReadOnlyList<WarehouseDto> Warehouses, IReadOnlyList<ShippingChannelDto> Channels, int MaxWarehouses);

/// <summary>The shop's carriers as it set them: no row = on, COD as the carrier allows.</summary>
public static class ShopChannels
{
    public sealed record Choice(bool Enabled, bool Cod);

    public static async Task<Dictionary<string, Choice>> ForShopAsync(IApplicationDbContext db, Guid shopId, CancellationToken ct) =>
        await db.ShopShippingChannels.AsNoTracking().Where(c => c.ShopId == shopId)
            .ToDictionaryAsync(c => c.CarrierCode, c => new Choice(c.IsEnabled, c.CodEnabled), ct);

    public static bool Allows(IReadOnlyDictionary<string, Choice> choices, string code) => !choices.TryGetValue(code, out var c) || c.Enabled;

    public static bool AllowsCod(IReadOnlyDictionary<string, Choice> choices, string code) => !choices.TryGetValue(code, out var c) || c.Cod;
}

public record CarrierOptionDto(string Code, string Name, string? Description, bool SupportsCod);

/// <summary>Active carriers, for the registration form (III.1) — before the seller has a shop.</summary>
public record ActiveCarriersQuery : IRequest<IReadOnlyList<CarrierOptionDto>>;

public sealed class ActiveCarriersHandler(IApplicationDbContext db) : IRequestHandler<ActiveCarriersQuery, IReadOnlyList<CarrierOptionDto>>
{
    public async Task<IReadOnlyList<CarrierOptionDto>> Handle(ActiveCarriersQuery request, CancellationToken ct) =>
        await db.Carriers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => new CarrierOptionDto(c.Code, c.Name, c.Description, c.SupportsCod)).ToListAsync(ct);
}

public record ShopLogisticsQuery(Guid ShopId) : IRequest<ShopLogisticsDto>;

public sealed class ShopLogisticsHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<ShopLogisticsQuery, ShopLogisticsDto>
{
    public const int MaxWarehouses = 10;

    public async Task<ShopLogisticsDto> Handle(ShopLogisticsQuery request, CancellationToken ct)
    {
        // Read by the product editor too (kho gửi, carriers of a product); changing it needs SETTINGS.MANAGE
        await access.RequireAsync(request.ShopId, ShopPermissions.ProductView, ct);
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == request.ShopId, ct);
        var warehouses = await Checkout.Parcels.WarehousesAsync(db, shop.Id, ct);
        var ids = warehouses.Select(w => w.Id).ToList();
        var counts = await db.Products.Where(p => p.ShopId == shop.Id && p.WarehouseId != null && ids.Contains(p.WarehouseId.Value))
            .GroupBy(p => p.WarehouseId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var codes = warehouses.SelectMany(w => new[] { w.ProvinceCode, w.DistrictCode, w.WardCode }).Distinct().ToList();
        var names = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
        string Full(ShopWarehouse w) => string.Join(", ", new[] { w.Street, names.GetValueOrDefault(w.WardCode), names.GetValueOrDefault(w.DistrictCode),
            names.GetValueOrDefault(w.ProvinceCode) }.Where(x => !string.IsNullOrEmpty(x)));

        var choices = await ShopChannels.ForShopAsync(db, shop.Id, ct);
        var carriers = await db.Carriers.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        return new ShopLogisticsDto(shop.MultiWarehouse,
            warehouses.Select(w => new WarehouseDto(w.Id, w.Name, w.ContactName, w.Phone, w.ProvinceCode, w.DistrictCode, w.WardCode, w.Street, Full(w),
                w.IsPickupDefault, w.IsReturnDefault, counts.GetValueOrDefault(w.Id))).ToList(),
            carriers.Select(c => new ShippingChannelDto(c.Code, c.Name, c.Description, c.IsActive, c.SupportsCod, ShopChannels.Allows(choices, c.Code),
                c.SupportsCod && ShopChannels.AllowsCod(choices, c.Code))).ToList(),
            MaxWarehouses);
    }
}

public record SaveWarehouseCommand(Guid ShopId, Guid? WarehouseId, string Name, WarehouseInput Address, bool IsPickupDefault, bool IsReturnDefault)
    : IRequest<Guid>;

public sealed class SaveWarehouseValidator : AbstractValidator<SaveWarehouseCommand>
{
    public SaveWarehouseValidator()
    {
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập tên kho.")
            .MaximumLength(100).WithMessage("Tên kho tối đa 100 ký tự.");
        RuleFor(x => x.Address).NotNull().WithMessage("Vui lòng nhập địa chỉ kho.");
        RuleFor(x => x.Address.ContactName).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập người liên hệ.")
            .MaximumLength(100).WithMessage("Tên người liên hệ tối đa 100 ký tự.").When(x => x.Address is not null);
        RuleFor(x => x.Address.Phone).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập số điện thoại kho.")
            .Must(p => Identifiers.NormalisePhone(p) is not null).WithMessage("Số điện thoại không hợp lệ.").When(x => x.Address is not null);
        RuleFor(x => x.Address.Street).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập số nhà, tên đường.")
            .MaximumLength(255).WithMessage("Địa chỉ tối đa 255 ký tự.").When(x => x.Address is not null);
    }
}

/// <summary>
/// Adds or edits a warehouse. Exactly one warehouse is the default pickup and one the default return address: making
/// another one the default moves the flag, taking the flag off the only default is refused.
/// </summary>
public sealed class SaveWarehouseHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<SaveWarehouseCommand, Guid>
{
    public async Task<Guid> Handle(SaveWarehouseCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        await ShopRules.EnsureWarehouseAsync(db, request.Address, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"shop:warehouses:{request.ShopId}", ct);
        var all = await db.ShopWarehouses.Where(w => w.ShopId == request.ShopId).ToListAsync(ct);
        ShopWarehouse warehouse;
        if (request.WarehouseId is { } id)
            warehouse = all.FirstOrDefault(w => w.Id == id) ?? throw new NotFoundException("Không tìm thấy kho hàng.");
        else
        {
            if (all.Count >= ShopLogisticsHandler.MaxWarehouses)
                throw new ConflictException($"Mỗi shop tối đa {ShopLogisticsHandler.MaxWarehouses} kho hàng.", "WAREHOUSE_LIMIT");
            warehouse = new ShopWarehouse(request.ShopId);
            db.ShopWarehouses.Add(warehouse);
            all.Add(warehouse);
        }
        var pickup = request.IsPickupDefault || all.Count == 1;
        var @return = request.IsReturnDefault || all.Count == 1;
        if (!pickup && warehouse.IsPickupDefault)
            throw new ConflictException("Shop cần một kho lấy hàng mặc định — hãy đặt kho khác làm mặc định trước.", "NEED_PICKUP_DEFAULT");
        if (!@return && warehouse.IsReturnDefault)
            throw new ConflictException("Shop cần một địa chỉ trả hàng mặc định — hãy đặt kho khác làm mặc định trước.", "NEED_RETURN_DEFAULT");
        var a = request.Address;
        warehouse.Update(request.Name, a.ContactName, Identifiers.NormalisePhone(a.Phone)!, a.ProvinceCode, a.DistrictCode, a.WardCode, a.Street,
            pickup, @return);
        foreach (var other in all.Where(w => w.Id != warehouse.Id))
            other.MakeDefaults(other.IsPickupDefault && !pickup, other.IsReturnDefault && !@return);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return warehouse.Id;
    }
}

public record DeleteWarehouseCommand(Guid ShopId, Guid WarehouseId) : IRequest<Unit>;

/// <summary>A default warehouse cannot go, nor one that parcels still waiting to leave come from; its products fall back to the default.</summary>
public sealed class DeleteWarehouseHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<DeleteWarehouseCommand, Unit>
{
    public async Task<Unit> Handle(DeleteWarehouseCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"shop:warehouses:{request.ShopId}", ct);
        var warehouse = await db.ShopWarehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.ShopId == request.ShopId, ct)
                        ?? throw new NotFoundException("Không tìm thấy kho hàng.");
        if (warehouse.IsPickupDefault || warehouse.IsReturnDefault)
            throw new ConflictException("Không xoá được kho mặc định (lấy hàng / trả hàng).", "DEFAULT_WAREHOUSE");
        var waiting = await db.OrderPackages.AnyAsync(p => p.WarehouseId == warehouse.Id
                                                           && db.Orders.Any(o => o.Id == p.OrderId && (o.Status == OrderStatus.PendingPayment
                                                               || o.Status == OrderStatus.PendingConfirmation || o.Status == OrderStatus.ReadyToShip)), ct);
        if (waiting) throw new ConflictException("Còn đơn hàng chờ gửi từ kho này — hãy giao xong các đơn ấy trước.", "WAREHOUSE_IN_USE");
        foreach (var product in await db.Products.IgnoreQueryFilters().Where(p => p.WarehouseId == warehouse.Id).ToListAsync(ct))
            product.ShipFrom(null);
        db.ShopWarehouses.Remove(warehouse);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}

public record SetMultiWarehouseCommand(Guid ShopId, bool Enabled) : IRequest<Unit>;

public sealed class SetMultiWarehouseHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<SetMultiWarehouseCommand, Unit>
{
    public async Task<Unit> Handle(SetMultiWarehouseCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        if (request.Enabled && await db.ShopWarehouses.CountAsync(w => w.ShopId == request.ShopId, ct) < 2)
            throw new ConflictException("Cần ít nhất 2 kho hàng để bật đa kho.", "NEED_TWO_WAREHOUSES");
        await db.RetryOnStaleAsync(async () =>
        {
            var shop = await db.Shops.FirstAsync(s => s.Id == request.ShopId, ct);
            shop.SetMultiWarehouse(request.Enabled);
            await db.SaveChangesAsync(ct);
        });
        return Unit.Value;
    }
}

public record SetShippingChannelCommand(Guid ShopId, string CarrierCode, bool Enabled, bool CodEnabled) : IRequest<Unit>;

/// <summary>Switch a carrier off for the shop, or keep it without COD; at least one active carrier must stay on.</summary>
public sealed class SetShippingChannelHandler(IApplicationDbContext db, SellerAccess access) : IRequestHandler<SetShippingChannelCommand, Unit>
{
    public async Task<Unit> Handle(SetShippingChannelCommand request, CancellationToken ct)
    {
        await access.RequireAsync(request.ShopId, ShopPermissions.SettingsManage, ct);
        var carrier = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Code == request.CarrierCode, ct)
                      ?? throw new NotFoundException("Không tìm thấy đơn vị vận chuyển.");
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"shop:channels:{request.ShopId}", ct);
        var row = await db.ShopShippingChannels.FirstOrDefaultAsync(c => c.ShopId == request.ShopId && c.CarrierCode == carrier.Code, ct);
        if (row is null) db.ShopShippingChannels.Add(row = new ShopShippingChannel(request.ShopId, carrier.Code, request.Enabled, request.CodEnabled));
        else row.Set(request.Enabled, request.CodEnabled);
        if (!request.Enabled)
        {
            var choices = await ShopChannels.ForShopAsync(db, request.ShopId, ct);
            choices[carrier.Code] = new ShopChannels.Choice(false, request.CodEnabled);
            var active = await db.Carriers.AsNoTracking().Where(c => c.IsActive).Select(c => c.Code).ToListAsync(ct);
            if (!active.Any(code => ShopChannels.Allows(choices, code)))
                throw new ConflictException("Shop cần ít nhất một đơn vị vận chuyển đang bật.", "NEED_ONE_CARRIER");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
