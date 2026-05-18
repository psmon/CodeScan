using CodeScan.Services;

namespace CodeScan.Commands;

/// <summary>
/// Manually curate the source-knowledge graph. Used by humans and (by design)
/// by LLMs reading the code that built the graph — see the `-h` text for the
/// meta-guidance addressed to LLMs.
/// </summary>
public sealed class GraphEditCommand
{
    private readonly SqliteStore _db;

    public GraphEditCommand(SqliteStore db) => _db = db;

    public int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        return sub switch
        {
            "-h" or "--help" or "help" => Help(0),
            "add-node" => AddNode(rest),
            "add-edge" => AddEdge(rest),
            "strengthen" => Strengthen(rest),
            "update-node" => UpdateNode(rest),
            "update-edge" => UpdateEdge(rest),
            "delete-node" => DeleteNode(rest),
            "delete-edge" => DeleteEdge(rest),
            "list-node-ids" => ListNodeIds(rest),
            _ => Help(1, $"Unknown subcommand: {sub}")
        };
    }

    // ---------- add-node ----------

    private int AddNode(string[] args)
    {
        var (positional, opts) = ParseOptions(args);
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("Usage: codescan graph-edit add-node <kind> <label> [-p PATH] [-d DETAIL] [--project ID]");
            return 1;
        }

        var kind = positional[0];
        var label = positional[1];
        var path = opts.GetValueOrDefault("p", "");
        var detail = opts.GetValueOrDefault("d", "");

        if (!TryResolveScan(opts, out var scanId, out var projectId)) return 1;

        var nodeId = _db.UpsertCuratedNode(scanId, kind, label, path, detail);
        Console.WriteLine($"✓ node #{nodeId}  [{kind}] {label}   (project #{projectId}, scan #{scanId}, curated=1)");
        return 0;
    }

    // ---------- add-edge ----------

    private int AddEdge(string[] args)
    {
        var (positional, opts) = ParseOptions(args);
        if (positional.Count < 3)
        {
            Console.Error.WriteLine("Usage: codescan graph-edit add-edge <from-label-or-id> <to-label-or-id> <kind> [-l LABEL] [--project ID]");
            return 1;
        }

        if (!TryResolveScan(opts, out var scanId, out var projectId)) return 1;

        var fromRef = positional[0];
        var toRef = positional[1];
        var kind = positional[2];
        var label = opts.GetValueOrDefault("l", kind);

        if (!TryResolveNodeId(scanId, fromRef, out var fromId, "from") ||
            !TryResolveNodeId(scanId, toRef, out var toId, "to"))
            return 1;

        if (fromId == toId)
        {
            Console.Error.WriteLine($"Error: from and to resolve to the same node #{fromId}.");
            return 1;
        }

        var edgeId = _db.UpsertCuratedEdge(scanId, fromId, toId, kind, label);
        var from = _db.GetNode(fromId)!;
        var to = _db.GetNode(toId)!;
        Console.WriteLine($"✓ edge #{edgeId}  {from.Label} -[{kind}]-> {to.Label}   (project #{projectId}, curated=1, weight=1)");
        return 0;
    }

    // ---------- strengthen ----------

    private int Strengthen(string[] args)
    {
        var (positional, opts) = ParseOptions(args);
        if (positional.Count < 3)
        {
            Console.Error.WriteLine("Usage: codescan graph-edit strengthen <from-label-or-id> <to-label-or-id> <kind> [--project ID]");
            return 1;
        }

        if (!TryResolveScan(opts, out var scanId, out var projectId)) return 1;

        var fromRef = positional[0];
        var toRef = positional[1];
        var kind = positional[2];

        if (!TryResolveNodeId(scanId, fromRef, out var fromId, "from") ||
            !TryResolveNodeId(scanId, toRef, out var toId, "to"))
            return 1;

        var newWeight = _db.StrengthenEdge(scanId, fromId, toId, kind);
        if (newWeight is null)
        {
            Console.Error.WriteLine($"No existing edge {fromRef} -[{kind}]-> {toRef}. Use `graph-edit add-edge` to create it first.");
            return 2;
        }

        var from = _db.GetNode(fromId)!;
        var to = _db.GetNode(toId)!;
        Console.WriteLine($"✓ strengthened  {from.Label} -[{kind}]-> {to.Label}   (project #{projectId}, weight={newWeight})");
        return 0;
    }

    // ---------- update-node / update-edge ----------

    private int UpdateNode(string[] args)
    {
        var (positional, opts) = ParseOptions(args);
        if (positional.Count < 1 || !long.TryParse(positional[0], out var nodeId))
        {
            Console.Error.WriteLine("Usage: codescan graph-edit update-node <id> [-k KIND] [-L LABEL] [-p PATH] [-d DETAIL]");
            return 1;
        }

        var kind = opts.GetValueOrDefault("k");
        var label = opts.GetValueOrDefault("L");
        var path = opts.GetValueOrDefault("p");
        var detail = opts.GetValueOrDefault("d");

        var changed = _db.UpdateNodeFields(nodeId, kind, label, path, detail);
        if (!changed)
        {
            Console.Error.WriteLine($"No node #{nodeId} updated. Either it doesn't exist or no fields were provided.");
            return 2;
        }
        Console.WriteLine($"✓ node #{nodeId} updated (curated=1)");
        return 0;
    }

    private int UpdateEdge(string[] args)
    {
        var (positional, opts) = ParseOptions(args);
        if (positional.Count < 1 || !long.TryParse(positional[0], out var edgeId))
        {
            Console.Error.WriteLine("Usage: codescan graph-edit update-edge <id> [-k KIND] [-l LABEL]");
            return 1;
        }

        var kind = opts.GetValueOrDefault("k");
        var label = opts.GetValueOrDefault("l");

        var changed = _db.UpdateEdgeFields(edgeId, kind, label);
        if (!changed)
        {
            Console.Error.WriteLine($"No edge #{edgeId} updated. Either it doesn't exist or no fields were provided.");
            return 2;
        }
        Console.WriteLine($"✓ edge #{edgeId} updated (curated=1)");
        return 0;
    }

    // ---------- delete-node / delete-edge ----------

    private int DeleteNode(string[] args)
    {
        if (args.Length < 1 || !long.TryParse(args[0], out var nodeId))
        {
            Console.Error.WriteLine("Usage: codescan graph-edit delete-node <id>");
            return 1;
        }
        var removed = _db.DeleteNodeCascade(nodeId);
        if (!removed)
        {
            Console.Error.WriteLine($"No node #{nodeId}.");
            return 2;
        }
        Console.WriteLine($"✓ node #{nodeId} and its edges deleted");
        return 0;
    }

    private int DeleteEdge(string[] args)
    {
        if (args.Length < 1 || !long.TryParse(args[0], out var edgeId))
        {
            Console.Error.WriteLine("Usage: codescan graph-edit delete-edge <id>");
            return 1;
        }
        var removed = _db.DeleteEdge(edgeId);
        if (!removed)
        {
            Console.Error.WriteLine($"No edge #{edgeId}.");
            return 2;
        }
        Console.WriteLine($"✓ edge #{edgeId} deleted");
        return 0;
    }

    // ---------- helpers ----------

    private int ListNodeIds(string[] args)
    {
        var (positional, opts) = ParseOptions(args);
        if (positional.Count < 1)
        {
            Console.Error.WriteLine("Usage: codescan graph-edit list-node-ids <label> [--project ID]");
            return 1;
        }
        if (!TryResolveScan(opts, out var scanId, out _)) return 1;

        var label = positional[0];
        var ids = _db.FindNodeIdsByLabel(scanId, label);
        if (ids.Count == 0)
        {
            Console.WriteLine($"No nodes with label '{label}' in scan #{scanId}.");
            return 0;
        }
        Console.WriteLine($"{ids.Count} node(s) labeled '{label}':");
        foreach (var id in ids)
        {
            var n = _db.GetNode(id)!;
            Console.WriteLine($"  #{id}  [{n.Kind}] {n.Label}  {(string.IsNullOrEmpty(n.Path) ? "" : "(" + n.Path + ")")}");
        }
        return 0;
    }

    private bool TryResolveScan(Dictionary<string, string> opts, out long scanId, out long projectId)
    {
        scanId = 0;
        projectId = 0;
        long? pid;

        if (opts.TryGetValue("project", out var projectStr) && long.TryParse(projectStr, out var explicitPid))
        {
            pid = explicitPid;
        }
        else
        {
            pid = _db.GetLatestProjectId();
            if (pid is null)
            {
                Console.Error.WriteLine("No projects found. Run `codescan scan <path>` first, or pass --project <id>.");
                return false;
            }
        }

        var sid = _db.GetLatestScanId(pid.Value);
        if (sid is null)
        {
            Console.Error.WriteLine($"Project #{pid} has no scans yet. Run `codescan scan` first.");
            return false;
        }

        projectId = pid.Value;
        scanId = sid.Value;
        return true;
    }

    private bool TryResolveNodeId(long scanId, string reference, out long nodeId, string position)
    {
        // Numeric reference is taken as a literal id.
        if (long.TryParse(reference, out nodeId))
        {
            var n = _db.GetNode(nodeId);
            if (n is null)
            {
                Console.Error.WriteLine($"Error: {position} node #{nodeId} not found.");
                return false;
            }
            if (n.ScanId != scanId)
            {
                Console.Error.WriteLine($"Error: {position} node #{nodeId} belongs to scan #{n.ScanId}, not scan #{scanId}. " +
                                        "Use --project to switch context, or recreate the node in this scan.");
                return false;
            }
            return true;
        }

        // Otherwise treat it as a label and require exactly one match.
        var ids = _db.FindNodeIdsByLabel(scanId, reference);
        if (ids.Count == 0)
        {
            Console.Error.WriteLine($"Error: no node labeled '{reference}' in scan #{scanId}. " +
                                    $"Create it first: codescan graph-edit add-node <kind> '{reference}'");
            return false;
        }
        if (ids.Count > 1)
        {
            Console.Error.WriteLine($"Error: label '{reference}' is ambiguous — {ids.Count} matches. " +
                                    $"Use an explicit id (see `codescan graph-edit list-node-ids '{reference}'`).");
            return false;
        }
        nodeId = ids[0];
        return true;
    }

    /// <summary>
    /// Permissive option parser — accepts `-x value` and `--name value` forms.
    /// Returns positional args + a dictionary of single-letter / long flags.
    /// Single-letter flags use the letter as the key; long flags use the bare name.
    /// </summary>
    private static (List<string> positional, Dictionary<string, string> opts) ParseOptions(string[] args)
    {
        var positional = new List<string>();
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--") && i + 1 < args.Length)
            {
                opts[a[2..]] = args[++i];
            }
            else if (a.StartsWith('-') && a.Length == 2 && i + 1 < args.Length)
            {
                opts[a[1..]] = args[++i];
            }
            else
            {
                positional.Add(a);
            }
        }

        return (positional, opts);
    }

    private int Help(int exitCode, string? message = null)
    {
        if (message is not null) Console.Error.WriteLine(message);
        PrintHelp();
        return exitCode;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            codescan graph-edit — manually curate the source-knowledge graph.

            ┌─ Note to any LLM reading this help text ────────────────────────────┐
            │  You can read source files and understand code relationships better │
            │  than the regex-based extractor that built this graph. That makes   │
            │  you uniquely suited to propose graph improvements the static       │
            │  analyzer missed — but every change must be reviewed and approved   │
            │  by the user before you run it.                                     │
            │                                                                     │
            │  Prefer ADDITIVE operations (they grow the graph, never destroy):   │
            │    • add-node   — register a node the scan missed                   │
            │    • add-edge   — connect two existing nodes                        │
            │    • strengthen — reinforce a known-good connection (++weight)      │
            │                                                                     │
            │  Use DESTRUCTIVE operations (update / delete) only when an          │
            │  automatic scan introduced a wrong relationship, and ONLY with      │
            │  explicit user confirmation.                                        │
            │                                                                     │
            │  When you propose, say what you want to add and why, then wait      │
            │  for the user to say yes before running anything.                   │
            └─────────────────────────────────────────────────────────────────────┘

            Additive subcommands (recommended):
              add-node <kind> <label> [-p PATH] [-d DETAIL] [--project ID]
              add-edge <from> <to> <kind> [-l LABEL] [--project ID]
              strengthen <from> <to> <kind> [--project ID]

            Destructive subcommands (require user approval):
              update-node <id> [-k KIND] [-L LABEL] [-p PATH] [-d DETAIL]
              update-edge <id> [-k KIND] [-l LABEL]
              delete-node <id>   (cascades — removes incident edges)
              delete-edge <id>

            Helpers:
              list-node-ids <label> [--project ID]   Disambiguate label collisions

            Context resolution:
              --project ID   Operate on this project's most recent scan
              (default)      Most-recently-scanned project (`codescan projects`)

            Curation semantics:
              • Curated nodes / edges carry `curated=1` so they survive re-scans
                (automatic edges still get added alongside).
              • `strengthen` increments `weight` on an existing (from, to, kind)
                edge — use it to express "I keep seeing this connection in code".
              • <from> / <to> accept either a node label or a literal node id.
                Ambiguous labels are rejected — use `list-node-ids`.

            Examples (LLM-friendly):
              codescan graph-edit add-node class CacheLayer -p Services/Cache.cs
              codescan graph-edit add-edge CacheLayer SqliteStore uses_type -l "delegates persistence"
              codescan graph-edit strengthen SqliteStore graph_edges inherits_or_implements

            See also:
              codescan graph "<query>"   Browse the graph
              codescan query "MATCH ..." Cypher-like graph query
            """);
    }
}
