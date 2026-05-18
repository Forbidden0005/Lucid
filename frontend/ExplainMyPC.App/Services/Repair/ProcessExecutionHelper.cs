using System.Diagnostics;
using System.Text;
using ExplainMyPC.Services.Execution;

namespace ExplainMyPC.Services.Repair;

/// <summary>
/// Shared utility for running Windows system tools with live output streaming
/// and graceful cancellation.
///
/// Design:
///   • All output lines (stdout + stderr) are streamed to the caller's log
///     in real time via the <see cref="ActionExecutionLog"/> callback.  This
///     means every line appears in the UI as the tool produces it — not just
///     after it finishes.
///   • Cancellation kills the entire process tree so child processes (e.g.
///     DISM sub-tools) are also terminated.
///   • Callers can supply an optional <paramref name="lineFilter"/> to
///     suppress noisy lines (blank lines, decoration) or an optional
///     <paramref name="progressLineCallback"/> to extract progress data
///     (e.g. "Verification 45 percent complete.") without logging it.
///   • <see cref="OutputDataReceived"/> event handlers run on thread-pool
///     threads; all writes go through the log's streaming callback which
///     marshals them to the UI thread.
///
/// Threading:
///   Call from a <c>Task.Run()</c> context only — the polling loop sleeps
///   the calling thread.  Never call from the UI thread.
/// </summary>
internal static class ProcessExecutionHelper
{
    /// <summary>
    /// Result returned by <see cref="Run"/>.
    /// </summary>
    internal sealed record RunResult(
        int  ExitCode,
        bool WasCancelled,
        int  LinesEmitted);

    // ── Main entry point ──────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="exe"/> with <paramref name="arguments"/> on the
    /// calling (background) thread, streaming every non-empty stdout line to
    /// <paramref name="log"/> and polling for cancellation every 250 ms.
    ///
    /// Stderr lines are logged at Warn level.
    ///
    /// Returns when the process exits or when
    /// <paramref name="ct"/> is cancelled (in which case the process is killed
    /// and <see cref="RunResult.WasCancelled"/> is <see langword="true"/>).
    /// </summary>
    /// <param name="exe">Fully-qualified or PATH-resolved executable name.</param>
    /// <param name="arguments">Argument string passed verbatim to the process.</param>
    /// <param name="log">Live-streaming action log.</param>
    /// <param name="ct">Cancellation token — kills the process tree if cancelled.</param>
    /// <param name="lineFilter">
    ///   Optional predicate.  When supplied, lines for which it returns
    ///   <see langword="false"/> are silently discarded and not logged.
    /// </param>
    /// <param name="progressLineCallback">
    ///   Optional callback invoked for <em>every</em> stdout line (before the
    ///   <paramref name="lineFilter"/> is applied).  Use it to parse progress
    ///   percentages without logging the noisy progress lines.
    /// </param>
    /// <param name="outputEncoding">
    ///   Optional stdout/stderr encoding.  Defaults to UTF-8 when
    ///   <see langword="null"/>.  Use <c>Encoding.GetEncoding(850)</c> for
    ///   tools that write OEM characters (rare on modern Windows).
    /// </param>
    internal static RunResult Run(
        string             exe,
        string             arguments,
        ActionExecutionLog log,
        CancellationToken  ct,
        Func<string, bool>? lineFilter             = null,
        Action<string>?     progressLineCallback   = null,
        Encoding?           outputEncoding         = null)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = outputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding  = outputEncoding ?? Encoding.UTF8,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Could not start process: {exe}");

        int  linesEmitted = 0;
        bool cancelled    = false;

        // ── Attach async output readers ───────────────────────────────────────
        // OutputDataReceived fires on a thread-pool thread for each line.
        // The ActionExecutionLog callback (wired by the ViewModel) marshals
        // writes back to the UI thread via DispatcherQueue.TryEnqueue.

        process.OutputDataReceived += (_, e) =>
        {
            var line = e.Data;
            if (line is null) return;

            // Always give the progress callback first look at every line.
            progressLineCallback?.Invoke(line);

            // Apply the caller's filter (blank lines, decoration headers, etc.)
            if (string.IsNullOrWhiteSpace(line)) return;
            if (lineFilter is not null && !lineFilter(line)) return;

            log.Info(line);
            Interlocked.Increment(ref linesEmitted);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            var line = e.Data;
            if (line is null || string.IsNullOrWhiteSpace(line)) return;
            log.Warn($"[stderr] {line}");
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // ── Poll until exit or cancellation ───────────────────────────────────
        while (!process.WaitForExit(250))
        {
            if (!ct.IsCancellationRequested) continue;

            cancelled = true;
            try
            {
                // Kill the entire process tree so sub-tools (e.g. DISM helpers)
                // are also terminated.
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                System.ComponentModel.Win32Exception) { }

            break;
        }

        // Final WaitForExit flushes the async output reader buffers.
        // Use a short absolute timeout to prevent hanging if the process is
        // a zombie (rare but possible on elevated contexts).
        process.WaitForExit(3_000);

        return new RunResult(
            ExitCode:     cancelled ? -1 : process.ExitCode,
            WasCancelled: cancelled,
            LinesEmitted: linesEmitted);
    }

    // ── Convenience overload for simple commands ──────────────────────────────

    /// <summary>
    /// Runs <paramref name="exe"/> and returns its stdout as a single string.
    /// Used for quick queries (e.g. ipconfig, sc query) where the output is
    /// short and streaming is not needed.
    /// </summary>
    internal static string Capture(string exe, string arguments)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15_000);
        return output;
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>Elapsed time as a human-readable string suitable for a log entry.</summary>
    internal static string FormatDuration(TimeSpan elapsed) => elapsed.TotalMinutes >= 1
        ? $"{elapsed.TotalMinutes:F1} min"
        : $"{elapsed.TotalSeconds:F1} s";
}
