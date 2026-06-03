using System.Diagnostics;

namespace CodeScan.Services.Diagnostics;

/// <summary>
/// Lightweight elapsed-time markers for the TUI cold-start path.
/// Each <see cref="Mark"/> call appends one line to
/// <c>~/.codescan/logs/tui-startup.log</c> with the milliseconds elapsed
/// since process start, so a "어느순간 늦게뜸" report can be answered with
/// real numbers instead of guesses.
///
/// Intentionally lock-free and side-effect-free on failure — startup
/// instrumentation that itself throws during boot is worse than no
/// instrumentation at all.
/// </summary>
public static class StartupTracer
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static readonly string LogPath = Path.Combine(
        AppPaths.GetLogDir(), "tui-startup.log");
    private static readonly long ProcessId = Environment.ProcessId;
    private static bool _headerWritten;

    public static void Mark(string phase)
    {
        try
        {
            if (!_headerWritten)
            {
                File.AppendAllText(LogPath,
                    $"\n=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={ProcessId} ===\n");
                _headerWritten = true;
            }
            File.AppendAllText(LogPath,
                $"[+{Watch.ElapsedMilliseconds,6} ms] {phase}\n");
        }
        catch { /* logging must never break boot */ }
    }
}
