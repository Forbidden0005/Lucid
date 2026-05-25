using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucid.Core.MVVM;

namespace Lucid.ViewModels;

/// <summary>
/// ViewModel for the Privacy page.
///
/// Phase 5: SecurityService will surface:
///   • App permission usage (camera, microphone, location)
///   • Telemetry settings status
///   • Browser data exposure indicators
///
/// Per security-model.md: diagnostics are local-first.
/// No personal data leaves the device without explicit user consent.
/// </summary>
public partial class PrivacyViewModel : ViewModelBase
{
    [ObservableProperty] private int    _exposureCount   = 0;
    [ObservableProperty] private string _privacySummary  =
        "Scan to review app permissions and privacy exposure on this device.";

    [RelayCommand]
    private async Task RunPrivacyScanAsync()
    {
        IsBusy        = true;
        StatusMessage = "Scanning…";
        await Task.Delay(300);
        IsBusy        = false;
        StatusMessage = "Privacy scanning available in Phase 5.";
    }
}
