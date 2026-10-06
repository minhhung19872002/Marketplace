using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopHub.Api.Common;
using ShopHub.Application.Features.Seo;

namespace ShopHub.Api.Controllers;

/// <summary>
/// Crawler-facing output (spec 6.6). The gateway sends known bots' page requests to <c>render</c> and serves
/// /sitemap.xml, /sitemaps/*.xml and /robots.txt from here.
/// </summary>
[Route("api/seo")]
public sealed class SeoController : ApiControllerBase
{
    /// <summary>Server-rendered HTML of a buyer-site path (product, category, shop, search, static page, home).</summary>
    [HttpGet("render")]
    [AllowAnonymous]
    [Produces("text/html")]
    public async Task<IActionResult> Render([FromQuery] string? path, CancellationToken ct)
    {
        var query = Request.Query.Where(q => q.Key != "path").ToDictionary(q => q.Key, q => q.Value.ToString());
        var page = await Sender.Send(new SeoRenderQuery(path ?? "/", query), ct);
        if (page.RedirectTo is { } to) return RedirectPermanent(to);
        Response.Headers.CacheControl = "public, max-age=600";
        return new ContentResult { Content = page.Html, ContentType = "text/html; charset=utf-8", StatusCode = page.StatusCode };
    }

    [HttpGet("sitemap.xml")]
    [AllowAnonymous]
    [Produces("application/xml")]
    public async Task<IActionResult> Sitemap(CancellationToken ct) => Xml(await Sender.Send(new SitemapIndexQuery(), ct));

    [HttpGet("sitemaps/{name}.xml")]
    [AllowAnonymous]
    [Produces("application/xml")]
    public async Task<IActionResult> SitemapFile(string name, CancellationToken ct) =>
        await Sender.Send(new SitemapQuery(name), ct) is { } xml ? Xml(xml) : NotFound();

    [HttpGet("robots.txt")]
    [AllowAnonymous]
    [Produces("text/plain")]
    public async Task<IActionResult> Robots(CancellationToken ct) =>
        new ContentResult { Content = await Sender.Send(new RobotsQuery(), ct), ContentType = "text/plain; charset=utf-8" };

    private ContentResult Xml(string xml)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return new ContentResult { Content = xml, ContentType = "application/xml; charset=utf-8" };
    }
}
