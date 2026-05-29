namespace Lucid.Services.Infrastructure.Startup;

/// <summary>
/// Runs an async initialization task from a sync context without risking
/// a deadlock on the WinUI UI thread.
///
/// Problem:
///   WinUI 3 uses a synchronization context. Calling
///   <c>asyncTask.GetAwaiter().GetResult()</c> directly on the UI thread
///   can deadlock when the async method captures the context and tries to
///   resume on it — but the thread is blocked waiting for the result.
///
/// Solution:
///   Schedule the work on the thread-pool (<c>Task.Run</c>) so no sync
///   context is captured, then block on that outer task.
///
/// Usage:
///   <code>
///   StartupTimeoutGuard.Run("SQLite init", () => dbService.InitializeAsync());
///   </code>
///
/// The timeout prevents a hung initialization from freezing the splash screen
/// indefinitely. On timeout the initialization is abandoned (not cancelled —
/// the task continues running in the background) and a degraded snapshot is
/// returned so the app can still start.
/// </summary>
public static class StartupTimeoutGuard
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs <paramref name="asyncFactory"/> on the thread-pool and blocks
    /// until it completes or <paramref name="timeout"/> elapses.
    ///
    /// Returns a <see cref="StartupHealthSnapshot"/> describing the outcome.
    /// Never throws — all exceptions are captured in the snapshot.
    /// </summary>
    public static StartupHealthSnapshot Run(
        string    stepName,
        Func<Task> asyncFactory,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var sw    = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Task.Run escapes any captured sync context — safe to block on from UI thread.
            var task = Task.Run(asyncFactory);
            var completed = task.Wait(limit);
            sw.Stop();

            if (!completed)
            {
                return new StartupHealthSnapshot(
                    StepName:     stepName,
                    Succeeded:    false,
                    Elapsed:      sw.Elapsed,
                    ErrorMessage: $"Initialization timed out after {limit.TotalSeconds:0}s. " +
                                  $"The service will continue in degraded mode.");
            }

            if (task.IsFaulted)
            {
                return new StartupHealthSnapshot(
                    StepName:     stepName,
                    Succeeded:    false,
                    Elapsed:      sw.Elapsed,
                    ErrorMessage: task.Exception?.GetBaseException().Message ?? "Unknown error");
            }

            return new StartupHealthSnapshot(
                StepName:  stepName,
                Succeeded: true,
                Elapsed:   sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new StartupHealthSnapshot(
                StepName:     stepName,
                Succeeded:    false,
                Elapsed:      sw.Elapsed,
                ErrorMessage: ex.Message);
        }
    }
}
