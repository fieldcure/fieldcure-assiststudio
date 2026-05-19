using System.Net;
using System.Net.Sockets;

namespace AnthropicSdkSample.Tools;

/// <summary>
/// SSRF guard: resolves DNS and blocks connections to private/loopback IPs.
/// Adapted from FieldCure.Mcp.Essentials. Keeps the sample's <c>fetch</c> tool from
/// becoming a pivot into an internal network if a model is tricked into requesting it.
/// </summary>
internal static class SsrfGuard
{
    /// <summary>
    /// Returns an error message if the URI resolves to a private/loopback address, null if allowed.
    /// </summary>
    public static async Task<string?> CheckAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            foreach (var addr in addresses)
            {
                if (IPAddress.IsLoopback(addr)
                    || (addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv6LinkLocal)
                    || (addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv6SiteLocal)
                    || IsPrivateIpv4(addr))
                {
                    return $"SSRF blocked: {uri.Host} resolves to private address {addr}.";
                }
            }
        }
        catch (SocketException ex)
        {
            return $"DNS resolution failed for {uri.Host}: {ex.Message}";
        }

        return null;
    }

    /// <summary>
    /// Validates that a URL is absolute and uses http or https scheme.
    /// Returns the parsed Uri or null with an error message.
    /// </summary>
    public static (Uri? uri, string? error) ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return (null, "Invalid URL. Only http:// and https:// are allowed.");
        }

        return (uri, null);
    }

    /// <summary>
    /// Checks whether an IPv4 address belongs to a private or link-local range.
    /// </summary>
    private static bool IsPrivateIpv4(IPAddress addr)
    {
        if (addr.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = addr.GetAddressBytes();
        return bytes[0] == 10
               || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
               || (bytes[0] == 192 && bytes[1] == 168)
               || (bytes[0] == 169 && bytes[1] == 254); // link-local
    }
}
