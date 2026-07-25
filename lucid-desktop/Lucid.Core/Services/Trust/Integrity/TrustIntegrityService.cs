using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lucid.Services.Trust.Integrity;

/// <summary>
/// Generates and verifies HMAC-SHA256 signatures for the trust/consent posture.
///
/// Key derivation:
///   The signing key is derived from the machine GUID stored in the Windows
///   registry. This makes the signature machine-specific — an envelope created
///   on machine A cannot be validated on machine B — without requiring any
///   user-supplied secrets.
///
/// File layout:
///   %LOCALAPPDATA%\Lucid\settings.integrity  — JSON-serialized <see cref="SignedTrustEnvelope"/>
///
/// Resilience:
///   If the key cannot be derived (e.g. registry read fails), the service
///   operates in "unsigned" mode — no signature is written or verified. This
///   prevents a registry access failure from blocking startup.
/// </summary>
public sealed class TrustIntegrityService
{
    private static readonly string IntegrityFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucid", "settings.integrity");

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
    };

    // ── Signing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a signed envelope for the given consent posture to disk.
    /// No-op if key derivation fails.
    /// </summary>
    public void Sign(string consentMode, string automationMode)
    {
        var key = DeriveKey();
        if (key is null) return;

        var envelope = new SignedTrustEnvelope
        {
            ConsentMode    = consentMode,
            AutomationMode = automationMode,
            SignedAtTicks  = DateTime.UtcNow.Ticks,
        };

        envelope.Hmac = ComputeHmac(key, envelope.CanonicalPayload());

        try
        {
            var dir = Path.GetDirectoryName(IntegrityFilePath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(envelope, _json);
            var tmp  = IntegrityFilePath + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Move(tmp, IntegrityFilePath, overwrite: true);
        }
        catch
        {
            // Best-effort — a failed write does not block the app.
        }
    }

    // ── Verification ──────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the integrity file against the given current posture.
    ///
    /// Returns true if:
    ///   a) No integrity file exists yet (first run — not yet signed).
    ///   b) File exists and HMAC matches.
    ///
    /// Returns false (tamper detected) when:
    ///   c) File exists, HMAC does not match, or posture values differ.
    /// </summary>
    public bool Verify(string consentMode, string automationMode)
    {
        if (!File.Exists(IntegrityFilePath))
            return true; // First run — no baseline to compare against.

        var key = DeriveKey();
        if (key is null) return true; // Can't derive key — operate unsigned.

        try
        {
            var json = File.ReadAllText(IntegrityFilePath, Encoding.UTF8);
            var envelope = JsonSerializer.Deserialize<SignedTrustEnvelope>(json, _json);
            if (envelope is null) return false;

            // Re-derive HMAC over the stored payload fields.
            var expected = ComputeHmac(key, envelope.CanonicalPayload());
            if (!CryptographicEquals(expected, envelope.Hmac)) return false;

            // Also verify the live values match what was signed.
            if (!string.Equals(envelope.ConsentMode, consentMode, StringComparison.Ordinal))
                return false;
            if (!string.Equals(envelope.AutomationMode, automationMode, StringComparison.Ordinal))
                return false;

            return true;
        }
        catch
        {
            return false; // Parse failure → treat as tampered.
        }
    }

    // ── Key derivation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Derives an HMAC key from the machine GUID via SHA-256.
    /// Returns null if the machine GUID cannot be read.
    /// </summary>
    private static byte[]? DeriveKey()
    {
        try
        {
            var machineGuid = ReadMachineGuid();
            if (string.IsNullOrEmpty(machineGuid)) return null;

            var material = $"lucid-trust-{machineGuid}";
            return SHA256.HashData(Encoding.UTF8.GetBytes(material));
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    // ── HMAC helpers ──────────────────────────────────────────────────────────

    private static string ComputeHmac(byte[] key, string payload)
    {
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Constant-time comparison to prevent timing side-channels.</summary>
    private static bool CryptographicEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}
