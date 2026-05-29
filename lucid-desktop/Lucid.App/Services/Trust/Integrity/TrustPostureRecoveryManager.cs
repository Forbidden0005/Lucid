using Lucid.Services.Settings;

namespace Lucid.Services.Trust.Integrity;

/// <summary>
/// Restores safe consent defaults when a trust posture tamper is detected.
///
/// Safe defaults:
///   ConsentMode    = "AskForMediumAndHighRisk"  (most restrictive ask)
///   AutomationMode = "ConfirmBeforeAction"       (no silent automation)
///
/// Recovery behaviour:
///   1. Override in-memory settings snapshot to safe values.
///   2. Re-sign the integrity envelope with the safe defaults.
///   3. Log the tamper detection event.
///   4. Return the recovered settings so the rest of initialization proceeds safely.
///
/// The recovery does NOT write back to settings.json — it only resets the
/// in-memory posture for this session. The user can explicitly re-configure
/// via the UI, which will re-sign the envelope.
/// </summary>
public sealed class TrustPostureRecoveryManager
{
    // Safe defaults — the most restrictive posture Lucid supports.
    public const string SafeConsentMode    = "AskForMediumAndHighRisk";
    public const string SafeAutomationMode = "ConfirmBeforeAction";

    private readonly ConsentIntegrityValidator _validator;

    public TrustPostureRecoveryManager(ConsentIntegrityValidator validator)
    {
        _validator = validator;
    }

    /// <summary>
    /// Validates the given settings and returns them unchanged if intact,
    /// or returns a safe-default replacement if tamper is detected.
    ///
    /// Always call this during startup after loading settings.
    /// </summary>
    /// <param name="settings">Settings loaded from disk.</param>
    /// <param name="tamperDetected">Set to true if recovery was applied.</param>
    public AppSettings ValidateOrRecover(AppSettings settings, out bool tamperDetected)
    {
        var ok = _validator.ValidatePosture(settings.ConsentMode, settings.AutomationMode);

        if (ok)
        {
            tamperDetected = false;
            return settings;
        }

        // Tamper detected — override consent posture with safe defaults.
        tamperDetected = true;

        var recovered = settings with
        {
            ConsentMode    = SafeConsentMode,
            AutomationMode = SafeAutomationMode,
        };

        // Re-sign with the safe defaults so next session starts clean.
        _validator.RecordPosture(SafeConsentMode, SafeAutomationMode);

        return recovered;
    }

    /// <summary>
    /// Records a fresh baseline signature for the given posture.
    /// Call from the Settings UI after the user legitimately changes consent.
    /// </summary>
    public void CommitPosture(string consentMode, string automationMode)
    {
        _validator.RecordPosture(consentMode, automationMode);
    }
}
