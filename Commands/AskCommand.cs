using CodeScan.Services;
using CodeScan.Services.Llm;
using CodeScan.Services.Llm.Tools;

namespace CodeScan.Commands;

/// <summary>
/// Non-interactive counterpart to the TUI Chat screen. Loads a GGUF, runs the
/// same <see cref="AgentChatLoop"/> the TUI uses, and streams the answer to
/// stdout — so the on-device agent is scriptable (pipes, CI, `codescan ask …`)
/// without spinning up Terminal.Gui. Tool calls and progress go to stderr so
/// stdout stays a clean answer stream that callers can redirect.
/// </summary>
public sealed class AskCommand
{
    public sealed class Options
    {
        public string? Question;
        public string? ProjectRoot;
        public string? Model;        // GGUF path or catalog id; null → default catalog model, then any GGUF on disk
        public uint? ContextSize;    // null → min(gguf trained ctx, 8192)
        public int GpuLayers;        // 0 = CPU (Backend.Cpu); >0 offloads via Vulkan
        public int MaxTokens = 512;
        public bool Quiet;           // suppress tool/progress chatter — print only the final answer
        public bool ListModels;      // print callable models and exit (no question needed)
        public bool ShowHelp;
    }

    public int Execute(string[] args)
    {
        var opts = Parse(args);
        if (opts.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (opts.ListModels)
        {
            PrintModels();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(opts.Question))
        {
            Console.Error.WriteLine("error: no question given. Usage: codescan ask \"your question\" [options]");
            return 2;
        }

        try
        {
            return RunAsync(opts).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(Options opts)
    {
        var modelPath = ResolveModel(opts.Model);
        if (modelPath == null)
        {
            Console.Error.WriteLine(
                $"error: no GGUF model found. Drop one into {ModelLocator.ModelsDir} " +
                "(or pass --model <path>), then retry.");
            Console.Error.WriteLine("hint: run `codescan ask --list-models` to see callable models.");
            return 1;
        }

        var projectRoot = NormalizeProjectRoot(opts.ProjectRoot);

        // Default context to the model's trained window, capped so a CPU run
        // doesn't blow RAM on a 128K-trained Gemma. --ctx overrides outright.
        var meta = GgufReader.Read(modelPath);
        var ctxSize = opts.ContextSize
            ?? (uint)Math.Min(meta?.ContextLength ?? 8192, 8192);

        if (!opts.Quiet)
        {
            Console.Error.WriteLine($"model   : {Path.GetFileName(modelPath)}");
            Console.Error.WriteLine($"context : {ctxSize} tokens  •  gpu-layers: {opts.GpuLayers}");
            Console.Error.WriteLine(projectRoot != null
                ? $"project : {projectRoot}"
                : "project : (none — only absolute paths are readable)");
            Console.Error.WriteLine("loading model… (first load can take 10–30s)");
        }

        await using var host = await LlmHost.LoadAsync(
            modelPath,
            contextSize: ctxSize,
            gpuLayerCount: opts.GpuLayers);

        using var db = new SqliteStore(AppPaths.DbPath);
        var template = ChatTemplateRegistry.For(modelPath, meta);

        await using var loop = new AgentChatLoop(
            host,
            new CodeScanToolbelt(db, projectRoot),
            projectRoot: projectRoot,
            template: template,
            maxIterations: 6,
            maxTokensPerTurn: opts.MaxTokens);

        if (!opts.Quiet)
            Console.Error.WriteLine("ready.\n");

        var streamed = false;
        var exitCode = 0;

        await foreach (var update in loop.SendAsync(opts.Question!))
        {
            switch (update.Phase)
            {
                case "stream_delta":
                    Console.Out.Write(update.Text);
                    Console.Out.Flush();
                    streamed = true;
                    break;
                case "stream_end":
                    Console.Out.WriteLine();
                    break;
                case "done":
                    // Non-streamed fallback path — print the whole body once.
                    if (!streamed)
                        Console.Out.WriteLine(update.Text);
                    break;
                case "tool":
                    if (!opts.Quiet) Console.Error.WriteLine($"  → tool: {update.Text}");
                    break;
                case "tool_result":
                    if (!opts.Quiet) Console.Error.WriteLine("  ← tool result received");
                    break;
                case "thinking":
                case "progress":
                    if (!opts.Quiet) Console.Error.WriteLine($"  … {update.Text}");
                    break;
                case "error":
                    Console.Error.WriteLine($"error: {update.Text}");
                    exitCode = 1;
                    break;
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Resolve a model reference to a GGUF path. Order: explicit file path →
    /// catalog id → catalog default on disk → newest GGUF in the models dir.
    /// </summary>
    private static string? ResolveModel(string? model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            if (File.Exists(model)) return model;

            foreach (var entry in ChatModelCatalog.All)
            {
                if (string.Equals(entry.Id, model, StringComparison.OrdinalIgnoreCase))
                {
                    var p = ModelLocator.FindModel(entry);
                    if (p != null) return p;
                    Console.Error.WriteLine(
                        $"warning: model id '{model}' is in the catalog but not on disk.");
                }
            }
            // Unknown reference that isn't a file — fall through to defaults.
        }

        var def = ModelLocator.FindModel(ChatModelCatalog.Default);
        if (def != null) return def;

        var any = ModelLocator.EnumerateGgufFiles(ModelLocator.ModelsDir);
        return any.Count > 0 ? any[0] : null;
    }

    /// <summary>
    /// List the models <c>ask</c> can call: catalog entries (with download
    /// status + the id to pass to <c>--model</c>) and any custom GGUF dropped
    /// into the models dir (passed to <c>--model</c> by path). The model the
    /// next <c>ask</c> would auto-pick is marked.
    /// </summary>
    private static void PrintModels()
    {
        var autoPick = ResolveModel(null);

        Console.WriteLine($"Models dir: {ModelLocator.ModelsDir}\n");

        Console.WriteLine("Catalog models (pass the id to --model):");
        foreach (var entry in ChatModelCatalog.All)
        {
            var path = ModelLocator.FindModel(entry);
            var onDisk = path != null;
            var marker = onDisk && string.Equals(path, autoPick, StringComparison.OrdinalIgnoreCase)
                ? " (auto)"
                : entry.Id == ChatModelCatalog.Default.Id ? " (default)" : "";
            var status = onDisk ? "downloaded" : "not downloaded";
            Console.WriteLine($"  [{status,-14}] {entry.Id}{marker}");
            Console.WriteLine($"                   {entry.DisplayName}  •  {entry.Family}");
        }

        // Custom drops: GGUF files in the models dir that aren't catalog files.
        var catalogFiles = ChatModelCatalog.All
            .Select(e => e.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var customs = ModelLocator.EnumerateGgufFiles(ModelLocator.ModelsDir)
            .Where(p => !catalogFiles.Contains(Path.GetFileName(p)))
            .ToList();

        if (customs.Count > 0)
        {
            Console.WriteLine("\nCustom GGUF on disk (pass the path to --model):");
            foreach (var p in customs)
            {
                var marker = string.Equals(p, autoPick, StringComparison.OrdinalIgnoreCase) ? " (auto)" : "";
                Console.WriteLine($"  {p}{marker}");
            }
        }

        Console.WriteLine(autoPick != null
            ? $"\nNext `codescan ask` (no --model) uses: {Path.GetFileName(autoPick)}"
            : "\nNo model on disk yet — download one in the TUI Chat screen, or drop a GGUF into the models dir.");
    }

    private static string? NormalizeProjectRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            Console.Error.WriteLine($"warning: project root '{full}' does not exist — ignoring.");
            return null;
        }
        return full;
    }

    private static Options Parse(string[] args)
    {
        var opts = new Options();
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    opts.ShowHelp = true;
                    break;
                case "-p" or "--project" when i + 1 < args.Length:
                    opts.ProjectRoot = args[++i];
                    break;
                case "-m" or "--model" when i + 1 < args.Length:
                    opts.Model = args[++i];
                    break;
                case "--ctx" when i + 1 < args.Length:
                    if (uint.TryParse(args[++i], out var ctx)) opts.ContextSize = ctx;
                    break;
                case "--gpu" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var gpu)) opts.GpuLayers = Math.Max(0, gpu);
                    break;
                case "--max-tokens" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var mt)) opts.MaxTokens = Math.Clamp(mt, 16, 8192);
                    break;
                case "-q" or "--quiet":
                    opts.Quiet = true;
                    break;
                case "--list-models" or "--models":
                    opts.ListModels = true;
                    break;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        // Join positional tokens so an unquoted question still works.
        if (positional.Count > 0)
            opts.Question = string.Join(' ', positional);

        return opts;
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            codescan ask — ask the on-device agent a question (non-interactive)

            USAGE
              codescan ask --list-models                 list callable models, then
              codescan ask "<question>" [options]        ask with chosen options

            OPTIONS
                  --list-models      list downloaded/supported models and exit
                                     (shows the id/path to pass to --model)
              -p, --project <path>   project root for codebase context (tools read files
                                     and scope DB queries relative to it)
              -m, --model <ref>      GGUF file path or catalog id
                                     (default: catalog default, else newest GGUF on disk)
                  --ctx <n>          context window in tokens
                                     (default: min(model trained ctx, 8192))
                  --gpu <n>          transformer layers to offload to GPU via Vulkan
                                     (default: 0 = CPU only; 999 = offload all)
                  --max-tokens <n>   max tokens per turn (default: 512)
              -q, --quiet            print only the answer (suppress model/tool chatter)
              -h, --help             show this help

            OUTPUT
              The answer streams to stdout; model/tool/progress notes go to stderr,
              so `codescan ask "…" -q > answer.txt` captures just the answer.

            EXAMPLES
              codescan ask --list-models
              codescan ask "What does DirectoryScanner do?" -p C:\code\psmon\CodeScan
              codescan ask "Summarize the FTS5 schema" -q --model gemma-4-E2B-UD-Q4_K_XL
              codescan ask "Explain the AOT version bump" --gpu 999
            """);
    }
}
