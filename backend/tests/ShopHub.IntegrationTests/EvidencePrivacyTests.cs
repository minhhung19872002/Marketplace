using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Media;
using ShopHub.Infrastructure.Media;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>Phase 14 B2: what people upload never leaks — video metadata stripped, return evidence private with signed links.</summary>
[Collection(ApiCollection.Name)]
public class EvidencePrivacyTests(ApiFactory factory)
{
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string purpose, byte[] bytes, string name, string type)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new(type);
        form.Add(file, "file", name);
        return await client.PostAsync($"/api/media/{purpose}", form);
    }

    [Fact]
    public void Stripping_blanks_every_metadata_box_without_moving_a_byte_of_the_rest()
    {
        var video = Mp4Fixture.Build();
        var inspector = new Mp4VideoInspector();
        var clean = inspector.StripMetadata(video);
        clean.Length.Should().Be(video.Length, "không đổi kích thước, không dịch offset của mdat");
        Encoding.ASCII.GetString(clean).Should().NotContain(Mp4Fixture.Gps).And.NotContain("Nha rieng");
        inspector.GetDurationMs(clean).Should().Be(3000);
        clean.AsSpan(clean.Length - 64).ToArray().Should().OnlyContain(b => b == 0x11, "dữ liệu hình giữ nguyên");
    }

    [Fact]
    public async Task A_review_video_is_stored_without_the_place_it_was_filmed()
    {
        var buyer = await factory.CreateUserAsync();
        var res = await UploadAsync(buyer.Client, "review", Mp4Fixture.Build(), "clip.mp4", "video/mp4");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var url = (await res.ReadEnvelopeAsync()).Data.Str("url");
        var stored = await new HttpClient().GetByteArrayAsync(url);
        Encoding.ASCII.GetString(stored).Should().NotContain(Mp4Fixture.Gps);
    }

    [Fact]
    public async Task Return_evidence_is_private_and_reachable_only_through_a_short_signed_link()
    {
        var buyer = await factory.CreateUserAsync();
        var res = await UploadAsync(buyer.Client, "evidence", FakeImageHostHandler.Png(300, 300), "bang-chung.png", "image/png");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var data = (await res.ReadEnvelopeAsync()).Data;
        var id = Guid.Parse(data.Str("id"));
        var asset = await factory.WithDbAsync(db => db.MediaAssets.AsNoTracking().SingleAsync(a => a.Id == id));
        asset.Bucket.Should().Be(Buckets.Returns);

        var signed = data.Str("url");
        signed.Should().Contain("X-Amz-Signature", "chỉ phát liên kết ký có hạn");
        using var http = new HttpClient();
        (await http.GetAsync(signed)).StatusCode.Should().Be(HttpStatusCode.OK);
        var unsigned = signed[..signed.IndexOf('?')];
        (await http.GetAsync(unsigned)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "bucket riêng tư không cho đọc ẩn danh");
    }

    [Fact]
    public async Task Evidence_uploaded_to_the_public_bucket_before_the_fix_moves_to_the_private_one_and_its_old_link_dies()
    {
        var buyer = await factory.CreateUserAsync();
        var key = $"evidence/2026/09/{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            foreach (var size in Application.Features.Media.ImageSizes.Public)
                await storage.PutAsync(Buckets.Reviews, Application.Features.Media.ImageSizes.Key(key, size), FakeImageHostHandler.Png(50, 50), "image/webp", CancellationToken.None);
        }
        var asset = new MediaAsset(buyer.Id, MediaKind.Image, "evidence", Buckets.Reviews, key, "image/webp", 100, 300, 300, null, DateTimeOffset.UtcNow);
        await factory.WithDbAsync(async db =>
        {
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync();
            return 0;
        });
        var oldLink = $"{factory.MediaBaseUrl}/{Buckets.Reviews}/{Application.Features.Media.ImageSizes.Key(key, 1200)}";
        using var http = new HttpClient();
        (await http.GetAsync(oldLink)).StatusCode.Should().Be(HttpStatusCode.OK, "trước khi chuyển: công khai");

        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<EvidenceRelocation>().RunAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);

        (await factory.WithDbAsync(db => db.MediaAssets.AsNoTracking().Where(a => a.Id == asset.Id).Select(a => a.Bucket).SingleAsync())).Should().Be(Buckets.Returns);
        (await http.GetAsync(oldLink)).StatusCode.Should().Be(HttpStatusCode.NotFound, "liên kết công khai cũ không còn mở được");
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IObjectStorage>().ExistsAsync(Buckets.Returns, Application.Features.Media.ImageSizes.Key(key, 1200), CancellationToken.None))
                .Should().BeTrue();
    }
}
