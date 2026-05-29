using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CodeScan.Services.Llm;

/// <summary>
/// One discoverable accelerator the user could direct llama.cpp at.
/// </summary>
/// <param name="VulkanIndex">
/// Vulkan GPU index as llama.cpp sees it (matches <c>ModelParams.MainGpu</c>).
/// −1 means the device was found through WMI/nvidia-smi but Vulkan does not
/// expose it — usually a driver/loader gap. Still listed so the user knows it
/// exists and can fix the driver.
/// </param>
/// <param name="VramBytes">
/// Largest device-local heap reported by Vulkan (UMA-aware), or AdapterRAM
/// from WMI, or memory.total from nvidia-smi. 0 = unknown.
/// </param>
public sealed record GpuDevice(
    int VulkanIndex,
    string Name,
    string Vendor,
    bool IsDiscrete,
    long VramBytes,
    string Source);

/// <summary>
/// Cross-platform GPU discovery. Two probes layered:
///
///   1) vulkaninfo — authoritative for "what llama.cpp can use" and gives the
///      real device-local heap size (matters on UMA boxes like Strix Halo
///      where Windows reports ~4 GB of dedicated VRAM but Vulkan sees the
///      full ~64 GB of unified memory).
///   2) nvidia-smi — refines NVIDIA VRAM when present.
///
/// An earlier WMI Win32_VideoController fallback was removed: its
/// AdapterRAM is unreliable on UMA hardware, Vulkan-invisible devices can't
/// be used by llama.cpp's Vulkan backend anyway, and the PowerShell
/// process spawn cost ~300–500 ms per Chat open. Vulkan + nvidia-smi cover
/// every device this app can actually offload to.
///
/// Caching: a cold probe takes ~1–3 s (vulkaninfo full dump + nvidia-smi
/// try-and-fail on non-NVIDIA boxes). Hardware doesn't sprout new GPUs
/// mid-process, so the first result is cached for the process lifetime
/// via <see cref="Lazy{T}"/>.
/// </summary>
public static class GpuEnumerator
{
    // Lazy<T> with ExecutionAndPublication guarantees the probe runs at
    // most once even when MainView's startup prefetch and ChatView's inline
    // call race each other. The second caller blocks on the first's Task
    // and gets the same list back — no duplicate vulkaninfo subprocess work.
    private static readonly Lazy<List<GpuDevice>> CachedProbe =
        new(EnumerateUncached, LazyThreadSafetyMode.ExecutionAndPublication);

    public static List<GpuDevice> Enumerate() => CachedProbe.Value.ToList();

    /// <summary>True only when the probe has already completed and cached
    /// a result. Lets callers decide whether a synchronous Enumerate()
    /// will be instant or potentially slow.</summary>
    public static bool IsCached => CachedProbe.IsValueCreated;

    private static List<GpuDevice> EnumerateUncached()
    {
        var devices = new List<GpuDevice>();

        // ---- 1. Vulkan -----------------------------------------------------
        try
        {
            var vk = VulkanProbe.Enumerate();
            foreach (var v in vk)
                devices.Add(new GpuDevice(
                    VulkanIndex: v.Index,
                    Name: v.Name,
                    Vendor: VendorIdToName(v.VendorId),
                    IsDiscrete: v.IsDiscrete,
                    VramBytes: v.DeviceLocalHeapBytes,
                    Source: "vulkan"));
        }
        catch { /* probe failure is fine — fall through to other sources */ }

        // ---- 2. nvidia-smi (refines NVIDIA VRAM) ---------------------------
        try
        {
            foreach (var smi in NvidiaSmiProbe.Enumerate())
            {
                var existing = devices.FirstOrDefault(d => FuzzyMatch(d.Name, smi.Name));
                if (existing != null)
                {
                    // Replace with nvidia-smi-refined VRAM, keep Vulkan index.
                    devices.Remove(existing);
                    devices.Add(existing with { VramBytes = smi.VramBytes });
                }
                else
                {
                    devices.Add(new GpuDevice(
                        VulkanIndex: -1,
                        Name: smi.Name,
                        Vendor: "NVIDIA",
                        IsDiscrete: true,
                        VramBytes: smi.VramBytes,
                        Source: "nvidia-smi"));
                }
            }
        }
        catch { /* skip */ }

        // Stable order: Vulkan-capable first (by index), then non-Vulkan.
        return devices
            .OrderBy(d => d.VulkanIndex < 0 ? 1 : 0)
            .ThenBy(d => d.VulkanIndex < 0 ? int.MaxValue : d.VulkanIndex)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    private static bool FuzzyMatch(string a, string b)
    {
        // Strip the trademark / corporate noise so "AMD Radeon(TM) 8060S Graphics"
        // matches "Radeon 8060S".
        static string Norm(string s) => Regex
            .Replace(s, @"\(TM\)|\(R\)|Graphics|Corporation|Inc\.|GeForce|Radeon|Intel|AMD|NVIDIA", "",
                RegexOptions.IgnoreCase)
            .Replace(" ", "")
            .ToLowerInvariant();
        return Norm(a).Contains(Norm(b)) || Norm(b).Contains(Norm(a));
    }

    private static string VendorIdToName(uint id) => id switch
    {
        0x1002 => "AMD",
        0x10DE => "NVIDIA",
        0x8086 => "Intel",
        0x13B5 => "ARM",
        0x5143 => "Qualcomm",
        0x106B => "Apple",
        _ => $"0x{id:X4}",
    };

}

// ----------------------------------------------------------------------
// Probe sources
// ----------------------------------------------------------------------

internal sealed record VulkanDevice(int Index, string Name, uint VendorId, bool IsDiscrete, long DeviceLocalHeapBytes);

internal static class VulkanProbe
{
    private static readonly Regex GpuHeader = new(@"^GPU(\d+):", RegexOptions.Compiled);
    private static readonly Regex NameLine = new(@"deviceName\s*=\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex TypeLine = new(@"deviceType\s*=\s*(\S+)", RegexOptions.Compiled);
    private static readonly Regex VendorLine = new(@"vendorID\s*=\s*0x([0-9A-Fa-f]+)", RegexOptions.Compiled);
    private static readonly Regex HeapHeader = new(@"^\s*memoryHeaps\[(\d+)\]:", RegexOptions.Compiled);
    private static readonly Regex SizeLine = new(@"^\s*size\s*=\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex DeviceLocalFlag = new(@"MEMORY_HEAP_DEVICE_LOCAL_BIT", RegexOptions.Compiled);

    public static IReadOnlyList<VulkanDevice> Enumerate()
    {
        // The --summary form is faster but lacks memoryHeaps. We run the full
        // dump to get heap sizes; bail to summary if the full output is empty.
        var text = RunVulkanInfo(summary: false);
        if (string.IsNullOrEmpty(text))
            text = RunVulkanInfo(summary: true);
        if (string.IsNullOrEmpty(text)) return Array.Empty<VulkanDevice>();

        // The full dump groups by GPU. We split on the "GPU id : N (...)"
        // section headers and parse each block independently.
        var blocks = Regex.Split(text, @"(?=^GPU\s*id\s*[:=]\s*\d+|^GPU\d+:)", RegexOptions.Multiline);
        var devices = new List<VulkanDevice>();

        foreach (var block in blocks)
        {
            var headerMatch = Regex.Match(block,
                @"^(?:GPU\s*id\s*[:=]\s*|GPU)(\d+)", RegexOptions.Multiline);
            if (!headerMatch.Success) continue;
            var idx = int.Parse(headerMatch.Groups[1].Value, CultureInfo.InvariantCulture);

            string? name = null;
            bool? discrete = null;
            uint? vendor = null;
            long biggestDeviceLocal = 0;
            long currentHeapSize = 0;
            bool currentHeapDeviceLocal = false;
            bool inHeap = false;

            foreach (var raw in block.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (name == null)
                {
                    var nm = NameLine.Match(line);
                    if (nm.Success) name = nm.Groups[1].Value.Trim();
                }
                if (discrete == null)
                {
                    var tm = TypeLine.Match(line);
                    if (tm.Success)
                        discrete = tm.Groups[1].Value.Contains("DISCRETE", StringComparison.OrdinalIgnoreCase);
                }
                if (vendor == null)
                {
                    var vm = VendorLine.Match(line);
                    if (vm.Success && uint.TryParse(vm.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                        vendor = v;
                }

                // memoryHeaps block — accumulate the biggest device-local one.
                if (HeapHeader.IsMatch(line))
                {
                    Flush();
                    inHeap = true;
                    currentHeapSize = 0;
                    currentHeapDeviceLocal = false;
                    continue;
                }
                if (inHeap)
                {
                    var sm = SizeLine.Match(line);
                    if (sm.Success && long.TryParse(sm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sz))
                        currentHeapSize = sz;
                    if (DeviceLocalFlag.IsMatch(line))
                        currentHeapDeviceLocal = true;
                    // Heap blocks tend to end on a blank line or the next
                    // section ("memoryTypes:"). Either flushes.
                    if (line.Length == 0 || Regex.IsMatch(line, @"^\s*memoryTypes"))
                    {
                        Flush();
                        inHeap = false;
                    }
                }
            }
            Flush();

            if (name != null)
                devices.Add(new VulkanDevice(
                    idx, name, vendor ?? 0, discrete ?? false, biggestDeviceLocal));

            void Flush()
            {
                if (currentHeapDeviceLocal && currentHeapSize > biggestDeviceLocal)
                    biggestDeviceLocal = currentHeapSize;
                currentHeapSize = 0;
                currentHeapDeviceLocal = false;
            }
        }

        // Deduplicate by index (the regex split can yield the same GPU once
        // per header form).
        return devices
            .GroupBy(d => d.Index)
            .Select(g => g.OrderByDescending(d => d.DeviceLocalHeapBytes).First())
            .OrderBy(d => d.Index)
            .ToList();
    }

    private static string RunVulkanInfo(bool summary)
    {
        foreach (var exe in CandidatePaths())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = summary ? "--summary" : "",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                var sb = new System.Text.StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } continue; }
                var output = sb.ToString();
                if (!string.IsNullOrWhiteSpace(output)) return output;
            }
            catch { /* try next */ }
        }
        return string.Empty;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (!string.IsNullOrEmpty(sdk))
        {
            yield return Path.Combine(sdk, "Bin", "vulkaninfoSDK.exe");
            yield return Path.Combine(sdk, "Bin", "vulkaninfo.exe");
            yield return Path.Combine(sdk, "bin", "vulkaninfo");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield return Path.Combine(Environment.SystemDirectory, "vulkaninfo.exe");
        yield return "vulkaninfo";
    }
}

internal sealed record NvidiaSmiDevice(int Index, string Name, long VramBytes);

internal static class NvidiaSmiProbe
{
    public static IReadOnlyList<NvidiaSmiDevice> Enumerate()
    {
        foreach (var exe in CandidatePaths())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--query-gpu=index,name,memory.total --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                var sb = new System.Text.StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } continue; }
                var text = sb.ToString();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var list = new List<NvidiaSmiDevice>();
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var parts = line.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length < 3) continue;
                    if (!int.TryParse(parts[0], out var idx)) continue;
                    if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mib)) continue;
                    list.Add(new NvidiaSmiDevice(idx, parts[1], mib * 1024L * 1024L));
                }
                if (list.Count > 0) return list;
            }
            catch { /* try next */ }
        }
        return Array.Empty<NvidiaSmiDevice>();
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return "nvidia-smi";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            yield return @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
        }
    }
}
