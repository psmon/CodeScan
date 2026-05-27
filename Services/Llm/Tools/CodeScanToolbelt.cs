using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Executes a tool call (parsed from the LLM's JSON output) against the
/// CodeScan SQLite index and the local filesystem. Each method returns a
/// JSON-encoded result string — never raw objects — because the result
/// gets pasted back into the next model turn as plain text.
///
/// Path resolution: relative paths are resolved against the path of the
/// FIRST project the user is interacting with. The chat loop captures the
/// "current project" once at session start; subsequent searches still hit
/// the global index, but `read_file` / `grep_file` interpret relative
/// paths against that root. Absolute paths are honored verbatim.
/// </summary>
public sealed class CodeScanToolbelt
{
    private readonly SqliteStore _db;
    private readonly string? _projectRoot;

    public CodeScanToolbelt(SqliteStore db, string? projectRoot)
    {
        _db = db;
        _projectRoot = projectRoot;
    }

    public string Execute(string toolName, JsonObject args)
    {
        try
        {
            return toolName switch
            {
                "db_search"     => DbSearch(args),
                "read_file"     => ReadFile(args),
                "grep_file"     => GrepFile(args),
                "list_projects" => ListProjects(),
                "project_info"  => ProjectInfo(args),
                "graph_query"   => GraphQuery(args),
                _ => Err($"unknown tool '{toolName}'"),
            };
        }
        catch (Exception ex)
        {
            return Err(ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // db_search
    // ------------------------------------------------------------------
    private string DbSearch(JsonObject args)
    {
        var query = ReadStr(args, "query");
        if (string.IsNullOrWhiteSpace(query)) return Err("query is required");
        var type = ReadNullableStr(args, "type");
        var limit = Math.Clamp(ReadInt(args, "limit", 15), 1, 50);

        var results = _db.Search(query, type, limit);
        var arr = new JsonArray();
        foreach (var r in results)
        {
            var obj = new JsonObject
            {
                ["type"] = r.Type,
                ["name"] = r.Name,
                ["path"] = r.Path,
                ["excerpt"] = Truncate(r.Excerpt, 200),
            };
            arr.Add((JsonNode)obj);
        }
        return new JsonObject
        {
            ["ok"] = true,
            ["count"] = results.Count,
            ["results"] = arr,
        }.ToJsonString();
    }

    // ------------------------------------------------------------------
    // read_file
    // ------------------------------------------------------------------
    private string ReadFile(JsonObject args)
    {
        var rawPath = ReadStr(args, "path");
        if (string.IsNullOrWhiteSpace(rawPath)) return Err("path is required");

        var fullPath = ResolvePath(rawPath);
        if (fullPath == null) return Err($"file not found: {rawPath}");

        var start = ReadInt(args, "start", 1);
        var end = ReadInt(args, "end", 0);  // 0 = read to end (capped)

        string[] lines;
        try { lines = File.ReadAllLines(fullPath); }
        catch (Exception ex) { return Err($"read failed: {ex.Message}"); }

        if (start < 1) start = 1;
        var firstIdx = Math.Min(start - 1, lines.Length);
        var lastIdx = end <= 0 ? Math.Min(lines.Length, firstIdx + 200)  // default slice
                                : Math.Min(lines.Length, end);
        // Hard cap so the tool result doesn't blow the context window.
        lastIdx = Math.Min(lastIdx, firstIdx + 500);

        var sb = new StringBuilder();
        for (int i = firstIdx; i < lastIdx; i++)
            sb.Append(i + 1).Append(':').Append(lines[i]).Append('\n');

        return new JsonObject
        {
            ["ok"] = true,
            ["path"] = fullPath,
            ["total_lines"] = lines.Length,
            ["start"] = firstIdx + 1,
            ["end"] = lastIdx,
            ["text"] = sb.ToString(),
        }.ToJsonString();
    }

    // ------------------------------------------------------------------
    // grep_file
    // ------------------------------------------------------------------
    private string GrepFile(JsonObject args)
    {
        var rawPath = ReadStr(args, "path");
        var pattern = ReadStr(args, "pattern");
        if (string.IsNullOrWhiteSpace(rawPath)) return Err("path is required");
        if (string.IsNullOrWhiteSpace(pattern)) return Err("pattern is required");
        var limit = Math.Clamp(ReadInt(args, "limit", 20), 1, 50);

        var fullPath = ResolvePath(rawPath);
        if (fullPath == null) return Err($"file not found: {rawPath}");

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
        catch (ArgumentException)
        {
            // Fall back to literal match if regex is invalid.
            regex = new Regex(Regex.Escape(pattern), RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        var hits = new JsonArray();
        try
        {
            using var reader = new StreamReader(fullPath);
            int ln = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                ln++;
                if (regex.IsMatch(line))
                {
                    var hit = new JsonObject
                    {
                        ["line"] = ln,
                        ["text"] = Truncate(line, 240),
                    };
                    hits.Add((JsonNode)hit);
                    if (hits.Count >= limit) break;
                }
            }
        }
        catch (Exception ex) { return Err($"grep failed: {ex.Message}"); }

        return new JsonObject
        {
            ["ok"] = true,
            ["path"] = fullPath,
            ["pattern"] = pattern,
            ["count"] = hits.Count,
            ["matches"] = hits,
        }.ToJsonString();
    }

    // ------------------------------------------------------------------
    // list_projects
    // Cap output: a CodeScan DB with 19 indexed projects + verbose addinfo
    // produced a ~4 KB JSON blob that filled the model's 4096-token context
    // window when fed back as a tool_result. Limit to the 8 most-recent
    // projects with addinfo trimmed; expose total count so the model can
    // ask for more or guide the user to filter.
    // ------------------------------------------------------------------
    private const int ListProjectsCap = 8;
    private const int ListProjectsAddInfoMax = 120;

    private string ListProjects()
    {
        var all = _db.GetProjects();
        var shown = all.Take(ListProjectsCap).ToList();
        var arr = new JsonArray();
        foreach (var p in shown)
        {
            var node = new JsonObject
            {
                ["id"] = p.Id,
                ["root_path"] = p.RootPath,
                ["file_count"] = p.FileCount,
                ["last_scanned_at"] = p.LastScannedAt,
                ["addinfo"] = Truncate(p.AddInfo ?? "", ListProjectsAddInfoMax),
            };
            arr.Add((JsonNode)node);
        }

        var result = new JsonObject
        {
            ["ok"] = true,
            ["total"] = all.Count,
            ["shown"] = shown.Count,
            ["projects"] = arr,
        };
        if (all.Count > shown.Count)
            result["note"] = $"only most-recent {shown.Count} of {all.Count} shown; ask user to narrow if needed";
        return result.ToJsonString();
    }

    // ------------------------------------------------------------------
    // project_info
    // ------------------------------------------------------------------
    private string ProjectInfo(JsonObject args)
    {
        var id = ReadLong(args, "id", 0);
        if (id <= 0) return Err("id is required (positive int)");
        var p = _db.GetProject(id);
        if (p == null) return Err($"project #{id} not found");

        return new JsonObject
        {
            ["ok"] = true,
            ["id"] = p.Id,
            ["root_path"] = p.RootPath,
            ["file_count"] = p.FileCount,
            ["dir_count"] = p.DirCount,
            ["total_size"] = p.TotalSize,
            ["last_scanned_at"] = p.LastScannedAt,
            ["addinfo"] = p.AddInfo,
        }.ToJsonString();
    }

    // ------------------------------------------------------------------
    // graph_query (Cypher-like)
    // ------------------------------------------------------------------
    private string GraphQuery(JsonObject args)
    {
        var query = ReadStr(args, "query");
        if (string.IsNullOrWhiteSpace(query)) return Err("query is required");
        var limit = Math.Clamp(ReadInt(args, "limit", 20), 1, 50);

        GraphData graph;
        try { graph = _db.QueryGraph(query, projectId: null, depth: 0, limit: limit); }
        catch (GraphQueryParseException ex) { return Err($"query parse error: {ex.Message}"); }
        catch (Exception ex) { return Err($"query failed: {ex.Message}"); }

        var nodes = new JsonArray();
        foreach (var n in graph.Nodes)
        {
            var node = new JsonObject
            {
                ["id"] = n.Id,
                ["kind"] = n.Kind,
                ["label"] = n.Label,
                ["path"] = n.Path,
            };
            nodes.Add((JsonNode)node);
        }
        var edges = new JsonArray();
        foreach (var e in graph.Edges)
        {
            var edge = new JsonObject
            {
                ["from"] = e.From,
                ["to"] = e.To,
                ["kind"] = e.Kind,
            };
            edges.Add((JsonNode)edge);
        }
        return new JsonObject
        {
            ["ok"] = true,
            ["node_count"] = graph.Nodes.Count,
            ["edge_count"] = graph.Edges.Count,
            ["nodes"] = nodes,
            ["edges"] = edges,
        }.ToJsonString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    private string? ResolvePath(string raw)
    {
        // Absolute path: take it as-is.
        if (Path.IsPathRooted(raw))
            return File.Exists(raw) ? raw : null;

        // Otherwise resolve against the captured project root.
        if (_projectRoot != null)
        {
            var candidate = Path.GetFullPath(Path.Combine(_projectRoot, raw));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static string Err(string msg)
        => new JsonObject { ["ok"] = false, ["error"] = msg }.ToJsonString();

    private static string ReadStr(JsonObject args, string key)
        => args.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s)
            ? s : "";

    private static string? ReadNullableStr(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out var v)) return null;
        if (v is null) return null;
        if (v is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            return s;
        return null;
    }

    private static int ReadInt(JsonObject args, string key, int fallback)
        => args.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<int>(out var i)
            ? i : fallback;

    private static long ReadLong(JsonObject args, string key, long fallback)
        => args.TryGetPropertyValue(key, out var v) && v is JsonValue jv
           && (jv.TryGetValue<long>(out var l) || (jv.TryGetValue<int>(out var i) && (l = i) == l))
            ? l : fallback;
}
