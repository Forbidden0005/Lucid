namespace Lucid.Services.DesktopContext;

public interface IDesktopContextService
{
    WorkflowContextSnapshot CurrentSnapshot { get; }
    event EventHandler<ContextChangedEventArgs>? ContextChanged;
    void Start();
    void Stop();
}
