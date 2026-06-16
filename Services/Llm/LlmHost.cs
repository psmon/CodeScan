using LLama;
using LLama.Common;
using LLama.Native;

namespace CodeScan.Services.Llm;

/// <summary>
/// Owns the <see cref="LLamaWeights"/> + <see cref="ModelParams"/> for the
/// duration of a TUI Chat session. Loads through either the CPU backend
/// (Backend.Cpu) or the Vulkan GPU backend (Backend.Vulkan) — both ship
/// per-RID natives under runtimes/, so the right one is picked at load
/// time. Disposing releases the model from memory.
/// </summary>
public sealed class LlmHost : IAsyncDisposable
{
    private readonly LLamaWeights _weights;
    private readonly ModelParams _modelParams;

    public string ModelPath { get; }
    public uint ContextSize { get; }
    public int GpuLayerCount { get; }
    public int MainGpu { get; }

    private LlmHost(string modelPath, uint contextSize, int gpuLayerCount, int mainGpu,
        LLamaWeights weights, ModelParams modelParams)
    {
        ModelPath = modelPath;
        ContextSize = contextSize;
        GpuLayerCount = gpuLayerCount;
        MainGpu = mainGpu;
        _weights = weights;
        _modelParams = modelParams;
    }

    /// <param name="contextSize">
    /// Tokens the model can see at once. We default to 8192 here but the TUI
    /// derives this from the GGUF's trained <c>context_length</c> — Gemma 4
    /// E4B for example is trained at 131072.
    /// </param>
    /// <param name="gpuLayerCount">
    /// 0   = CPU only (uses Backend.Cpu native).
    /// 999 = offload ALL transformer layers to GPU (Backend.Vulkan).
    /// 1..N = partial split (lower layers on GPU, rest on CPU) — useful when
    /// VRAM is tight.
    /// </param>
    /// <param name="mainGpu">
    /// Vulkan device index. 0 = first device (default). Set this on multi-GPU
    /// boxes from the TUI selector. Ignored when <paramref name="gpuLayerCount"/> is 0.
    /// </param>
    public static async Task<LlmHost> LoadAsync(
        string modelPath,
        uint contextSize = 8192,
        int gpuLayerCount = 0,
        int mainGpu = 0,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"GGUF model not found: {modelPath}", modelPath);

        ConfigureNativeLogOnce();

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = contextSize,
            GpuLayerCount = gpuLayerCount,
            MainGpu = gpuLayerCount > 0 ? mainGpu : 0,
            UseMemorymap = true,
        };

        var weights = await LLamaWeights.LoadFromFileAsync(modelParams, ct, progress);
        return new LlmHost(modelPath, contextSize, gpuLayerCount, mainGpu, weights, modelParams);
    }

    internal (LLamaWeights weights, ModelParams modelParams) GetInternals() => (_weights, _modelParams);

    public ValueTask DisposeAsync()
    {
        _weights.Dispose();
        return ValueTask.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Native log capture — adapted from AgentZeroLite's LlamaSharpLocalLlm.
    // llama.cpp normally writes its diagnostics to stderr (silently swallowed
    // by a TUI), which made the "empty raw output" / "ctx clamp" failure modes
    // impossible to root-cause. Pipe them into ~/.codescan/logs/llama-native.log
    // and they become postmortemable.
    // ------------------------------------------------------------------
    private static readonly object NativeInitLock = new();
    private static bool _nativeLogConfigured;

    private static void ConfigureNativeLogOnce()
    {
        lock (NativeInitLock)
        {
            if (_nativeLogConfigured) return;

            // Single-file publish leaves Assembly.Location empty, so LLamaSharp's
            // auto-probe collapses to the CWD ('./runtimes/...'). codescan is on
            // PATH and run from arbitrary project dirs, so that path rarely holds
            // the natives and every backend load fails with a NativeApi throw.
            //
            // Two deploy layouts both need pinning:
            //   • deploy-*.{ps1,sh}: runtimes/ ships loose beside the exe →
            //     it lives at AppContext.BaseDirectory.
            //   • release/npm (IncludeNativeLibrariesForSelfExtract=true): the
            //     natives are embedded and the host extracts them to a temp
            //     bundle dir (…/Temp/.net/codescan/<id>/) that is NOT
            //     BaseDirectory. The host advertises that dir via the
            //     NATIVE_DLL_SEARCH_DIRECTORIES AppContext entry, and the
            //     extracted tree preserves runtimes/<rid>/native/…, so feeding
            //     those dirs to LLamaSharp lets it find llama.dll there.
            try
            {
                var searchDirs = new List<string>();
                if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string ndd)
                    searchDirs.AddRange(ndd.Split(Path.PathSeparator,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                searchDirs.Add(AppContext.BaseDirectory);

                foreach (var dir in searchDirs
                             .Where(d => !string.IsNullOrWhiteSpace(d))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    NativeLibraryConfig.All.WithSearchDirectory(dir);
                }
            }
            catch { /* search-dir config failure — fall back to default probe */ }

            var logPath = Path.Combine(Services.AppPaths.GetLogDir(), "llama-native.log");
            try
            {
                NativeLibraryConfig.All.WithLogCallback((level, msg) =>
                {
                    // Skip Debug; everything else lands in the file. The Info
                    // tier is where ggml/llama.cpp announces ctx-clamp etc.
                    if (level < LLamaLogLevel.Info) return;
                    try
                    {
                        File.AppendAllText(logPath,
                            $"[{DateTime.Now:HH:mm:ss}][{level}] {msg?.TrimEnd('\r', '\n')}\n");
                    }
                    catch { /* logging mustn't kill the model load */ }
                });
            }
            catch { /* config-time failure — ignore, model still loads */ }

            _nativeLogConfigured = true;
        }
    }
}
