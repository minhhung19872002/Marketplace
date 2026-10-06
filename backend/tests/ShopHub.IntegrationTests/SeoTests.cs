using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 13 SEO (spec 6.6): crawler HTML with JSON-LD, canonical URLs, sitemap files, robots.txt.</summary>
[Collection(ApiCollection.Name)]
public class SeoTests(ApiFactory factory)
{
    private HttpClient NoRedirect() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static IEnumerable<JsonElement> JsonLd(string html) =>
        Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
            .Select(m => JsonDocument.Parse(m.Groups[1].Value).RootElement);

    [Fact]
    public async Task A_product_page_for_crawlers_has_meta_open_graph_canonical_and_product_json_ld()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Ấm Pha Trà <Gốm> \"Bát Tràng\"", "Đèn Bàn", 245_000, 8, "Việt Nam")]);
        var productId = store.Products.Single().Value;
        var (slug, shopId) = await factory.WithDbAsync(async db =>
        {
            var p = await db.Products.AsNoTracking().SingleAsync(x => x.Id == productId);
            return (p.Slug, p.ShopId);
        });
        var canonical = $"/san-pham/{slug}-i.{shopId}.{productId}";
        var client = NoRedirect();

        // The short URL moves permanently to the canonical one
        var old = await client.GetAsync($"/api/seo/render?path=/san-pham/{productId}");
        old.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        old.Headers.Location!.ToString().Should().Be(canonical);

        var res = await client.GetAsync($"/api/seo/render?path={Uri.EscapeDataString(canonical)}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("<title>Ấm Pha Trà &lt;Gốm&gt; &quot;Bát Tràng&quot;", "giá trị được mã hoá HTML");
        html.Should().NotContain("<Gốm>");
        html.Should().Contain($"<link rel=\"canonical\" href=\"http://localhost:18000{canonical}\">");
        html.Should().Contain("<meta property=\"og:type\" content=\"product\">");
        html.Should().Contain("<meta property=\"og:image\" content=\"https://example.invalid/a.webp\">");
        html.Should().Contain("₫245.000");

        var product = JsonLd(html).Single(j => j.GetProperty("@type").GetString() == "Product");
        product.GetProperty("name").GetString().Should().Be("Ấm Pha Trà <Gốm> \"Bát Tràng\" " + store.Marker);
        product.GetProperty("offers").GetProperty("price").GetInt64().Should().Be(245_000);
        product.GetProperty("offers").GetProperty("priceCurrency").GetString().Should().Be("VND");
        product.GetProperty("offers").GetProperty("availability").GetString().Should().Be("https://schema.org/InStock");
        JsonLd(html).Should().Contain(j => j.GetProperty("@type").GetString() == "BreadcrumbList");
        // No AggregateRating without reviews (an empty rating would be invalid structured data)
        product.TryGetProperty("aggregateRating", out _).Should().BeFalse();

        var missing = await client.GetAsync($"/api/seo/render?path=/san-pham/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("noindex");
    }

    [Fact]
    public async Task Category_shop_search_and_static_pages_render_and_search_is_not_indexed()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Đèn Ngủ Gỗ", "Đèn Bàn", 199_000, 8, "Việt Nam")]);
        var client = NoRedirect();
        var (categorySlug, shopSlug) = await factory.WithDbAsync(async db =>
        {
            var p = await db.Products.AsNoTracking().SingleAsync(x => x.Id == store.Products.Single().Value);
            var c = await db.Categories.AsNoTracking().SingleAsync(x => x.Id == p.CategoryId);
            var s = await db.Shops.AsNoTracking().SingleAsync(x => x.Id == store.ShopId);
            return (c.Slug, s.Slug);
        });
        var category = await (await client.GetAsync($"/api/seo/render?path=/danh-muc/{categorySlug}")).Content.ReadAsStringAsync();
        category.Should().Contain("<h1>Đèn Bàn</h1>").And.Contain("BreadcrumbList");
        var shop = await (await client.GetAsync($"/api/seo/render?path=/shop/{shopSlug}")).Content.ReadAsStringAsync();
        shop.Should().Contain($"Shop {store.Marker}").And.Contain("\"@type\":\"Store\"");
        var search = await (await client.GetAsync("/api/seo/render?path=/tim-kiem&q=den%20ngu")).Content.ReadAsStringAsync();
        search.Should().Contain("noindex,follow").And.Contain("Kết quả tìm kiếm cho \"den ngu\"");
        var legal = await (await client.GetAsync("/api/seo/render?path=/trang/chinh-sach-bao-mat")).Content.ReadAsStringAsync();
        legal.Should().Contain("Nghị định 13/2023/NĐ-CP");
    }

    [Fact]
    public async Task The_sitemap_index_lists_chunked_files_holding_canonical_urls_and_robots_points_to_it()
    {
        var store = await factory.CreateStoreAsync("79", products: [new("Bình Hoa Sứ", "Đèn Bàn", 150_000, 8, "Việt Nam")]);
        var productId = store.Products.Single().Value;
        var client = factory.CreateClient();
        var index = XDocument.Parse(await client.GetStringAsync("/api/seo/sitemap.xml"));
        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var files = index.Descendants(sm + "loc").Select(l => l.Value).ToList();
        files.Should().Contain(f => f.EndsWith("/sitemaps/products-1.xml")).And.Contain(f => f.EndsWith("/sitemaps/categories.xml"));

        var products = XDocument.Parse(await client.GetStringAsync("/api/seo/sitemaps/products-1.xml"));
        var urls = products.Descendants(sm + "loc").Select(l => l.Value).ToList();
        urls.Count.Should().BeLessThanOrEqualTo(50_000);
        urls.Should().Contain(u => u.EndsWith($".{store.ShopId}.{productId}") && u.Contains("/san-pham/"));
        (await client.GetAsync("/api/seo/sitemaps/khong-co.xml")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var robots = await client.GetStringAsync("/api/seo/robots.txt");
        robots.Should().Contain("Disallow: /tai-khoan/").And.Contain("Sitemap: http://localhost:18000/sitemap.xml");
    }
}
