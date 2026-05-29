using System.Net;
using System.Net.Sockets;

namespace Lucid.Services.Trust.EndpointValidation;

/// <summary>
/// Validates that a URL targets only local machine (loopback) endpoints.
///
/// Enforces Lucid's "nothing ever leaves this machine" trust guarantee for
/// LLM inference and any other network-bound service.
///
/// Classification logic:
///   • localhost / 127.x.x.x / ::1 → Local (allowed)
///   • 10.x / 172.16-31.x / 192.168.x → PrivateLan (denied — LAN can observe traffic)
///   • Everything else → Remote (denied — public internet)
///   • Unresolvable hostname → Unknown (denied)
///
/// This is the enforcement point. All consumers should call
/// <see cref="Validate"/> before constructing HTTP clients.
/// </summary>
public static class LocalEndpointValidator
{
    // Covers all textual forms of the loopback interface.
    private static readonly HashSet<string> _localHostnames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "::1",
            "[::1]",
        };

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates <paramref name="url"/> against the local-only policy.
    ///
    /// Returns an <see cref="EndpointValidationResult"/> that is allowed only
    /// if the URL targets a loopback address.
    ///
    /// The <see cref="EndpointValidationResult.Reason"/> on a denial is
    /// user-readable and safe to display in the UI.
    /// </summary>
    public static EndpointValidationResult Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return EndpointValidationResult.Deny(
                url ?? string.Empty,
                EndpointTrustClassification.Unknown,
                "Endpoint URL is empty or not configured.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return EndpointValidationResult.Deny(
                url,
                EndpointTrustClassification.Unknown,
                $"The value '{url}' is not a valid absolute URI.");

        if (uri.Scheme is not ("http" or "https"))
            return EndpointValidationResult.Deny(
                url,
                EndpointTrustClassification.Unknown,
                $"Unsupported scheme '{uri.Scheme}'. Only http/https are accepted.");

        var classification = ClassifyHost(uri.Host);

        return classification == EndpointTrustClassification.Local
            ? EndpointValidationResult.Allow(url, classification)
            : EndpointValidationResult.Deny(
                url,
                classification,
                $"The endpoint '{uri.Host}' is not a local address. " +
                $"Lucid only connects to local inference servers to ensure your data never " +
                $"leaves your machine. Change the LLM endpoint to http://localhost:11434.");
    }

    /// <summary>
    /// Returns <paramref name="url"/> if it passes local-only validation,
    /// or <paramref name="fallback"/> if it does not — without throwing.
    ///
    /// Useful at construction sites that must always produce a valid URL.
    /// </summary>
    public static string EnforceOrFallback(
        string? url,
        string  fallback = "http://localhost:11434")
    {
        var result = Validate(url);
        return result.IsAllowed ? url! : fallback;
    }

    // ── Classification ─────────────────────────────────────────────────────────

    private static EndpointTrustClassification ClassifyHost(string host)
    {
        // Direct loopback hostname match
        if (_localHostnames.Contains(host))
            return EndpointTrustClassification.Local;

        // Try IP-address parse — handles 127.x.x.x, IPv6, private ranges, etc.
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
                return EndpointTrustClassification.Local;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (b[0] == 10)
                    return EndpointTrustClassification.PrivateLan;
                // 172.16.0.0/12
                if (b[0] == 172 && b[1] is >= 16 and <= 31)
                    return EndpointTrustClassification.PrivateLan;
                // 192.168.0.0/16
                if (b[0] == 192 && b[1] == 168)
                    return EndpointTrustClassification.PrivateLan;
            }

            // Link-local IPv6 (fe80::/10) — still local-ish, but deny for safety
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 &&
                ip.IsIPv6LinkLocal)
                return EndpointTrustClassification.PrivateLan;

            return EndpointTrustClassification.Remote;
        }

        // DNS hostname that is NOT "localhost" → treat as remote
        // (We do not resolve DNS here — that could be spoofed.)
        return EndpointTrustClassification.Remote;
    }
}
