using System.Net;
using SkiaSharp;

namespace ShopHub.IntegrationTests.Infrastructure;

/// <summary>Stands in for image hosts on the internet during the tests.</summary>
public sealed class FakeImageHostHandler : HttpMessageHandler
{
    public static byte[] Png(int w = 600, int h = 600)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor((byte)Random.Shared.Next(256), 140, 90));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 90).ToArray();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(request.RequestUri!.Host switch
    {
        "img.test" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) },
        "big.test" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[6 * 1024 * 1024]) },
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    });
}
