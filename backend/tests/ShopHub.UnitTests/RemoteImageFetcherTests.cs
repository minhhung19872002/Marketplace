using System.Net;
using FluentAssertions;
using ShopHub.Domain.Common;
using ShopHub.Infrastructure.Media;

namespace ShopHub.UnitTests;

/// <summary>Image links in import sheets must never let the server reach its own network (SSRF).</summary>
public class RemoteImageFetcherTests
{
    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(RemoteImageFetcher.CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) };
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]   // cloud metadata
    [InlineData("100.64.0.1", false)]        // CGNAT
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Only_public_addresses_are_dialled(string address, bool allowed) =>
        RemoteImageFetcher.IsPublic(IPAddress.Parse(address)).Should().Be(allowed);

    [Theory]
    [InlineData("https://127.0.0.1/a.png")]
    [InlineData("https://localhost/a.png")]
    [InlineData("https://[::1]/a.png")]
    public async Task A_link_to_the_machine_itself_is_refused_before_any_request(string url)
    {
        var act = () => new RemoteImageFetcher(new Factory()).FetchAsync(url, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("nội bộ");
    }

    [Theory]
    [InlineData("http://example.com/a.png")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("a.png")]
    public async Task Only_absolute_https_links_are_accepted(string url)
    {
        var act = () => new RemoteImageFetcher(new Factory()).FetchAsync(url, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("https");
    }
}
