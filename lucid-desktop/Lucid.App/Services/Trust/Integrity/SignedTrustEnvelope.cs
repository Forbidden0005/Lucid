namespace Lucid.Services.Trust.Integrity;

/// <summary>
/// Serializable envelope that wraps the trust/consent posture fields with
/// an HMAC-SHA256 signature to detect file tampering.
///
/// Stored alongside settings.json as <c>settings.integrity</c>.
/// On load, <see cref="ConsentIntegrityValidator"/> re-derives the HMAC and
/// compares it to <see cref="Hmac"/>. A mismatch causes the system to fail
/// safe to the most restrictive consent defaults.
///
/// THREAT MODEL:
///   This is tamper-detection, not cryptographic secrecy. The goal is to
///   detect naive file edits that try to relax consent requirements. It does
///   not protect against an attacker who can read %LOCALAPPDATA% (they could
///   derive the same key). It protects against accidental or scripted edits
///   to settings.json that bypass the UI consent flows.
/// </summary>
public sealed class SignedTrustEnvelope
{
    /// <summary>Snapshot of the consent-mode string at signing time.</summary>
    public string ConsentMode { get; set; } = string.Empty;

    /// <summary>Snapshot of the automation-mode string at signing time.</summary>
    public string AutomationMode { get; set; } = string.Empty;

    /// <summary>UTC tick count when this envelope was written.</summary>
    public long SignedAtTicks { get; set; }

    /// <summary>HMAC-SHA256 hex string over the canonical payload.</summary>
    public string Hmac { get; set; } = string.Empty;

    /// <summary>
    /// Produces the canonical string that is HMAC-signed.
    /// Order and separator are fixed — changing either invalidates all signatures.
    /// </summary>
    public string CanonicalPayload() =>
        $"consent={ConsentMode}|automation={AutomationMode}|ts={SignedAtTicks}";
}
