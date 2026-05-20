using Microsoft.Win32;
using System.Management;

namespace ExplainMyPC.Services.Security;

/// <summary>
/// Reads the status of Windows built-in security features.
/// All reads are local — no cloud, no network calls.
///
/// Methods are deliberately lenient: a WMI/registry failure returns a
/// "Status unknown" result rather than throwing, so a single failed query
/// doesn't abort the entire scan.
/// </summary>
internal static class WindowsSecurityStatusReader
{
    internal static WindowsSecurityStatus Read()
    {
        return new WindowsSecurityStatus(
            Defender:        ReadDefender(),
            Firewall:        ReadFirewall(),
            SmartScreen:     ReadSmartScreen(),
            SecureBoot:      ReadSecureBoot(),
            Tpm:             ReadTpm(),
            BitLocker:       ReadBitLocker(),
            MemoryIntegrity: ReadMemoryIntegrity(),
            ReadAt:          DateTimeOffset.Now);
    }

    // ── Windows Defender ──────────────────────────────────────────────────────

    private static SecurityFeatureStatus ReadDefender()
    {
        const string name = "Windows Defender";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2",
                "SELECT * FROM AntiVirusProduct WHERE displayName LIKE '%Microsoft%' OR displayName LIKE '%Defender%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                uint state = (uint)obj["productState"];
                // productState encodes enabled/disabled in bits 12-19 and up-to-date in bits 4-11
                bool enabled = ((state >> 12) & 0xF) != 0;
                bool upToDate = ((state >> 4) & 0xF) == 0;

                string detail = enabled
                    ? (upToDate ? "Real-time protection active" : "Definitions may be outdated")
                    : "Real-time protection is off";

                return new SecurityFeatureStatus(name, enabled, enabled && upToDate, detail);
            }

            // No Defender entry found — may be replaced by third-party AV
            return new SecurityFeatureStatus(name, false, false,
                "Defender not registered — may be replaced by third-party AV");
        }
        catch
        {
            // Fallback: check Defender service
            try
            {
                using var svc = new System.ServiceProcess.ServiceController("WinDefend");
                bool running = svc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
                return new SecurityFeatureStatus(name, running, running,
                    running ? "Service running" : "Service not running");
            }
            catch
            {
                return new SecurityFeatureStatus(name, false, false, "Status unavailable");
            }
        }
    }

    // ── Windows Firewall ──────────────────────────────────────────────────────

    private static SecurityFeatureStatus ReadFirewall()
    {
        const string name = "Windows Firewall";
        try
        {
            // Check Domain, Private, and Public profiles via registry
            const string key = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
            bool domain  = ReadFirewallProfile(key + @"\DomainProfile");
            bool std     = ReadFirewallProfile(key + @"\StandardProfile");
            bool pub     = ReadFirewallProfile(key + @"\PublicProfile");

            bool allOn = domain && std && pub;
            bool anyOn = domain || std || pub;

            string detail = allOn ? "All profiles active"
                : anyOn ? "Some profiles disabled"
                : "All profiles disabled";

            return new SecurityFeatureStatus(name, anyOn, allOn, detail);
        }
        catch
        {
            return new SecurityFeatureStatus(name, false, false, "Status unavailable");
        }
    }

    private static bool ReadFirewallProfile(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue("EnableFirewall") is int v && v == 1;
        }
        catch { return false; }
    }

    // ── SmartScreen ───────────────────────────────────────────────────────────

    private static SecurityFeatureStatus ReadSmartScreen()
    {
        const string name = "SmartScreen";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer");
            string? val = key?.GetValue("SmartScreenEnabled")?.ToString();

            bool enabled = val is null || !val.Equals("Off", StringComparison.OrdinalIgnoreCase);
            string detail = val switch
            {
                "RequireAdmin" => "Requires admin approval",
                "Prompt"       => "Warns before running",
                "Off"          => "Disabled",
                _              => "Enabled"
            };

            return new SecurityFeatureStatus(name, enabled, enabled, detail);
        }
        catch
        {
            return new SecurityFeatureStatus(name, true, true, "Status unavailable — assumed enabled");
        }
    }

    // ── Secure Boot ───────────────────────────────────────────────────────────

    private static SecurityFeatureStatus ReadSecureBoot()
    {
        const string name = "Secure Boot";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            bool enabled = key?.GetValue("UEFISecureBootEnabled") is int v && v == 1;
            return new SecurityFeatureStatus(name, enabled, enabled,
                enabled ? "Enabled" : "Disabled or not supported");
        }
        catch
        {
            return new SecurityFeatureStatus(name, false, false, "Status unavailable");
        }
    }

    // ── TPM ───────────────────────────────────────────────────────────────────

    private static SecurityFeatureStatus ReadTpm()
    {
        const string name = "TPM";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2\Security\MicrosoftTpm", "SELECT * FROM Win32_Tpm");

            foreach (ManagementObject obj in searcher.Get())
            {
                bool present = true;
                bool enabled = obj["IsEnabled"] is bool e && e;
                bool activated = obj["IsActivated"] is bool a && a;
                string version = obj["SpecVersion"]?.ToString() ?? "unknown";

                string detail = enabled && activated
                    ? $"TPM {version} — active"
                    : "TPM present but not fully active";

                return new SecurityFeatureStatus(name, present, enabled && activated, detail);
            }

            return new SecurityFeatureStatus(name, false, false, "No TPM detected");
        }
        catch
        {
            return new SecurityFeatureStatus(name, false, false, "Status unavailable");
        }
    }

    // ── BitLocker ─────────────────────────────────────────────────────────────

    private static SecurityFeatureStatus ReadBitLocker()
    {
        const string name = "BitLocker";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2\Security\MicrosoftVolumeEncryption",
                "SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter = 'C:'");

            foreach (ManagementObject obj in searcher.Get())
            {
                uint status = (uint)obj["ProtectionStatus"];
                // 0 = unprotected, 1 = protected, 2 = protection suspended
                bool protected_ = status == 1;
                string detail = status switch
                {
                    1 => "C: drive encrypted",
                    2 => "Encryption suspended",
                    _ => "C: drive not encrypted"
                };
                return new SecurityFeatureStatus(name, protected_, protected_, detail);
            }

            return new SecurityFeatureStatus(name, false, false, "Not enabled on C:");
        }
        catch
        {
            return new SecurityFeatureStatus(name, false, false, "Status unavailable");
        }
    }

    // ── Memory Integrity (HVCI) ───────────────────────────────────────────────

    private static SecurityFeatureStatus ReadMemoryIntegrity()
    {
        const string name = "Memory Integrity";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            bool enabled = key?.GetValue("Enabled") is int v && v == 1;
            return new SecurityFeatureStatus(name, enabled, enabled,
                enabled ? "Core isolation active" : "Not enabled");
        }
        catch
        {
            return new SecurityFeatureStatus(name, false, false, "Status unavailable");
        }
    }
}
