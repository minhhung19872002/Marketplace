using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Catalog;
using ShopHub.Domain.Iam;
using ShopHub.Domain.Promo;
using ShopHub.Domain.Sales;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Checkout;

public record CheckoutShopChoice(Guid ShopId, string? CarrierCode, string? VoucherCode, string? Note);

public record CheckoutRequest(
    Guid? AddressId,
    IReadOnlyList<CheckoutShopChoice>? Shops,
    string? PlatformVoucherCode,
    string? FreeshipVoucherCode,
    bool UseCoins,
    PaymentMethod PaymentMethod);

// PriceLabel: "Flash Sale" / "Giảm giá" / "Mua kèm" when the unit price comes from a programme
public record QuoteLineDto(Guid SkuId, Guid ProductId, string Name, string? ImageUrl, string? Variant, long UnitPrice, long OriginalPrice,
    int Quantity, long LineTotal, long ShopDiscount, long PlatformDiscount, long CoinDiscount, long ComboDiscount = 0, string? PriceLabel = null);

public record QuoteGiftDto(Guid SkuId, string Name, string? Variant, int Quantity, long Value);

public record VoucherOptionDto(Guid Id, string Code, string Name, VoucherType Type, long DiscountValue, int DiscountPercentBp, long? MaxDiscount,
    long MinOrder, DateTimeOffset EndAt, long Discount, bool Usable, string? Problem, bool Selected);

public record QuoteShopDto(
    Guid ShopId,
    string ShopName,
    bool IsMall,
    IReadOnlyList<QuoteLineDto> Lines,
    IReadOnlyList<ShippingOption> ShippingOptions,
    string? CarrierCode,
    long Subtotal,
    long ShopDiscount,
    long ShippingFee,
    long ShippingDiscount,
    long PlatformDiscount,
    long CoinUsed,
    long Total,
    VoucherOptionDto? ShopVoucher,
    IReadOnlyList<VoucherOptionDto> ShopVoucherOptions,
    string? Note,
    long ComboDiscount = 0,
    IReadOnlyList<QuoteGiftDto>? Gifts = null,
    IReadOnlyList<QuoteParcelDto>? Parcels = null);

// A parcel of the shop's order (đa kho): where it leaves from and what is in it
public record QuoteParcelDto(int No, string WarehouseName, string ProvinceCode, long ShippingFee, IReadOnlyList<Guid> ProductIds);

public record PaymentMethodDto(PaymentMethod Code, string Name, bool Available, string? Reason);

public record CoinInfoDto(long Balance, long Max, long Used, bool Applied);

public record CheckoutAddressDto(Guid Id, string ReceiverName, string Phone, string FullAddress, string ProvinceCode);

public record CheckoutQuoteDto(
    CheckoutAddressDto? Address,
    IReadOnlyList<QuoteShopDto> Shops,
    IReadOnlyList<VoucherOptionDto> PlatformVouchers,
    IReadOnlyList<VoucherOptionDto> FreeshipVouchers,
    CoinInfoDto Coins,
    IReadOnlyList<PaymentMethodDto> PaymentMethods,
    PaymentMethod PaymentMethod,
    long Subtotal,
    long ShopDiscount,
    long ShippingFee,
    long ShippingDiscount,
    long PlatformDiscount,
    long CoinUsed,
    long GrandTotal,
    long CoinCashback,
    IReadOnlyList<string> Problems,
    bool CanPlace,
    long ComboDiscount = 0);

/// <summary>Everything needed to place the orders exactly as quoted.</summary>
public sealed record CheckoutPlan(
    CheckoutQuoteDto Quote,
    Address? Address,
    string AddressSnapshot,
    PricingResult? Pricing,
    IReadOnlyDictionary<Guid, PlanLine> Lines,
    IReadOnlyDictionary<Guid, Voucher> ShopVouchers,
    Voucher? PlatformVoucher,
    Voucher? FreeshipVoucher,
    IReadOnlyDictionary<Guid, ShippingOption> Carriers,
    IReadOnlyList<Marketing.GiftLine>? Gifts = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<PlanParcel>>? Parcels = null);

public sealed record PlanLine(Guid SkuId, Guid ProductId, Guid ShopId, Guid CategoryId, string Name, string? Variant, string? ImageUrl,
    long UnitPrice, long OriginalPrice, int Quantity, Marketing.LinePrice? Price = null);

/// <summary>
/// Builds the checkout from the buyer's ticked cart lines: shipping per shop, voucher options with reasons, xu, payment
/// methods — all priced by <see cref="PricingEngine"/>. The same plan is rebuilt when placing, so the server never
/// trusts totals sent by the client.
/// </summary>
public sealed class CheckoutBuilder(
    IApplicationDbContext db,
    ShippingCalculator shipping,
    VoucherEvaluator vouchers,
    CoinWallet coins,
    IPaymentGatewayRegistry gateways,
    Marketing.DealsBook deals,
    ISystemParameters parameters,
    Features.Cart.PurchaseLimits purchaseLimits,
    IClock clock)
{
    private sealed record LineInfo(CartItem Item, Sku Sku, Product Product, Shop Shop, string? Variant, string? Image);

    public async Task<CheckoutPlan> BuildAsync(Guid userId, CheckoutRequest request, CancellationToken ct)
    {
        var problems = new List<string>();
        var now = clock.UtcNow;

        // ----- address -----
        var address = request.AddressId is { } addressId
            ? await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, ct)
              ?? throw new NotFoundException("Không tìm thấy địa chỉ nhận hàng.")
            : await db.Addresses.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).FirstOrDefaultAsync(ct);
        CheckoutAddressDto? addressDto = null;
        var snapshot = "{}";
        if (address is null) problems.Add("Vui lòng thêm địa chỉ nhận hàng.");
        else
        {
            var codes = new[] { address.ProvinceCode, address.DistrictCode, address.WardCode };
            var names = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, d => d.Name, ct);
            var full = string.Join(", ", new[] { address.Street, names.GetValueOrDefault(address.WardCode), names.GetValueOrDefault(address.DistrictCode),
                names.GetValueOrDefault(address.ProvinceCode) }.Where(x => !string.IsNullOrWhiteSpace(x)));
            addressDto = new CheckoutAddressDto(address.Id, address.ReceiverName, address.Phone, full, address.ProvinceCode);
            snapshot = JsonSerializer.Serialize(new
            {
                address.ReceiverName, address.Phone, address.ProvinceCode, address.DistrictCode, address.WardCode, address.Street, FullAddress = full,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        // ----- ticked cart lines -----
        var cart = await db.Carts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);
        var items = cart?.Items.Where(i => i.IsSelected).ToList() ?? [];
        var infos = await LoadLinesAsync(items, ct);
        if (infos.Count == 0) problems.Add("Chưa chọn sản phẩm nào để thanh toán.");
        var buyable = new List<LineInfo>();
        foreach (var l in infos)
        {
            var problem = l.Product.Status != ProductStatus.Active || !l.Sku.IsActive ? $"\"{l.Product.Name}\" đã ngừng bán."
                : l.Shop.Status == ShopStatus.Vacation ? $"Shop {l.Shop.Name} đang tạm nghỉ, chưa thể đặt hàng."
                : l.Shop.Status != ShopStatus.Active ? $"Shop {l.Shop.Name} đã ngừng hoạt động."
                : l.Sku.Available <= 0 ? $"\"{l.Product.Name}\" đã hết hàng."
                : l.Item.Quantity > l.Sku.Available ? $"\"{l.Product.Name}\" chỉ còn {l.Sku.Available} sản phẩm."
                : null;
            if (problem is null) buyable.Add(l);
            else problems.Add(problem);
        }
        // Giới hạn mua mỗi người: the ticked units of a product plus what the buyer already bought
        var limits = await purchaseLimits.ForAsync(userId, buyable.Select(l => l.Product.Id).Distinct().ToList(), ct);
        foreach (var g in buyable.Where(l => limits.ContainsKey(l.Product.Id)).GroupBy(l => l.Product.Id).ToList())
        {
            var limit = limits[g.Key];
            if (g.Sum(l => l.Item.Quantity) + limit.Bought <= limit.Max) continue;
            problems.Add(Features.Cart.PurchaseLimits.Message(g.First().Product.Name, limit));
            buyable.RemoveAll(l => l.Product.Id == g.Key);
        }

        // ----- shop marketing: programme prices, add-on deals, combos, gifts -----
        var offers = await deals.ForCheckoutAsync(userId, buyable.Select(l => new Marketing.DealLine(l.Sku.Id, l.Product.Id, l.Shop.Id, l.Item.Quantity)).ToList(),
            now, ct);
        problems.AddRange(offers.Problems);
        long UnitPrice(LineInfo l) => offers.Prices.TryGetValue(l.Sku.Id, out var p) ? p.UnitPrice : l.Sku.Price;

        var choices = (request.Shops ?? []).GroupBy(c => c.ShopId).ToDictionary(g => g.Key, g => g.First());
        var shops = buyable.GroupBy(l => l.Shop.Id).Select(g => g.First().Shop).OrderBy(s => s.Name).ThenBy(s => s.Id).ToList();

        // ----- shipping per shop -----
        var shippingOptions = new Dictionary<Guid, IReadOnlyList<ShippingOption>>();
        var chosenCarrier = new Dictionary<Guid, ShippingOption>();
        // Đa kho: one parcel per ship-from warehouse, each quoted on its own route; the shop's fee is their sum
        var parcelQuotes = new Dictionary<Guid, List<(PlannedParcel Parcel, IReadOnlyList<ShippingOption> Options)>>();
        foreach (var shop in shops)
        {
            var warehouses = await Parcels.WarehousesAsync(db, shop.Id, ct);
            if (warehouses.Count == 0 || address is null)
            {
                shippingOptions[shop.Id] = [];
                if (warehouses.Count == 0) problems.Add($"Shop {shop.Name} chưa có kho lấy hàng.");
                continue;
            }
            var shopLines = buyable.Where(l => l.Shop.Id == shop.Id).ToList();
            var planned = Parcels.Plan(shop.MultiWarehouse, warehouses, shopLines.Select(l => new ParcelLine(l.Product.Id, l.Product.WarehouseId,
                new ParcelItem(l.Sku.WeightG ?? l.Product.WeightG, l.Product.LengthMm, l.Product.WidthMm, l.Product.HeightMm, l.Item.Quantity))).ToList());
            var to = new RoutePoint(address.ProvinceCode, address.DistrictCode, address.WardCode);
            var quoted = new List<(PlannedParcel, IReadOnlyList<ShippingOption>)>();
            foreach (var parcel in planned)
            {
                var value = shopLines.Where(l => parcel.ProductIds.Contains(l.Product.Id)).Sum(l => UnitPrice(l) * l.Item.Quantity);
                var w = parcel.Warehouse;
                quoted.Add((parcel, await shipping.QuoteAsync(new RoutePoint(w.ProvinceCode, w.DistrictCode, w.WardCode), to, parcel.WeightG, value, ct)));
            }
            parcelQuotes[shop.Id] = quoted;
            // The shop's own carrier choice (on / COD) and each product's allowed carriers (III.3, III.9)
            var channels = await Seller.ShopChannels.ForShopAsync(db, shop.Id, ct);
            var limited = shopLines.Select(l => l.Product.CarrierCodes).Where(c => c.Count > 0).ToList();
            var options = Parcels.Combine(quoted.Select(q => q.Item2).ToList())
                .Where(o => Seller.ShopChannels.Allows(channels, o.Code) && limited.All(c => c.Contains(o.Code)))
                .Select(o => o with { SupportsCod = o.SupportsCod && Seller.ShopChannels.AllowsCod(channels, o.Code) })
                .ToList();
            shippingOptions[shop.Id] = options;
            if (options.Count == 0)
            {
                problems.Add(planned.Count > 1
                    ? $"Chưa có đơn vị vận chuyển nào giao được cả {planned.Count} kiện của shop {shop.Name} tới địa chỉ này."
                    : $"Chưa có đơn vị vận chuyển phục vụ tuyến này cho shop {shop.Name}.");
                continue;
            }
            var wanted = choices.GetValueOrDefault(shop.Id)?.CarrierCode;
            chosenCarrier[shop.Id] = options.FirstOrDefault(o => o.Code == wanted) ?? options[0];
        }
        // The chosen carrier's fee of every parcel
        var shopParcels = chosenCarrier.ToDictionary(c => c.Key, c => (IReadOnlyList<PlanParcel>)parcelQuotes[c.Key].Select(q =>
            new PlanParcel(q.Parcel.No, q.Parcel.Warehouse.Id, q.Parcel.Warehouse.Name, q.Parcel.Warehouse.ProvinceCode, q.Parcel.WeightG,
                q.Options.First(o => o.Code == c.Value.Code).Fee, q.Parcel.ProductIds)).ToList());

        // ----- vouchers -----
        var lines = buyable.Select(l => new PricingLine(l.Sku.Id, l.Product.Id, l.Product.CategoryId, l.Shop.Id, UnitPrice(l), l.Item.Quantity,
            l.Shop.FreeshipXtraSince is not null, l.Shop.VoucherXtraSince is not null)).ToList();
        var shopIds = shops.Select(s => s.Id).ToList();
        var claimed = await db.VoucherClaims.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.VoucherId).ToListAsync(ct);
        var running = await db.Vouchers.AsNoTracking()
            .Where(v => v.IsActive && v.StartAt <= now && v.EndAt > now && (v.IsPublic || claimed.Contains(v.Id))
                        && (v.Owner == VoucherOwner.Platform || (v.ShopId != null && shopIds.Contains(v.ShopId.Value))))
            .OrderBy(v => v.EndAt).ThenBy(v => v.Id).Take(200).ToListAsync(ct);

        async Task<Voucher?> Requested(string? code, Func<Voucher, bool> fits, string kind)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var v = running.FirstOrDefault(x => x.Code == code.Trim().ToUpperInvariant()) ?? await vouchers.ByCodeAsync(code, ct);
            if (v is null || !fits(v))
            {
                problems.Add($"Mã {code.Trim().ToUpperInvariant()} không phải {kind} hợp lệ.");
                return null;
            }
            if (!running.Contains(v)) running.Add(v);
            return v;
        }

        var dbProblems = new Dictionary<Guid, string?>();
        async Task<string?> DbProblem(Voucher v)
        {
            if (!dbProblems.TryGetValue(v.Id, out var p)) dbProblems[v.Id] = p = await vouchers.ProblemAsync(v, userId, ct);
            return p;
        }

        var selectedShopVouchers = new Dictionary<Guid, Voucher>();
        foreach (var shop in shops)
        {
            var v = await Requested(choices.GetValueOrDefault(shop.Id)?.VoucherCode, x => x.Owner == VoucherOwner.Shop && x.ShopId == shop.Id, "mã giảm giá của shop này");
            if (v is null) continue;
            var problem = await DbProblem(v);
            if (problem is null) selectedShopVouchers[shop.Id] = v;
            else problems.Add($"Mã {v.Code}: {problem}");
        }
        var platform = await Requested(request.PlatformVoucherCode,
            x => x.Owner == VoucherOwner.Platform && x.Type is VoucherType.Amount or VoucherType.Percent or VoucherType.CoinCashback, "mã giảm giá của sàn");
        if (platform is not null && await DbProblem(platform) is { } pp)
        {
            problems.Add($"Mã {platform.Code}: {pp}");
            platform = null;
        }
        var freeship = await Requested(request.FreeshipVoucherCode, x => x.Owner == VoucherOwner.Platform && x.Type == VoucherType.FreeShipping, "mã miễn phí vận chuyển");
        if (freeship is not null && await DbProblem(freeship) is { } fp)
        {
            problems.Add($"Mã {freeship.Code}: {fp}");
            freeship = null;
        }

        // ----- price -----
        var balance = await coins.BalanceAsync(userId, ct);
        var coinMaxBp = (int)await parameters.GetIntAsync(ParameterKeys.CoinMaxPercentBp, ct);
        PricingResult? pricing = null;
        if (lines.Count > 0)
        {
            pricing = PricingEngine.Price(new PricingInput(lines,
                chosenCarrier.ToDictionary(kv => kv.Key, kv => kv.Value.Fee),
                selectedShopVouchers.ToDictionary(kv => kv.Key, kv => PricingVoucher.From(kv.Value)),
                freeship is null ? null : PricingVoucher.From(freeship),
                platform is null ? null : PricingVoucher.From(platform),
                request.UseCoins ? balance : 0, coinMaxBp, offers.Combos));
            // A requested voucher that the engine could not apply (minimum order, scope) is reported, not silently dropped
            foreach (var outcome in pricing.Vouchers.Where(o => !o.Applied)) problems.Add($"Mã {outcome.Code}: {outcome.Problem}");
        }

        // ----- voucher options with reasons (spec 3.6: show unusable ones too) -----
        long ShopBase(Guid shopId, Voucher v) => lines.Where(l => l.ShopId == shopId && PricingVoucher.From(v).Covers(l)).Sum(l => l.LineTotal);
        long NetOf(PricingLine l) => pricing?.Shops.SelectMany(s => s.Lines).Where(p => p.Line.SkuId == l.SkuId).Select(p => p.Line.LineTotal - p.ShopDiscount).FirstOrDefault() ?? l.LineTotal;
        var totalShipping = chosenCarrier.Values.Sum(c => c.Fee);

        async Task<VoucherOptionDto> Option(Voucher v, long baseAmount, int covered, bool selected, Func<long, long>? amountFor = null)
        {
            var problem = await DbProblem(v);
            long discount = 0;
            if (problem is null)
            {
                if (v.Type == VoucherType.FreeShipping)
                {
                    problem = covered == 0 ? "Không có sản phẩm phù hợp với mã này."
                        : baseAmount < v.MinOrder ? $"Mua thêm {Domain.Common.Money.Vnd(v.MinOrder - baseAmount)} để dùng mã này."
                        : totalShipping == 0 ? "Đơn hàng không có phí vận chuyển để giảm." : null;
                    discount = problem is null ? Math.Min(totalShipping, v.MaxDiscount ?? totalShipping) : 0;
                }
                else
                {
                    var outcome = PricingEngine.Discount(PricingVoucher.From(v), covered, baseAmount);
                    problem = outcome.Problem;
                    discount = outcome.Discount;
                }
            }
            return new VoucherOptionDto(v.Id, v.Code, v.Name, v.Type, v.DiscountValue, v.DiscountPercentBp, v.MaxDiscount, v.MinOrder, v.EndAt,
                discount, problem is null, problem, selected);
        }

        var quoteShops = new List<QuoteShopDto>();
        foreach (var shop in shops)
        {
            var priced = pricing?.Shops.FirstOrDefault(s => s.ShopId == shop.Id);
            var shopOptions = new List<VoucherOptionDto>();
            foreach (var v in running.Where(v => v.Owner == VoucherOwner.Shop && v.ShopId == shop.Id))
            {
                var covered = lines.Count(l => l.ShopId == shop.Id && PricingVoucher.From(v).Covers(l));
                shopOptions.Add(await Option(v, ShopBase(shop.Id, v), covered, selectedShopVouchers.GetValueOrDefault(shop.Id)?.Id == v.Id));
            }
            var selectedOption = shopOptions.FirstOrDefault(o => o.Selected);
            var lineDtos = buyable.Where(l => l.Shop.Id == shop.Id).Select(l =>
            {
                var p = priced?.Lines.First(x => x.Line.SkuId == l.Sku.Id);
                var price = offers.Prices.GetValueOrDefault(l.Sku.Id);
                var label = price?.AddOnPromotionId is not null ? "Mua kèm"
                    : price?.Kind is Domain.Promo.PriceProgramKind.Discount ? "Giảm giá" : price?.Kind is not null ? "Flash Sale" : null;
                return new QuoteLineDto(l.Sku.Id, l.Product.Id, l.Product.Name, l.Image, l.Variant, UnitPrice(l), Math.Max(l.Sku.OriginalPrice, l.Sku.Price),
                    l.Item.Quantity, UnitPrice(l) * l.Item.Quantity, p?.ShopDiscount ?? 0, p?.PlatformDiscount ?? 0, p?.CoinDiscount ?? 0, p?.ComboDiscount ?? 0,
                    label);
            }).ToList();
            var carrier = chosenCarrier.GetValueOrDefault(shop.Id);
            quoteShops.Add(new QuoteShopDto(shop.Id, shop.Name, shop.Type == ShopType.Mall, lineDtos, shippingOptions.GetValueOrDefault(shop.Id) ?? [],
                carrier?.Code, priced?.Subtotal ?? 0, priced?.ShopDiscount ?? 0, priced?.ShippingFee ?? 0, priced?.ShippingDiscount ?? 0,
                priced?.PlatformDiscount ?? 0, priced?.CoinUsed ?? 0, priced?.GrandTotal ?? 0,
                selectedOption is { Usable: true } ? selectedOption with { Discount = priced?.ShopDiscount ?? 0 } : null, shopOptions,
                choices.GetValueOrDefault(shop.Id)?.Note?.Trim(), priced?.ComboDiscount ?? 0,
                offers.Gifts.Where(g => g.ShopId == shop.Id).Select(g => new QuoteGiftDto(g.SkuId, g.Name, g.Variant, g.Quantity, g.OriginalPrice * g.Quantity))
                    .ToList(),
                shopParcels.GetValueOrDefault(shop.Id)?.Select(p => new QuoteParcelDto(p.No, p.WarehouseName, p.ProvinceCode, p.Fee, p.ProductIds)).ToList()));
        }

        var platformOptions = new List<VoucherOptionDto>();
        var freeshipOptions = new List<VoucherOptionDto>();
        foreach (var v in running.Where(v => v.Owner == VoucherOwner.Platform))
        {
            var pv = PricingVoucher.From(v);
            var coveredLines = lines.Where(pv.Covers).ToList();
            var baseAmount = coveredLines.Sum(NetOf);
            if (v.Type == VoucherType.FreeShipping)
                freeshipOptions.Add(await Option(v, baseAmount, coveredLines.Count, freeship?.Id == v.Id));
            else
                platformOptions.Add(await Option(v, baseAmount, coveredLines.Count, platform?.Id == v.Id));
        }

        // ----- payment methods -----
        var grand = pricing?.GrandTotal ?? 0;
        var codMax = await parameters.GetIntAsync(ParameterKeys.PaymentCodMaxAmount, ct);
        var codProblem = grand > codMax ? $"Thanh toán khi nhận hàng chỉ áp dụng cho đơn đến {Domain.Common.Money.Vnd(codMax)}."
            : chosenCarrier.Values.Any(c => !c.SupportsCod) ? "Đơn vị vận chuyển đã chọn không hỗ trợ thu hộ (COD)." : null;
        var hasWallet = await db.Wallets.AnyAsync(w => w.UserId == userId, ct);
        var walletBalance = await db.LedgerAccounts.Where(a => a.OwnerType == Domain.Finance.LedgerOwnerType.Buyer && a.OwnerId == userId
                                                              && a.Type == Domain.Finance.LedgerAccountType.BuyerWallet)
            .Select(a => (long?)a.Balance).FirstOrDefaultAsync(ct) ?? 0;
        var walletProblem = !hasWallet ? "Bạn chưa kích hoạt Ví ShopHub (tạo mật khẩu ví ở Tài khoản → Ví ShopHub)."
            : walletBalance < grand ? $"Số dư Ví ShopHub ({Domain.Common.Money.Vnd(walletBalance)}) không đủ cho đơn này." : null;
        var methods = new List<PaymentMethodDto> { new(PaymentMethod.Cod, "Thanh toán khi nhận hàng", codProblem is null, codProblem) };
        // Real gateways (VNPay, MoMo) appear only when their keys are configured and an admin has not switched them off
        var enabled = await gateways.EnabledAsync(ct);
        methods.AddRange(enabled.Where(g => g.Method != PaymentMethod.Simulated).Select(g => new PaymentMethodDto(g.Method, g.DisplayName, true, null)));
        var simulatedOn = enabled.Any(g => g.Method == PaymentMethod.Simulated);
        methods.Add(new(PaymentMethod.Simulated, "Thẻ / Ví điện tử (cổng thanh toán giả lập)", simulatedOn, simulatedOn ? null : "Cổng thanh toán giả lập đang tắt."));
        methods.Add(new(PaymentMethod.Wallet, $"Ví ShopHub (số dư {Domain.Common.Money.Vnd(walletBalance)})", walletProblem is null, walletProblem));
        var method = methods.FirstOrDefault(m => m.Code == request.PaymentMethod);
        if (method is null) problems.Add("Phương thức thanh toán này hiện không khả dụng.");
        else if (!method.Available) problems.Add(method.Reason!);
        if (grand == 0 && request.PaymentMethod != PaymentMethod.Cod && lines.Count > 0)
            problems.Add("Đơn hàng 0₫ vui lòng chọn thanh toán khi nhận hàng.");

        var quote = new CheckoutQuoteDto(addressDto, quoteShops, platformOptions, freeshipOptions,
            new CoinInfoDto(balance, pricing?.CoinMax ?? 0, pricing?.CoinUsed ?? 0, request.UseCoins),
            methods, request.PaymentMethod,
            pricing?.Subtotal ?? 0, pricing?.ShopDiscount ?? 0, pricing?.ShippingFee ?? 0, pricing?.ShippingDiscount ?? 0,
            pricing?.PlatformDiscount ?? 0, pricing?.CoinUsed ?? 0, grand, pricing?.CoinCashback ?? 0,
            problems.Distinct().ToList(), problems.Count == 0 && pricing is not null, pricing?.ComboDiscount ?? 0);

        var planLines = buyable.ToDictionary(l => l.Sku.Id, l => new PlanLine(l.Sku.Id, l.Product.Id, l.Shop.Id, l.Product.CategoryId, l.Product.Name,
            l.Variant, l.Image, UnitPrice(l), Math.Max(l.Sku.OriginalPrice, l.Sku.Price), l.Item.Quantity, offers.Prices.GetValueOrDefault(l.Sku.Id)));
        return new CheckoutPlan(quote, address, snapshot, pricing, planLines, selectedShopVouchers, platform, freeship, chosenCarrier, offers.Gifts,
            shopParcels);
    }

    private async Task<List<LineInfo>> LoadLinesAsync(List<CartItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var skuIds = items.Select(i => i.SkuId).ToList();
        var rows = await (from s in db.Skus.IgnoreQueryFilters().AsNoTracking()
                          join p in db.Products.IgnoreQueryFilters().AsNoTracking() on s.ProductId equals p.Id
                          join sh in db.Shops.AsNoTracking() on p.ShopId equals sh.Id
                          where skuIds.Contains(s.Id)
                          select new
                          {
                              Sku = s,
                              Product = p,
                              Shop = sh,
                              Option1 = db.VariantOptions.Where(o => o.Id == s.Option1Id).Select(o => o.Value).FirstOrDefault(),
                              Option2 = db.VariantOptions.Where(o => o.Id == s.Option2Id).Select(o => o.Value).FirstOrDefault(),
                              OptionImage = db.VariantOptions.Where(o => o.Id == s.Option1Id).Select(o => o.ImageUrl).FirstOrDefault(),
                              Image = p.Media.Where(m => m.Type == MediaType.Image).OrderBy(m => m.SortOrder).Select(m => m.Url).FirstOrDefault(),
                          }).ToListAsync(ct);
        return items.Join(rows, i => i.SkuId, r => r.Sku.Id, (i, r) =>
        {
            var variant = string.Join(", ", new[] { r.Option1, r.Option2 }.Where(v => !string.IsNullOrEmpty(v)));
            return new LineInfo(i, r.Sku, r.Product, r.Shop, variant.Length == 0 ? null : variant, r.OptionImage ?? r.Image);
        }).OrderBy(l => l.Shop.Name).ThenBy(l => l.Sku.Id).ToList();
    }
}
