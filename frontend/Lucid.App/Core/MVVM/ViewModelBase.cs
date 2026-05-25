using CommunityToolkit.Mvvm.ComponentModel;

namespace Lucid.Core.MVVM;

/// <summary>
/// Base class for all page ViewModels.
///
/// Inheriting from CommunityToolkit's ObservableObject gives us:
///   • INotifyPropertyChanged via [ObservableProperty] source generators
///   • SetProperty() helpers with equality checks
///   • OnPropertyChanged() overrides
///
/// IsBusy and StatusMessage are common enough to live here; pages that
/// don't need them simply ignore them.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// True while an async operation is in flight.
    /// Bind loading spinners or button-disabled states to this.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Human-readable status line (empty string means no status).
    /// Never set this to a fear-based or jargon-heavy string.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Called once after the page is navigated to and the ViewModel is ready.
    /// Override to perform initial data loading without blocking the constructor.
    /// </summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;
}
