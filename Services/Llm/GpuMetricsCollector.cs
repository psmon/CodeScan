#if DEBUG
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodeScan.Services.Llm;

/// <summary>
/// Develop-mode GPU memory telemetry surfaced through the legacy
/// <see cref="System.Diagnostics.Tracing.EventSource"/> + PollingCounter
/// path so JetBrains Rider's Diagnostic Tools (and the dotnet-counters CLI)
/// can pick it up without an extra exporter. The newer
/// <see cref="System.Diagnostics.Metrics.Meter"/> API isn't shown in Rider's
/// in-IDE counter view today — Rider listens on the EventPipe stream that
/// EventSource writes to.
///
/// Compiled out of Release / AOT publish (see <c>#if DEBUG</c> and the
/// matching conditional <see cref="System.Diagnostics.PerformanceCounter"/>
/// reference in CodeScan.csproj) so the shipped binary stays portable and
/// trim-safe.
///
/// EventSource name: <c>CodeScan-Gpu</c>. Open Rider → Run with profiler
/// (or attach Diagnostic Tools) → "Counters" tab to see the readings live.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GpuMetricsCollector
{
    private static readonly List<AdapterCounters> Adapters = new();
    private static bool _started;

    private sealed record AdapterCounters(
        string Instance,
        PerformanceCounter Dedicated,
        PerformanceCounter Shared);

    /// <summary>
    /// Best-effort startup. No-op when not on Windows, when PerfMon counters
    /// are missing, or when called twice. Safe to invoke from Main without
    /// worrying about the rest of the boot path.
    /// </summary>
    public static void StartIfWindows()
    {
        if (_started) return;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            var category = new PerformanceCounterCategory("GPU Adapter Memory");
            // GetInstanceNames is the only portable way to find the
            // luid_0x…_phys_0 strings — they're regenerated on driver
            // reinstalls and differ per machine. Picking them up at runtime
            // makes the gauge work on whatever box the binary runs on.
            foreach (var instance in category.GetInstanceNames())
            {
                try
                {
                    var dedicated = new PerformanceCounter(
                        "GPU Adapter Memory", "Dedicated Usage", instance, readOnly: true);
                    var shared = new PerformanceCounter(
                        "GPU Adapter Memory", "Shared Usage", instance, readOnly: true);
                    // First read primes the counter so subsequent NextValue()
                    // returns a real number instead of 0.
                    _ = dedicated.NextValue();
                    _ = shared.NextValue();
                    Adapters.Add(new AdapterCounters(instance, dedicated, shared));
                }
                catch
                {
                    // Single bad instance shouldn't kill the whole probe.
                }
            }
        }
        catch (InvalidOperationException)
        {
            // PerfMon counter cache is sometimes stale (lodctr /r as admin
            // rebuilds it). Surface nothing in that case — Develop-mode
            // telemetry isn't load-bearing.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            // Restricted user — same outcome, no telemetry.
            return;
        }

        if (Adapters.Count == 0) return;

        // PollingCounter has no concept of tags — sum across every adapter
        // for the IDE view. Per-adapter breakdown stays available via PerfMon /
        // Task Manager when needed.
        GpuEventSource.Log.Bind(
            dedicatedBytes: () => Adapters.Sum(a => SafeNext(a.Dedicated)),
            sharedBytes: () => Adapters.Sum(a => SafeNext(a.Shared)));

        _started = true;
    }

    private static double SafeNext(PerformanceCounter c)
    {
        try { return c.NextValue(); }
        catch { return 0; }
    }
}

/// <summary>
/// EventPipe-visible counter source. PollingCounter callbacks fire on
/// demand from whatever listener attaches (Rider's Diagnostic Tools,
/// dotnet-counters, dotTrace), so we don't have to drive a polling loop
/// ourselves.
/// </summary>
[EventSource(Name = "CodeScan-Gpu")]
internal sealed class GpuEventSource : EventSource
{
    public static readonly GpuEventSource Log = new();

    private PollingCounter? _dedicated;
    private PollingCounter? _shared;

    private GpuEventSource() { }

    internal void Bind(Func<double> dedicatedBytes, Func<double> sharedBytes)
    {
        _dedicated = new PollingCounter("gpu-dedicated-bytes", this, dedicatedBytes)
        {
            DisplayName = "GPU Dedicated VRAM",
            DisplayUnits = "B",
        };
        _shared = new PollingCounter("gpu-shared-bytes", this, sharedBytes)
        {
            DisplayName = "GPU Shared VRAM",
            DisplayUnits = "B",
        };
    }

    protected override void Dispose(bool disposing)
    {
        _dedicated?.Dispose();
        _shared?.Dispose();
        base.Dispose(disposing);
    }
}
#endif
