using System.Net;
using System.Net.Sockets;
using ShopHub.Application.Abstractions;
using ShopHub.Domain.Common;

namespace ShopHub.Infrastructure.Media;

/// <summary>
/// Downloads product images named in import sheets. The address check runs in ConnectCallback — on the IP actually
/// dialled, for every connection including redirects — so a host that resolves (or re-resolves) to an internal
/// address is refused. https only, ≤ 5 MB, 10 s.
/// </summary>
public sealed class RemoteImageFetcher(IHttpClientFactory factory) : IRemoteImageFetcher
{
    public const string HttpClientName = "remote-images";
    public const int MaxBytes = 5 * 1024 * 1024;

    public async Task<byte[]> FetchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new BusinessRuleException("Link ảnh phải là địa chỉ https đầy đủ.");
        try
        {
            using var response = await factory.CreateClient(HttpClientName).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new BusinessRuleException($"Không tải được ảnh (mã {(int)response.StatusCode}).");
            if (response.Content.Headers.ContentLength > MaxBytes) throw new BusinessRuleException("Ảnh lớn hơn 5 MB.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBytes) throw new BusinessRuleException("Ảnh lớn hơn 5 MB.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            throw new BusinessRuleException("Link ảnh trỏ tới địa chỉ nội bộ, không được phép.");
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Không kết nối được tới máy chủ chứa ảnh.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BusinessRuleException("Tải ảnh quá thời gian cho phép (10 giây).");
        }
    }

    /// <summary>The handler behind the named client: dials only public addresses.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var target = addresses.FirstOrDefault(IsPublic) ?? throw new BlockedAddressException();
            if (addresses.Any(a => !IsPublic(a))) throw new BlockedAddressException();
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>False for loopback, private (RFC 1918 / ULA), link-local, CGNAT, multicast, unspecified and documentation ranges.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal
                     || address.ToString().StartsWith("2001:db8", StringComparison.OrdinalIgnoreCase));
        var b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0
                 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                 || (b[0] == 192 && b[1] == 0 && b[2] == 2) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                 || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                 || b[0] >= 224);
    }

    private sealed class BlockedAddressException() : Exception("Blocked internal address");
}
