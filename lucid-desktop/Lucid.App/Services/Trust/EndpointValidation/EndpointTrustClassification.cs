namespace Lucid.Services.Trust.EndpointValidation;

/// <summary>
/// Trust classification for a network endpoint URL.
/// Used by <see cref="LocalEndpointValidator"/> to enforce local-only
/// connectivity for all Lucid inference and data services.
/// </summary>
public enum EndpointTrustClassification
{
    /// <summary>Loopback address (localhost, 127.0.0.1, ::1). Always allowed.</summary>
    Local,

    /// <summary>RFC-1918 private LAN address (192.168.x, 10.x, 172.16-31.x). Not allowed — could expose data on LAN.</summary>
    PrivateLan,

    /// <summary>Public internet or routable address. Never allowed.</summary>
    Remote,

    /// <summary>Could not determine — treat as untrusted.</summary>
    Unknown,
}
