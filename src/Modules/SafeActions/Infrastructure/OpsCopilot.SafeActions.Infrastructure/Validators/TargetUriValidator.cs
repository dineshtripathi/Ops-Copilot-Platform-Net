using System.Net;
using System.Net.Sockets;

namespace OpsCopilot.SafeActions.Infrastructure.Validators;

/// <summary>
/// Validates target URIs for outbound HTTP probes.
/// Rejects SSRF vectors: non-HTTPS, localhost, private/link-local IP ranges,
/// Azure IMDS, and *.internal hostnames.
/// </summary>
internal sealed class TargetUriValidator
{
    /// <summary>Validates the URI and returns whether it is safe to probe.</summary>
    public (bool IsValid, string? Reason) Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "url must not be null or whitespace");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, $"url is not a valid absolute URI: {url}");

        // ── HTTPS only ──────────────────────────────────────────────────
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return (false, $"only HTTPS is allowed; got {uri.Scheme}");

        var host = uri.Host;

        // ── Blocked hostnames ───────────────────────────────────────────
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return (false, "localhost is blocked");

        if (host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return (false, "*.internal hostnames are blocked");

        // ── IP-literal checks ───────────────────────────────────────────
        if (IPAddress.TryParse(host, out var ip))
        {
            var reason = CheckBlockedIp(ip);
            if (reason is not null)
                return (false, reason);
        }

        // ── DNS resolution check (best-effort) ─────────────────────────
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var addr in addresses)
            {
                var reason = CheckBlockedIp(addr);
                if (reason is not null)
                    return (false, $"DNS for {host} resolved to blocked IP: {reason}");
            }
        }
        catch (SocketException)
        {
            // DNS failure — cannot reach the host, let the caller decide
            return (false, $"DNS resolution failed for {host}");
        }

        return (true, null);
    }

    // ── Private / link-local / loopback / IMDS checks ───────────────────

    private static string? CheckBlockedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return $"loopback address {ip} is blocked";

        // IPv6 link-local
        if (ip.IsIPv6LinkLocal)
            return $"IPv6 link-local address {ip} is blocked";

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
                return $"private IP {ip} (10.0.0.0/8) is blocked";

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return $"private IP {ip} (172.16.0.0/12) is blocked";

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return $"private IP {ip} (192.168.0.0/16) is blocked";

            // 169.254.0.0/16 — link-local (includes Azure IMDS 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254)
                return $"link-local/IMDS address {ip} is blocked";
        }

        return null;
    }
}
