using System.Net;
using System.Net.Sockets;

namespace ShopHub.Api.Security;

/// <summary>
/// "Inside the private network": a call that did not come through the gateway (no X-Forwarded-For) from loopback or a
/// private address (another container, the host's own monitoring). Used for diagnostics that must not be public.
/// </summary>
public static class NetworkScope
{
    public static bool IsInternal(HttpContext http)
    {
        if (http.Request.Headers.ContainsKey("X-Forwarded-For")) return false;
        var ip = http.Connection.RemoteIpAddress;
        // In-process calls (test host) have no remote address
        if (ip is null) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6UniqueLocal || ip.IsIPv6LinkLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
