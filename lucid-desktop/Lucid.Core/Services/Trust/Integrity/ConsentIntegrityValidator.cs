namespace Lucid.Services.Trust.Integrity;

/// <summary>
/// Validates that the trust/consent posture loaded from settings.json has
/// not been tampered with outside the normal UI consent flows.
///
/// Used at startup by AppServices before the automation/consent systems are
/// initialized. If tamper is detected, <see cref="TrustPostureRecoveryManager"/>
/// is invoked to restore safe defaults.
/// </summary>
public sealed class ConsentIntegrityValidator
{
    private readonly TrustIntegrityService _integrity;

    public ConsentIntegrityValidator(TrustIntegrityService integrity)
    {
        _integrity = integrity;
    }

    /// <summary>
    /// Validates the current settings against the stored integrity signature.
    ///
    /// Returns <c>true</c> if the posture is intact (or uninitialized — first run).
    /// Returns <c>false</c> if a tamper is detected. The caller should invoke
    /// <see cref="TrustPostureRecoveryManager.RecoverToSafeDefaults"/> in that case.
    /// </summary>
    public bool ValidatePosture(string consentMode, string automationMode)
    {
        return _integrity.Verify(consentMode, automationMode);
    }

    /// <summary>
    /// Records a baseline signature for the current posture.
    /// Call after the user legitimately changes consent settings through the UI.
    /// </summary>
    public void RecordPosture(string consentMode, string automationMode)
    {
        _integrity.Sign(consentMode, automationMode);
    }
}
