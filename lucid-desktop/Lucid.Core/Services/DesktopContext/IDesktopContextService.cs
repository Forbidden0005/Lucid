namespace Lucid.Services.DesktopContext;

public interface IDesktopContextService
{
    WorkflowContextSnapshot CurrentSnapshot { get; }

    /// <summary>True while capture is active (user has opted in and Start() ran).</summary>
    bool IsRunning { get; }

    event EventHandler<ContextChangedEventArgs>? ContextChanged;
    void Start();
    void Stop();
}
