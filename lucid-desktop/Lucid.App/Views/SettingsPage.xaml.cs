using CommunityToolkit.Mvvm.DependencyInjection;
using Lucid.Services.Automation;
using Lucid.Services.Trust;
using Lucid.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lucid.Views;

/// <summary>
/// Settings page — thin code-behind. All state and logic lives in SettingsViewModel.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();

        ViewModel = Ioc.Default.GetRequiredService<SettingsViewModel>();

        Loaded   += (_, _) => ViewModel.Initialize();
        Unloaded += (_, _) => ViewModel.Cleanup();
    }
}
