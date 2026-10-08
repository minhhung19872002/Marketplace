using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Checkout;
using ShopHub.Application.Features.Seller;

namespace ShopHub.Application.Features.Storefront;

public record ShippingDestinationDto(Guid? AddressId, string ProvinceCode, string Label);

public record ShippingEstimateOptionDto(string Code, string Name, long Fee, int Days, DateOnly ExpectedDate, bool SupportsCod);

public record ShippingEstimateDto(ShippingDestinationDto Destination, string? FromProvinceName, IReadOnlyList<ShippingEstimateOptionDto> Options,
    IReadOnlyList<ShippingDestinationDto> MyAddresses);

/// <summary>
/// "Vận chuyển tới …" on the product page (II.4): one unit of the cheapest variant from the product's ship-from warehouse
/// to the buyer's default address (or the address / province they pick), with the shop's carriers and the product's
/// allowed carriers — the same rules as the checkout.
/// </summary>
public record ShippingEstimateQuery(Guid ProductId, Guid? AddressId, string? ProvinceCode) : IRequest<ShippingEstimateDto>;

public sealed class ShippingEstimateHandler(IApplicationDbContext db, ICurrentUser currentUser, ShippingCalculator shipping)
    : IRequestHandler<ShippingEstimateQuery, ShippingEstimateDto>
{
    // Guests without a choice see the fee to the capital
    private const string DefaultProvince = "01";

    public async Task<ShippingEstimateDto> Handle(ShippingEstimateQuery request, CancellationToken ct)
    {
        var p = await ProductCards.Visible(db).AsNoTracking().Include(x => x.Skus).FirstOrDefaultAsync(x => x.Id == request.ProductId, ct)
                ?? throw new NotFoundException("Sản phẩm không tồn tại hoặc đã ngừng bán.");
        var shop = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == p.ShopId, ct);

        // Destination: an address of mine (picked, else my default), else the province the guest picked
        var mine = currentUser.UserId is { } userId
            ? await db.Addresses.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToListAsync(ct)
            : [];
        var address = request.AddressId is { } id ? mine.FirstOrDefault(a => a.Id == id) : request.ProvinceCode is null ? mine.FirstOrDefault() : null;
        var codes = mine.Select(a => a.ProvinceCode).Append(request.ProvinceCode ?? DefaultProvince).Distinct().ToList();
        var provinceNames = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
        RoutePoint to;
        ShippingDestinationDto destination;
        if (address is not null)
        {
            to = new RoutePoint(address.ProvinceCode, address.DistrictCode, address.WardCode);
            destination = new ShippingDestinationDto(address.Id, address.ProvinceCode, Label(address.Street, provinceNames.GetValueOrDefault(address.ProvinceCode)));
        }
        else
        {
            var province = request.ProvinceCode is { } code && provinceNames.ContainsKey(code) ? code : DefaultProvince;
            // A province alone: its first ward stands for it (fees are by zone / province)
            var ward = await db.AdminDivisions.AsNoTracking()
                .Where(d => d.ParentCode == province && d.Level == Domain.Iam.AdminDivisionLevel.Ward && d.IsActive)
                .OrderBy(d => d.Code).Select(d => d.Code).FirstOrDefaultAsync(ct) ?? "";
            to = new RoutePoint(province, null, ward);
            destination = new ShippingDestinationDto(null, province, provinceNames.GetValueOrDefault(province) ?? province);
        }

        var warehouses = await Parcels.WarehousesAsync(db, shop.Id, ct);
        var sku = p.Skus.Where(s => s.IsActive).OrderBy(s => s.Price).ThenBy(s => s.Id).FirstOrDefault();
        IReadOnlyList<ShippingEstimateOptionDto> options = [];
        string? fromName = null;
        if (warehouses.Count > 0 && sku is not null)
        {
            var from = Parcels.ShipFrom(shop.MultiWarehouse, warehouses, p.WarehouseId);
            fromName = await db.AdminDivisions.AsNoTracking().Where(d => d.Code == from.ProvinceCode).Select(d => d.Name).FirstOrDefaultAsync(ct);
            var weight = ShippingCalculator.ChargeableWeightG([new ParcelItem(sku.WeightG ?? p.WeightG, sku.LengthMm ?? p.LengthMm, sku.WidthMm ?? p.WidthMm, sku.HeightMm ?? p.HeightMm, 1)]);
            var channels = await ShopChannels.ForShopAsync(db, shop.Id, ct);
            options = (await shipping.QuoteAsync(new RoutePoint(from.ProvinceCode, from.DistrictCode, from.WardCode), to, weight, sku.Price, ct))
                .Where(o => ShopChannels.Allows(channels, o.Code) && (p.CarrierCodes.Count == 0 || p.CarrierCodes.Contains(o.Code)))
                .Select(o => new ShippingEstimateOptionDto(o.Code, o.Name, o.Fee, o.Days, o.ExpectedDate, o.SupportsCod && ShopChannels.AllowsCod(channels, o.Code)))
                .ToList();
        }
        return new ShippingEstimateDto(destination, ProductCards.ShortProvince(fromName), options,
            mine.Select(a => new ShippingDestinationDto(a.Id, a.ProvinceCode, Label(a.Street, provinceNames.GetValueOrDefault(a.ProvinceCode)))).ToList());
    }

    private static string Label(string street, string? province) => province is null ? street : $"{street}, {province}";
}
