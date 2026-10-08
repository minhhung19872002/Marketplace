using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Features.Storefront;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Shops;

namespace ShopHub.Application.Features.Seo;

/// <summary>Canonical buyer-site URLs (spec II.4: /san-pham/:slug-i.:shopId.:productId; /san-pham/:id still works).</summary>
public static partial class SeoPaths
{
    public static string Product(string slug, Guid shopId, Guid productId) => $"/san-pham/{slug}-i.{shopId}.{productId}";

    public static string Category(string slug) => $"/danh-muc/{slug}";

    public static string Shop(string slug) => $"/shop/{slug}";

    [GeneratedRegex("([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$")]
    private static partial Regex TrailingGuid();

    /// <summary>The product id at the end of either URL form.</summary>
    public static Guid? ProductIdOf(string key) => TrailingGuid().Match(key) is { Success: true } m ? Guid.Parse(m.Value) : null;
}

public record SeoPage(int StatusCode, string Html, string? RedirectTo = null);

/// <summary>Server-rendered page for crawlers: path (as the buyer site routes it) + its query string.</summary>
public record SeoRenderQuery(string Path, IReadOnlyDictionary<string, string> Query) : IRequest<SeoPage>;

/// <summary>
/// HTML for crawlers (spec 6.6): title, meta description, Open Graph, canonical, JSON-LD (Product + Offer +
/// AggregateRating, BreadcrumbList, Organization) and the page's real content as plain links / text. The gateway
/// sends known bots here; people get the SPA. Every value is HTML-encoded; JSON-LD is serialised (no raw strings).
/// </summary>
public sealed class SeoRenderHandler(ISender sender, IApplicationDbContext db, ISystemParameters parameters) : IRequestHandler<SeoRenderQuery, SeoPage>
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // Escapes only what HTML needs (<, >, &, quotes); Vietnamese letters stay readable instead of &#224;
    private static readonly System.Text.Encodings.Web.HtmlEncoder Html =
        System.Text.Encodings.Web.HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All);

    private static string E(string? s) => Html.Encode(s ?? "");

    private static string Vnd(long v) => $"₫{v.ToString("N0", Vi)}";

    private static string Plain(string html, int max)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    private sealed record Page(string Title, string Description, string Canonical, string? Image, string OgType, string Body, IReadOnlyList<object> JsonLd,
        bool Index = true);

    public async Task<SeoPage> Handle(SeoRenderQuery request, CancellationToken ct)
    {
        var site = (await parameters.GetStringAsync(ParameterKeys.SitePublicUrl, ct)).TrimEnd('/');
        var name = await parameters.GetStringAsync(ParameterKeys.SitePlatformName, ct);
        var path = "/" + request.Path.Trim().Trim('/');
        try
        {
            Page? page = path switch
            {
                "/" => await HomeAsync(name, ct),
                _ when path.StartsWith("/san-pham/", StringComparison.Ordinal) => await ProductAsync(path, site, ct) is var (p, redirect) && redirect is not null
                    ? throw new RedirectException(redirect)
                    : p,
                _ when path.StartsWith("/danh-muc/", StringComparison.Ordinal) => await CategoryAsync(path["/danh-muc/".Length..], site, ct),
                _ when path.StartsWith("/shop/", StringComparison.Ordinal) => await ShopAsync(path["/shop/".Length..], ct),
                "/tim-kiem" => await SearchAsync(request.Query.GetValueOrDefault("q") ?? "", ct),
                _ when path.StartsWith("/trang/", StringComparison.Ordinal) || path.StartsWith("/tro-giup/", StringComparison.Ordinal) =>
                    await CmsAsync(path, ct),
                _ => null,
            };
            if (page is null) return new SeoPage(404, Document(name, site, NotFound(path)));
            return new SeoPage(200, Document(name, site, page));
        }
        catch (RedirectException r)
        {
            return new SeoPage(301, "", r.To);
        }
        catch (NotFoundException)
        {
            return new SeoPage(404, Document(name, site, NotFound(path)));
        }
    }

    private sealed class RedirectException(string to) : Exception
    {
        public string To { get; } = to;
    }

    private static Page NotFound(string path) =>
        new("Không tìm thấy trang", "Trang bạn tìm không tồn tại hoặc đã bị gỡ.", path, null, "website", "<h1>Không tìm thấy trang</h1>", [], Index: false);

    private static string Cards(IEnumerable<ProductCardDto> items) =>
        "<ul class=\"products\">" + string.Concat(items.Select(p =>
            $"<li><a href=\"{E(SeoPaths.Product(p.Slug, p.ShopId, p.Id))}\">{E(p.Name)}</a> — {E(Vnd(p.MinPrice))}" +
            (p.RatingCount > 0 ? $" · {p.RatingAvg.ToString("0.0", Vi)}★ ({p.RatingCount})" : "") + "</li>")) + "</ul>";

    private static object BreadcrumbLd(string site, IEnumerable<(string Name, string Path)> items) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = items.Select((c, i) => new Dictionary<string, object>
        {
            ["@type"] = "ListItem", ["position"] = i + 1, ["name"] = c.Name, ["item"] = site + c.Path,
        }).ToList(),
    };

    private static string Crumbs(IEnumerable<(string Name, string Path)> items) =>
        "<nav class=\"breadcrumb\">" + string.Join(" › ", items.Select(c => $"<a href=\"{E(c.Path)}\">{E(c.Name)}</a>")) + "</nav>";

    private async Task<Page> HomeAsync(string name, CancellationToken ct)
    {
        var hidden = await Features.Catalog.CategoryVisibility.HiddenIdsAsync(db, ct);
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive && c.ParentId == null && !hidden.Contains(c.Id)).OrderBy(c => c.SortOrder)
            .Select(c => new { c.Name, c.Slug }).ToListAsync(ct);
        var top = await sender.Send(new SearchProductsQuery(Sort: ProductSort.BestSelling, PageSize: 60), ct);
        var body = $"<h1>{E(name)} — Mua sắm online giá tốt</h1>" +
                   "<h2>Danh mục</h2><ul>" + string.Concat(categories.Select(c => $"<li><a href=\"{E(SeoPaths.Category(c.Slug))}\">{E(c.Name)}</a></li>")) + "</ul>" +
                   "<h2>Bán chạy</h2>" + Cards(top.Items);
        return new Page($"{name} — Mua sắm online giá tốt", $"{name}: hàng nghìn sản phẩm chính hãng, Flash Sale mỗi ngày, giao hàng toàn quốc, thanh toán khi nhận hàng.",
            "/", null, "website", body, []);
    }

    private async Task<(Page? Page, string? Redirect)> ProductAsync(string path, string site, CancellationToken ct)
    {
        var key = path["/san-pham/".Length..];
        if (SeoPaths.ProductIdOf(key) is not { } id) throw new NotFoundException("Không tìm thấy sản phẩm.");
        var p = await sender.Send(new GetProductPageQuery(id), ct);
        var canonical = SeoPaths.Product(p.Slug, p.Shop.Id, p.Id);
        // /san-pham/:id (and stale slugs) move permanently to the canonical URL
        if (!string.Equals(path, canonical, StringComparison.Ordinal)) return (null, canonical);

        var images = p.Media.Where(m => m.Type == "Image").Select(m => m.Url).ToList();
        var crumbs = new List<(string, string)> { ("Trang chủ", "/") };
        crumbs.AddRange(p.Breadcrumb.Select(c => (c.Name, SeoPaths.Category(c.Slug))));
        crumbs.Add((p.Name, canonical));
        var description = Plain(p.Description, 160);
        var offer = new Dictionary<string, object>
        {
            ["@type"] = p.MinPrice == p.MaxPrice ? "Offer" : "AggregateOffer",
            ["priceCurrency"] = "VND",
            ["availability"] = p.TotalAvailable > 0 ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
            ["url"] = site + canonical,
            ["seller"] = new Dictionary<string, object> { ["@type"] = "Organization", ["name"] = p.Shop.Name },
        };
        if (p.MinPrice == p.MaxPrice) offer["price"] = p.MinPrice;
        else
        {
            offer["lowPrice"] = p.MinPrice;
            offer["highPrice"] = p.MaxPrice;
        }
        var product = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = p.Name,
            ["description"] = Plain(p.Description, 5000),
            ["image"] = images,
            ["sku"] = p.Id.ToString(),
            ["url"] = site + canonical,
            ["itemCondition"] = p.Condition.ToString() == "New" ? "https://schema.org/NewCondition" : "https://schema.org/UsedCondition",
            ["offers"] = offer,
        };
        if (p.BrandName is { } brand) product["brand"] = new Dictionary<string, object> { ["@type"] = "Brand", ["name"] = brand };
        if (p.RatingCount > 0)
            product["aggregateRating"] = new Dictionary<string, object>
            {
                ["@type"] = "AggregateRating", ["ratingValue"] = Math.Round(p.RatingAvg, 1), ["reviewCount"] = p.RatingCount, ["bestRating"] = 5, ["worstRating"] = 1,
            };
        var body = Crumbs(crumbs) +
                   $"<h1>{E(p.Name)}</h1>" +
                   $"<p class=\"price\">{E(p.MinPrice == p.MaxPrice ? Vnd(p.MinPrice) : $"{Vnd(p.MinPrice)} – {Vnd(p.MaxPrice)}")}</p>" +
                   (p.RatingCount > 0 ? $"<p>{p.RatingAvg.ToString("0.0", Vi)}/5 từ {p.RatingCount} đánh giá · Đã bán {p.SoldCount}</p>" : $"<p>Đã bán {p.SoldCount}</p>") +
                   string.Concat(images.Take(4).Select(u => $"<img src=\"{E(u)}\" alt=\"{E(p.Name)}\" width=\"400\" height=\"400\">")) +
                   $"<p>Shop: <a href=\"{E(SeoPaths.Shop(p.Shop.Slug))}\">{E(p.Shop.Name)}</a></p>" +
                   "<h2>Thông số</h2><ul>" + string.Concat(p.Attributes.Select(a => $"<li>{E(a.Name)}: {E(a.Value)}</li>")) + "</ul>" +
                   // Already sanitised by the API before it was stored (HtmlSanitizer)
                   $"<h2>Mô tả sản phẩm</h2><div class=\"description\">{p.Description}</div>";
        return (new Page($"{p.Name} | {p.Shop.Name}", description.Length > 0 ? description : p.Name, canonical, images.FirstOrDefault(), "product", body,
            [product, BreadcrumbLd(site, crumbs)]), null);
    }

    private async Task<Page> CategoryAsync(string slug, string site, CancellationToken ct)
    {
        var c = await sender.Send(new GetCategoryBySlugQuery(slug), ct);
        var products = await sender.Send(new SearchProductsQuery(CategoryId: c.Category.Id, Sort: ProductSort.BestSelling, PageSize: 60), ct);
        var crumbs = new List<(string, string)> { ("Trang chủ", "/") };
        crumbs.AddRange(c.Breadcrumb.Select(b => (b.Name, SeoPaths.Category(b.Slug))));
        var body = Crumbs(crumbs) + $"<h1>{E(c.Category.Name)}</h1>" +
                   (c.Children.Count > 0 ? "<ul>" + string.Concat(c.Children.Select(x => $"<li><a href=\"{E(SeoPaths.Category(x.Slug))}\">{E(x.Name)}</a></li>")) + "</ul>" : "") +
                   $"<p>{products.TotalCount} sản phẩm</p>" + Cards(products.Items);
        return new Page($"{c.Category.Name} giá tốt, chính hãng", $"Mua {c.Category.Name} chính hãng giá tốt: {products.TotalCount} sản phẩm, giao hàng toàn quốc.",
            SeoPaths.Category(c.Category.Slug), products.Items.FirstOrDefault()?.ImageUrl, "website", body, [BreadcrumbLd(site, crumbs)]);
    }

    private async Task<Page> ShopAsync(string slug, CancellationToken ct)
    {
        var s = await sender.Send(new GetShopPageQuery(slug), ct);
        var products = await sender.Send(new SearchProductsQuery(ShopId: s.Shop.Id, Sort: ProductSort.BestSelling, PageSize: 60), ct);
        var org = new Dictionary<string, object> { ["@context"] = "https://schema.org", ["@type"] = "Store", ["name"] = s.Shop.Name };
        if (s.Shop.LogoUrl is { } logo) org["logo"] = logo;
        var body = $"<h1>{E(s.Shop.Name)}</h1><p>{E(Plain(s.Description, 500))}</p>" +
                   $"<p>{s.Shop.ProductCount} sản phẩm · {s.Shop.FollowerCount} người theo dõi</p>" + Cards(products.Items);
        return new Page($"{s.Shop.Name} — Shop trên ShopHub", Plain(s.Description, 160) is { Length: > 0 } d ? d : $"Sản phẩm của {s.Shop.Name}",
            SeoPaths.Shop(s.Shop.Slug), s.Shop.LogoUrl ?? s.CoverUrl, "website", body, [org]);
    }

    private async Task<Page> SearchAsync(string q, CancellationToken ct)
    {
        q = q.Trim();
        if (q.Length > 100) q = q[..100];
        var result = await sender.Send(new SearchProductsQuery(Q: q, PageSize: 60), ct);
        var body = $"<h1>Kết quả tìm kiếm cho \"{E(q)}\"</h1><p>{result.TotalCount} sản phẩm</p>" + Cards(result.Items);
        // Result pages are crawlable for links but not indexed themselves (thin, endless variations)
        return new Page($"{q} — giá tốt", $"{result.TotalCount} sản phẩm {q} giá tốt trên ShopHub.", $"/tim-kiem?q={Uri.EscapeDataString(q)}", null, "website", body, [],
            Index: false);
    }

    private async Task<Page?> CmsAsync(string path, CancellationToken ct)
    {
        var slug = path[(path.IndexOf('/', 1) + 1)..];
        var page = await db.CmsPages.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, ct);
        return page is null ? null
            : new Page(page.Title, Plain(page.Content, 160), path, null, "article", $"<h1>{E(page.Title)}</h1>{page.Content}", []);
    }

    private static string Document(string siteName, string site, Page p)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>{E(p.Title)} | {E(siteName)}</title>");
        sb.Append($"<meta name=\"description\" content=\"{E(p.Description)}\">");
        sb.Append($"<meta name=\"robots\" content=\"{(p.Index ? "index,follow" : "noindex,follow")}\">");
        sb.Append($"<link rel=\"canonical\" href=\"{E(site + p.Canonical)}\">");
        sb.Append($"<meta property=\"og:site_name\" content=\"{E(siteName)}\">");
        sb.Append($"<meta property=\"og:type\" content=\"{E(p.OgType)}\">");
        sb.Append($"<meta property=\"og:title\" content=\"{E(p.Title)}\">");
        sb.Append($"<meta property=\"og:description\" content=\"{E(p.Description)}\">");
        sb.Append($"<meta property=\"og:url\" content=\"{E(site + p.Canonical)}\">");
        sb.Append("<meta property=\"og:locale\" content=\"vi_VN\">");
        if (p.Image is { } img) sb.Append($"<meta property=\"og:image\" content=\"{E(img)}\">");
        foreach (var ld in p.JsonLd)
            // The default encoder escapes <, > and & so the JSON cannot close the script element
            sb.Append($"<script type=\"application/ld+json\">{JsonSerializer.Serialize(ld, Json)}</script>");
        sb.Append("</head><body>");
        sb.Append($"<header><a href=\"/\">{E(siteName)}</a></header><main>{p.Body}</main>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}

// ---------- sitemap.xml (≤ 50.000 URLs per file) & robots.txt ----------

public record SitemapIndexQuery : IRequest<string>;

public record SitemapQuery(string Name) : IRequest<string?>;

public record RobotsQuery : IRequest<string>;

public static class Sitemaps
{
    public const int UrlsPerFile = 50_000;
}

public sealed class SitemapHandlers(IApplicationDbContext db, ISystemParameters parameters, IClock clock)
    : IRequestHandler<SitemapIndexQuery, string>, IRequestHandler<SitemapQuery, string?>, IRequestHandler<RobotsQuery, string>
{
    private async Task<string> SiteAsync(CancellationToken ct) => (await parameters.GetStringAsync(ParameterKeys.SitePublicUrl, ct)).TrimEnd('/');

    private static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    private IQueryable<Domain.Catalog.Product> Products() => ProductCards.Visible(db).AsNoTracking();

    public async Task<string> Handle(SitemapIndexQuery request, CancellationToken ct)
    {
        var site = await SiteAsync(ct);
        var files = (int)Math.Ceiling(await Products().CountAsync(ct) / (double)Sitemaps.UrlsPerFile);
        var names = new List<string> { "pages", "categories", "shops" };
        names.AddRange(Enumerable.Range(1, Math.Max(files, 1)).Select(i => $"products-{i}"));
        var now = clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
               string.Concat(names.Select(n => $"<sitemap><loc>{X($"{site}/sitemaps/{n}.xml")}</loc><lastmod>{now}</lastmod></sitemap>")) + "</sitemapindex>";
    }

    public async Task<string?> Handle(SitemapQuery request, CancellationToken ct)
    {
        var site = await SiteAsync(ct);
        IReadOnlyList<(string Path, DateTimeOffset? At)> urls;
        if (request.Name == "pages")
        {
            var cms = await db.CmsPages.AsNoTracking().Where(p => p.IsPublished)
                .Select(p => new { p.Kind, p.Slug, p.UpdatedAt }).ToListAsync(ct);
            urls = [("/", null), ("/tro-giup", null), .. cms.Select(p => ((p.Kind == Domain.SystemConfig.CmsKind.Page ? "/trang/" : "/tro-giup/") + p.Slug, (DateTimeOffset?)p.UpdatedAt))];
        }
        else if (request.Name == "categories")
        {
            var hidden = await Features.Catalog.CategoryVisibility.HiddenIdsAsync(db, ct);
            urls = (await db.Categories.AsNoTracking().Where(c => c.IsActive && !hidden.Contains(c.Id)).OrderBy(c => c.Slug).Select(c => c.Slug).ToListAsync(ct))
                .Select(s => (SeoPaths.Category(s), (DateTimeOffset?)null)).ToList();
        }
        else if (request.Name == "shops")
            urls = (await db.Shops.AsNoTracking().Where(s => s.Status == ShopStatus.Active || s.Status == ShopStatus.Vacation).OrderBy(s => s.Slug)
                    .Select(s => new { s.Slug, s.UpdatedAt }).Take(Sitemaps.UrlsPerFile).ToListAsync(ct))
                .Select(s => (SeoPaths.Shop(s.Slug), s.UpdatedAt)).ToList();
        else if (request.Name.StartsWith("products-", StringComparison.Ordinal) && int.TryParse(request.Name["products-".Length..], out var n) && n >= 1)
            urls = (await Products().OrderBy(p => p.Id).Skip((n - 1) * Sitemaps.UrlsPerFile).Take(Sitemaps.UrlsPerFile)
                    .Select(p => new { p.Slug, p.ShopId, p.Id, At = p.UpdatedAt ?? p.PublishedAt }).ToListAsync(ct))
                .Select(p => (SeoPaths.Product(p.Slug, p.ShopId, p.Id), p.At)).ToList();
        else return null;

        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var (path, at) in urls)
        {
            sb.Append("<url><loc>").Append(X(site + path)).Append("</loc>");
            if (at is { } t) sb.Append("<lastmod>").Append(t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</lastmod>");
            sb.Append("</url>");
        }
        return sb.Append("</urlset>").ToString();
    }

    public async Task<string> Handle(RobotsQuery request, CancellationToken ct) =>
        $"User-agent: *\nDisallow: /api/\nDisallow: /seller/\nDisallow: /admin/\nDisallow: /tai-khoan/\nDisallow: /gio-hang\nDisallow: /thanh-toan\n" +
        $"Disallow: /chat\nAllow: /\n\nSitemap: {await SiteAsync(ct)}/sitemap.xml\n";
}
