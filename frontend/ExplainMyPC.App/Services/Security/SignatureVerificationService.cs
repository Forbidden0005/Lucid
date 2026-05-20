using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ExplainMyPC.Services.Security;

/// <summary>
/// Verifies Authenticode signatures on Windows executables.
///
/// Strategy:
///   1. Try <see cref="X509Certificate.CreateFromSignedFile"/> — managed,
///      reads the embedded certificate without full chain validation.
///   2. Cache results keyed by (path, last-write-time) so every file is
///      only read from disk once per session.
///
/// This is an observation tool — it reports what signature is present,
/// not whether the chain is trusted to a specific root. Callers use the
/// publisher name to classify trust level, not the chain result.
///
/// Thread safety: all public methods lock the cache. Safe to call from
/// background threads.
/// </summary>
public sealed class SignatureVerificationService
{
    private readonly Dictionary<string, CachedResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private sealed record CachedResult(
        bool   IsSigned,
        string Publisher,
        DateTime LastWriteUtc);

    // ── Known Microsoft publisher names (CN field of signing cert) ────────────
    private static readonly HashSet<string> s_microsoftPublishers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Corporation",
        "Microsoft Windows",
        "Microsoft Windows Publisher",
        "Microsoft Code Signing PCA",
        "Microsoft Windows Component Publisher",
    };

    // ── Known trusted vendors ─────────────────────────────────────────────────
    private static readonly HashSet<string> s_knownVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Google LLC", "Google Inc",
        "Mozilla Corporation",
        "Valve Corp", "Valve Corporation",
        "Discord Inc.", "Discord Technology, Inc.",
        "Slack Technologies", "Slack Technologies, Inc.",
        "Zoom Video Communications, Inc.",
        "Adobe Inc.", "Adobe Systems Incorporated",
        "Spotify AB",
        "Dropbox, Inc.",
        "Apple Inc.",
        "NVIDIA Corporation",
        "Advanced Micro Devices, Inc.",
        "Intel Corporation",
        "Dell Inc.",
        "Lenovo",
        "HP Inc.",
        "ASUS",
        "Logitech Inc",
        "Razer Inc.",
        "Corsair Memory, Inc.",
        "Malwarebytes Corporation",
        "Avast Software s.r.o.",
        "AVG Technologies CZ, s.r.o.",
        "ESET, spol. s r.o.",
        "Bitdefender SRL",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns (IsSigned, Publisher) for the given file path.
    /// Unsigned or unreadable files return (false, "").
    /// Results are cached by path + last-write-time.
    /// </summary>
    public (bool IsSigned, string Publisher) Verify(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (false, string.Empty);

        DateTime lastWrite;
        try { lastWrite = File.GetLastWriteTimeUtc(path); }
        catch { return (false, string.Empty); }

        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached) &&
                cached.LastWriteUtc == lastWrite)
                return (cached.IsSigned, cached.Publisher);
        }

        var result = ReadSignature(path);

        lock (_lock)
        {
            _cache[path] = new CachedResult(result.IsSigned, result.Publisher, lastWrite);
        }

        return (result.IsSigned, result.Publisher);
    }

    /// <summary>Classifies a publisher string into a trust level.</summary>
    public static TrustLevel ClassifyPublisher(string publisher, string filePath)
    {
        if (string.IsNullOrEmpty(publisher))
            return ClassifyUnsigned(filePath);

        if (s_microsoftPublishers.Contains(publisher) ||
            publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return TrustLevel.TrustedMicrosoft;

        if (s_knownVendors.Contains(publisher))
            return TrustLevel.SignedKnownVendor;

        // Signed by someone — treat as known vendor (signed = some accountability)
        return TrustLevel.SignedKnownVendor;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static (bool IsSigned, string Publisher) ReadSignature(string path)
    {
        try
        {
            var cert = X509Certificate.CreateFromSignedFile(path);
            // Parse CN from the subject name
            var subject = cert.Subject;
            string cn = ParseCn(subject);
            return (true, cn);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    private static string ParseCn(string subject)
    {
        // Subject format: "CN=Microsoft Corporation, O=..., C=US"
        foreach (var part in subject.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..].Trim().Trim('"');
        }
        return subject;
    }

    private static TrustLevel ClassifyUnsigned(string path)
    {
        if (string.IsNullOrEmpty(path)) return TrustLevel.Unsigned;

        var lower = path.ToLowerInvariant();

        // High-risk locations for unsigned executables
        if (lower.Contains(@"\temp\") || lower.Contains(@"\tmp\") ||
            lower.Contains(@"\appdata\local\temp") ||
            lower.Contains(@"\downloads\"))
            return TrustLevel.Suspicious;

        return TrustLevel.Unsigned;
    }
}
