#if DEBUG
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodeScan.Services.Llm;

/// <summary>
/// Develop-mode GPU memory telemetry surfaced through
/// <see cref="System.Diagnostics.Metrics"/>. We tap the Windows PerfMon
/// "GPU Adapter Memory" counter set — the same numbers Task Manager's
/// Performance tab shows — and discover every adapter instance at startup
/// instead of hardcoding a per-machine LUID string.
///
/// Compiled out of Release / AOT publish (see <c>#if DEBUG</c> and the
/// matching conditional <see cref="System.Diagnostics.PerformanceCounter"/>
/// reference in CodeScan.csproj) so the shipped binary stays portable and
/// trim-safe.
///
/// Meter name: <c>CodeScan.Gpu</c>. Listen for it with
/// <c>MeterListener</c>, dotnet-counters, or any OTel-style exporter.
/// <see cref="AttachConsoleListener"/> spins up a 5-second console dump
/// for ad-hoc development sessions.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GpuMetricsCollector
{
    private static readonly Meter Meter = new("CodeScan.Gpu");
    private static readonly List<AdapterCounters> Adapters = new();
    private static bool _started;
    private static MeterListener? _consoleListener;

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
                    // First read primes the counter so the next NextValue()
                    // returns a real delta-based number instead of 0.
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

        Meter.CreateObservableGauge<long>(
            "gpu.memory.dedicated_bytes",
            () => Adapters.Select(a => new Measurement<long>(
                SafeNext(a.Dedicated),
                new KeyValuePair<string, object?>("adapter", a.Instance))),
            unit: "By",
            description: "Per-adapter dedicated VRAM usage (Windows PerfMon: GPU Adapter Memory / Dedicated Usage)");

        Meter.CreateObservableGauge<long>(
            "gpu.memory.shared_bytes",
            () => Adapters.Select(a => new Measurement<long>(
                SafeNext(a.Shared),
                new KeyValuePair<string, object?>("adapter", a.Instance))),
            unit: "By",
            description: "Per-adapter shared system-memory usage attributed to the GPU");

        _started = true;
    }

    /// <summary>
    /// Optional helper: dumps the current gauge values to stdout every
    /// <paramref name="interval"/>. Convenient for "run the TUI and watch
    /// VRAM" loops; production-grade scrape paths should use a real
    /// MeterListener / OTel exporter instead.
    /// </summary>
    public static void AttachConsoleListener(TimeSpan? interval = null)
    {
        if (!_started || _consoleListener != null) return;

        var window = interval ?? TimeSpan.FromSeconds(5);
        var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == "CodeScan.Gpu") l.EnableMeasurementEvents(instr);
            }
        };
        listener.SetMeasurementEventCallback<long>((instr, value, tags, _) =>
        {
            var adapter = "";
            foreach (var kv in tags)
                if (kv.Key == "adapter") adapter = kv.Value?.ToString() ?? "";
            Console.WriteLine($"[gpu-metrics] {instr.Name} adapter={Truncate(adapter, 32)} value={value:N0} bytes");
        });
        listener.Start();
        _consoleListener = listener;

        // Drive the polling loop on a background thread so Main doesn't
        // block. Marked background so process shutdown isn't held back.
        var t = new Thread(() =>
        {
            while (true)
            {
                try { listener.RecordObservableInstruments(); }
                catch { /* one-off failure shouldn't kill the loop */ }
                Thread.Sleep(window);
            }
        })
        {
            IsBackground = true,
            Name = "gpu-metrics-poll",
        };
        t.Start();
    }

    private static long SafeNext(PerformanceCounter c)
    {
        try { return (long)c.NextValue(); }
        catch { return 0; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
#endif
