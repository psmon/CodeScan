using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeScan.Models;
using CodeScan.Services;

namespace CodeScan.Commands;

/// <summary>
/// Architecture analysis — the CLI half of the "AI-outside" flow.
///
///   codescan arch bundle &lt;project&gt;   → emit a compact graph/code summary (JSON) to stdout
///   (an external AI reads the bundle and produces a Mermaid diagram + narrative)
///   codescan arch set &lt;project&gt; --diagram &lt;file|-&gt; [--summary &lt;file|-&gt;]  → persist it
///   codescan arch show &lt;project&gt;      → print the stored analysis
///   codescan arch status              → list projects and their analysis state
///
/// The bundle is a summary (not a full graph dump): directory structure, external
/// modules, classes grouped by top-level directory, key type relations, and doc
/// mentions — enough for an AI to infer layers without overflowing a prompt.
/// The web Architecture View renders whatever `arch set` stored.
/// </summary>
public sealed class ArchCommand
{
    private readonly SqliteStore _db;

    public ArchCommand(SqliteStore db) => _db = db;

    // Caps keep the bundle prompt-friendly on large projects.
    private const int MaxDirectories = 40;
    private const int MaxModules = 50;
    private const int MaxClassesPerDir = 40;
    private const int MaxTypeRelations = 150;
    private const int MaxDocMentions = 80;

    public int Execute(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var action = args[0].ToLowerInvariant();
        var rest = args[1..];
        return action switch
        {
            "bundle" => RunBundle(rest),
            "set" => RunSet(rest),
            "show" => RunShow(rest),
            "status" => RunStatus(rest),
            _ => Unknown(action)
        };
    }

    private int Unknown(string action)
    {
        Console.Error.WriteLine($"Error: unknown arch action '{action}'. Try: bundle | set | show | status");
        return 1;
    }

    // ---- bundle: graph/code summary → stdout (for an AI to consume) ----
    private int RunBundle(string[] args)
    {
        var projectId = ResolveProject(args);
        if (projectId is null) return 1;

        var bundle = Analyze(projectId.Value);
        if (bundle is null)
        {
            Console.Error.WriteLine($"Error: project {projectId} not found.");
            return 1;
        }

        var json = JsonSerializer.Serialize(bundle, ArchJsonContext.Default.ArchBundle);
        Console.WriteLine(json);
        return 0;
    }

    /// <summary>Build the architecture bundle for a project from its knowledge graph.
    /// Pure (no console I/O) so it can also feed tests / other callers.</summary>
    public ArchBundle? Analyze(long projectId)
    {
        var project = _db.GetProject(projectId);
        if (project is null) return null;

        var nodes = _db.GetProjectNodes(projectId);
        var archEdges = _db.GetProjectEdgesByKinds(projectId, new[]
        {
            EdgeKinds.Imports, EdgeKinds.InheritsOrImplements,
            EdgeKinds.UsesType, EdgeKinds.Creates, EdgeKinds.Mentions
        });
        var byId = nodes.ToDictionary(n => n.Id);

        static string TopDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(root)";
            var norm = path.Replace('\\', '/');
            var idx = norm.IndexOf('/');
            return idx > 0 ? norm[..idx] : "(root)";
        }

        // Directory structure — file nodes grouped by their top-level directory.
        var filesByDir = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes.Where(n => n.Kind == "file"))
        {
            var d = TopDir(n.Path);
            filesByDir[d] = filesByDir.GetValueOrDefault(d) + 1;
        }
        var directories = filesByDir
            .OrderByDescending(kv => kv.Value)
            .Take(MaxDirectories)
            .Select(kv => new ArchDir { Name = kv.Key, Files = kv.Value })
            .ToList();

        // Classes grouped by top-level directory (layer hints).
        var classesByDir = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes.Where(n => n.Kind == "class"))
        {
            var d = TopDir(n.Path);
            if (!classesByDir.TryGetValue(d, out var list)) classesByDir[d] = list = new();
            if (list.Count < MaxClassesPerDir) list.Add(n.Label);
        }
        var classGroups = classesByDir
            .OrderByDescending(kv => kv.Value.Count)
            .Select(kv => new ArchClassGroup { Dir = kv.Key, Classes = kv.Value })
            .ToList();

        // External modules — imports edges to module nodes, aggregated by target.
        var moduleUses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Type relations — inherits/uses/creates between symbols, aggregated.
        var relAgg = new Dictionary<(string From, string Kind, string To), int>();
        // Doc mentions — heading -> class.
        var mentions = new List<ArchMention>();

        foreach (var e in archEdges)
        {
            if (!byId.TryGetValue(e.From, out var from) || !byId.TryGetValue(e.To, out var to)) continue;
            switch (e.Kind)
            {
                case EdgeKinds.Imports when to.Kind == "module":
                    moduleUses[to.Label] = moduleUses.GetValueOrDefault(to.Label) + e.Weight;
                    break;
                case EdgeKinds.InheritsOrImplements:
                case EdgeKinds.UsesType:
                case EdgeKinds.Creates:
                    var key = (from.Label, e.Kind, to.Label);
                    relAgg[key] = relAgg.GetValueOrDefault(key) + e.Weight;
                    break;
                case EdgeKinds.Mentions when mentions.Count < MaxDocMentions:
                    mentions.Add(new ArchMention { Heading = from.Label, Symbol = to.Label });
                    break;
            }
        }

        var modules = moduleUses
            .OrderByDescending(kv => kv.Value)
            .Take(MaxModules)
            .Select(kv => new ArchModule { Module = kv.Key, Uses = kv.Value })
            .ToList();

        var typeRelations = relAgg
            .OrderByDescending(kv => kv.Value)
            .Take(MaxTypeRelations)
            .Select(kv => new ArchRel { From = kv.Key.From, Kind = kv.Key.Kind, To = kv.Key.To, Weight = kv.Value })
            .ToList();

        var stats = new ArchStats
        {
            Nodes = nodes.Count,
            Edges = _db.CountProjectEdges(projectId),
            Classes = nodes.Count(n => n.Kind == "class"),
            Methods = nodes.Count(n => n.Kind == "method"),
            Files = nodes.Count(n => n.Kind == "file")
        };

        return new ArchBundle
        {
            Project = new ArchProject
            {
                Id = project.Id,
                Name = System.IO.Path.GetFileName(project.RootPath.TrimEnd('/', '\\')),
                RootPath = project.RootPath,
                AddInfo = project.AddInfo,
                FileCount = project.FileCount,
                DirCount = project.DirCount
            },
            Directories = directories,
            ExternalModules = modules,
            ClassesByDir = classGroups,
            TypeRelations = typeRelations,
            DocMentions = mentions,
            Stats = stats,
            Hint = "Infer the architecture LAYERS/subsystems from directories, classesByDir, typeRelations and externalModules. " +
                   "Produce a Mermaid diagram (flowchart 'graph TD' or C4) of the layered architecture, then a short markdown summary of each layer. " +
                   "Persist with: codescan arch set " + project.Id + " --diagram <file|-> --summary <file|->"
        };
    }

    // ---- set: persist an AI-produced diagram + summary ----
    private int RunSet(string[] args)
    {
        long? projectId = null;
        string? diagramArg = null, summaryArg = null, layersArg = null, format = "mermaid";
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--diagram" when i + 1 < args.Length: diagramArg = args[++i]; break;
                case "--summary" when i + 1 < args.Length: summaryArg = args[++i]; break;
                case "--layers" when i + 1 < args.Length: layersArg = args[++i]; break;
                case "--format" when i + 1 < args.Length: format = args[++i]; break;
                default: positional.Add(args[i]); break;
            }
        }

        projectId = ResolveProject(positional.ToArray());
        if (projectId is null) return 1;

        if (diagramArg is null)
        {
            Console.Error.WriteLine("Error: --diagram <file|-> is required (- reads stdin).");
            return 1;
        }

        string diagram;
        try { diagram = ReadContent(diagramArg); }
        catch (Exception ex) { Console.Error.WriteLine($"Error reading diagram: {ex.Message}"); return 1; }

        if (string.IsNullOrWhiteSpace(diagram))
        {
            Console.Error.WriteLine("Error: diagram is empty.");
            return 1;
        }

        string? summary = null;
        if (summaryArg is not null)
        {
            try { summary = ReadContent(summaryArg); }
            catch (Exception ex) { Console.Error.WriteLine($"Error reading summary: {ex.Message}"); return 1; }
        }

        string? layers = null;
        if (layersArg is not null)
        {
            try { layers = ReadContent(layersArg); }
            catch (Exception ex) { Console.Error.WriteLine($"Error reading layers: {ex.Message}"); return 1; }
        }

        var sourceScan = _db.GetLatestScanId(projectId.Value);
        _db.UpsertArchitecture(projectId.Value, diagram, summary, layers, format ?? "mermaid", sourceScan);
        Console.WriteLine($"Architecture analysis saved for project {projectId} (format={format}, {diagram.Length} chars). Marked 'analyzed'.");
        return 0;
    }

    // ---- show: print stored analysis ----
    private int RunShow(string[] args)
    {
        var projectId = ResolveProject(args);
        if (projectId is null) return 1;

        var arch = _db.GetArchitecture(projectId.Value);
        if (arch is null)
        {
            Console.WriteLine($"No architecture analysis for project {projectId}. Run: codescan arch bundle {projectId}");
            return 0;
        }

        Console.WriteLine($"# Architecture — project {arch.ProjectId} ({arch.Format}, analyzed {arch.AnalyzedAt}, state={arch.State ?? "analyzed"})");
        Console.WriteLine();
        Console.WriteLine("## Diagram");
        Console.WriteLine(arch.Diagram);
        if (!string.IsNullOrWhiteSpace(arch.Summary))
        {
            Console.WriteLine();
            Console.WriteLine("## Summary");
            Console.WriteLine(arch.Summary);
        }
        return 0;
    }

    // ---- status: list projects + analysis state ----
    private int RunStatus(string[] args)
    {
        var projects = _db.GetProjects();
        if (projects.Count == 0)
        {
            Console.WriteLine("No indexed projects.");
            return 0;
        }

        Console.WriteLine("  ID   State       Path");
        Console.WriteLine("  --   -----       ----");
        foreach (var p in projects)
        {
            var state = p.AnalysisState ?? "none";
            Console.WriteLine($"  {p.Id,-4} {state,-11} {p.RootPath}");
        }
        return 0;
    }

    // ---- helpers ----

    /// <summary>Resolve the target project from args: first positional long, or --latest,
    /// or fall back to the most-recently-scanned project.</summary>
    private long? ResolveProject(string[] args)
    {
        foreach (var a in args)
        {
            if (a == "--latest") break;
            if (long.TryParse(a, out var id)) return id;
        }

        var latest = _db.GetLatestProjectId();
        if (latest is null)
            Console.Error.WriteLine("Error: no project specified and no scanned projects found.");
        return latest;
    }

    /// <summary>Read from a file path, or from stdin when the argument is "-".</summary>
    private static string ReadContent(string arg)
    {
        if (arg == "-")
            return Console.In.ReadToEnd();
        return File.ReadAllText(arg);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        codescan arch - AI-assisted architecture analysis (AI-outside flow)

        Usage:
          codescan arch bundle <project>                       Emit a graph/code summary (JSON) for an AI
          codescan arch set <project> --diagram <file|-> ...   Persist an AI-produced diagram + summary
          codescan arch show <project>                         Print the stored analysis
          codescan arch status                                 List projects and their analysis state

        arch set options:
          --diagram <file|->    Mermaid source (required; '-' reads stdin)
          --summary <file|->    Layered narrative in markdown (optional)
          --layers  <file>      Optional structured layers JSON
          --format  <name>      Diagram format (default: mermaid)

        Project defaults to the most-recently-scanned project when omitted.

        Flow:
          1) codescan arch bundle 1 > bundle.json     # CLI summarizes the graph
          2) (AI reads bundle.json, writes diagram.mmd + summary.md)
          3) codescan arch set 1 --diagram diagram.mmd --summary summary.md
          4) codescan gui start    # Architecture View renders it
        """);
    }
}

// ---- bundle DTOs (camelCase JSON via ArchJsonContext) ----

public sealed class ArchBundle
{
    public required ArchProject Project { get; init; }
    public List<ArchDir> Directories { get; init; } = [];
    public List<ArchModule> ExternalModules { get; init; } = [];
    public List<ArchClassGroup> ClassesByDir { get; init; } = [];
    public List<ArchRel> TypeRelations { get; init; } = [];
    public List<ArchMention> DocMentions { get; init; } = [];
    public ArchStats Stats { get; init; } = new();
    public string Hint { get; init; } = "";
}

public sealed class ArchProject
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string RootPath { get; init; } = "";
    public string? AddInfo { get; init; }
    public int FileCount { get; init; }
    public int DirCount { get; init; }
}

public sealed class ArchDir
{
    public string Name { get; init; } = "";
    public int Files { get; init; }
}

public sealed class ArchModule
{
    public string Module { get; init; } = "";
    public int Uses { get; init; }
}

public sealed class ArchClassGroup
{
    public string Dir { get; init; } = "";
    public List<string> Classes { get; init; } = [];
}

public sealed class ArchRel
{
    public string From { get; init; } = "";
    public string Kind { get; init; } = "";
    public string To { get; init; } = "";
    public int Weight { get; init; }
}

public sealed class ArchMention
{
    public string Heading { get; init; } = "";
    public string Symbol { get; init; } = "";
}

public sealed class ArchStats
{
    public int Nodes { get; init; }
    public int Edges { get; init; }
    public int Classes { get; init; }
    public int Methods { get; init; }
    public int Files { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ArchBundle))]
internal partial class ArchJsonContext : JsonSerializerContext
{
}
