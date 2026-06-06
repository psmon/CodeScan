using System.Text;
using System.Text.Encodings.Web;
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
    // Pass non-ASCII bytes through as raw UTF-8 instead of escaping them
    // to `\uXXXX`. The default System.Text.Json encoder turns every Korean
    // char into a 6-byte escape sequence, and the model — when echoing the
    // file content back inside its own code block — gets stuck reproducing
    // those sequences and collapses into a `\t\t\t…` degenerate loop until
    // the per-turn token budget is exhausted (see
    // harness/logs/tamer/2026-06-06-…-chatmode-runtime-log-audit.md).
    //
    // Relaxed escaping is "unsafe" only in HTML embedding contexts; we
    // never embed tool results in HTML — they go straight into the LLM
    // prompt as plain text — so the safety caveat doesn't apply.
    private static readonly JsonSerializerOptions RelaxedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
                "project_tree"  => ProjectTree(args),
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

        // Pre-join every hit with its owning project's absolute root_path so
        // each result carries `abs_path` directly. We keep `project_root` ONLY
        // when the hits span multiple projects — duplicating the same 60-char
        // path on every row used to push the 4096-token window out from under
        // a follow-up turn (observed as empty raw in chat-20260529_150535.log
        // after an 8-hit README response).
        var scanIds = results.Select(r => r.ScanId).Distinct();
        var roots = _db.GetProjectRootsByScanIds(scanIds);
        var distinctRoots = roots.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var multiProject = distinctRoots.Count > 1;

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
            if (roots.TryGetValue(r.ScanId, out var root) && !string.IsNullOrEmpty(r.Path))
            {
                try
                {
                    // Build an absolute path even when the index stored a
                    // forward-slash relative path on Linux/macOS or backslash
                    // on Windows — Path.Combine + Path.GetFullPath normalise.
                    obj["abs_path"] = Path.GetFullPath(Path.Combine(root, r.Path));
                }
                catch { /* malformed path — skip abs_path, model can fall back */ }
                if (multiProject) obj["project_root"] = root;
            }
            arr.Add((JsonNode)obj);
        }
        var resp = new JsonObject
        {
            ["ok"] = true,
            ["count"] = results.Count,
            ["results"] = arr,
        };
        // Single-project case: one project_root at the top, not on every hit.
        if (!multiProject && distinctRoots.Count == 1)
            resp["project_root"] = distinctRoots[0];

        // Steer the next turn. P4: 0-hit gets a recovery strategy hint instead
        // of the model giving up to the user. Otherwise reinforce abs_path.
        if (results.Count == 0)
            resp["hint"] = "0 hits. Try: (1) a SHORTER single keyword (FTS treats 'OR'/'AND' as literal terms — don't use them). " +
                          "(2) Vary `type` (file/method/comment/doc/null). " +
                          "(3) Call `project_tree` first to learn folder/file names, then re-query with a name you see.";
        else if (roots.Count > 0)
            resp["hint"] = "Use the `abs_path` field of any result for read_file / grep_file. The `path` field is project-relative.";
        return resp.ToJsonString(RelaxedJsonOptions);
    }

    // ------------------------------------------------------------------
    // read_file
    // ------------------------------------------------------------------
    private string ReadFile(JsonObject args)
    {
        var rawPath = ReadStr(args, "path");
        if (string.IsNullOrWhiteSpace(rawPath)) return Err("path is required");

        var fullPath = ResolvePath(rawPath);
        if (fullPath == null) return Err(BuildPathError("read_file", rawPath));

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
        }.ToJsonString(RelaxedJsonOptions);
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
        if (fullPath == null) return Err(BuildPathError("grep_file", rawPath));

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
        }.ToJsonString(RelaxedJsonOptions);
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
        return result.ToJsonString(RelaxedJsonOptions);
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
        }.ToJsonString(RelaxedJsonOptions);
    }

    // ------------------------------------------------------------------
    // project_tree — compressed directory layout for vocabulary discovery
    //
    // The model can't pick a useful db_search keyword for an abstract
    // request like "show me the app layout" if it has never seen the
    // project's folder names. This tool answers the prerequisite question
    // ("what directories exist?") in one turn, so a follow-up db_search
    // can use real-world terms like "MainWindow" or "Views".
    //
    // Output is intentionally compact — directory paths only (no per-file
    // listing), with each row showing the cumulative file count + top-3
    // extensions so the model can guess what language each subtree is.
    // ------------------------------------------------------------------
    private const int ProjectTreeLineCap = 80;

    private string ProjectTree(JsonObject args)
    {
        var projectId = ReadLong(args, "project_id", 0);
        var maxDepth = Math.Clamp(ReadInt(args, "max_depth", 3), 1, 5);

        // Resolve project: explicit id > captured chat context > most recent.
        if (projectId == 0)
        {
            var projects = _db.GetProjects();
            if (projects.Count == 0)
                return Err("no indexed projects. Run 'codescan scan <path>' first.");
            if (_projectRoot != null)
            {
                var match = projects.FirstOrDefault(p =>
                    string.Equals(p.RootPath, _projectRoot, StringComparison.OrdinalIgnoreCase));
                projectId = match?.Id ?? projects[0].Id;
            }
            else
            {
                projectId = projects[0].Id;
            }
        }

        var project = _db.GetProject(projectId);
        if (project == null) return Err($"project #{projectId} not found. Call list_projects to see valid ids.");

        var files = _db.GetProjectFiles(projectId);
        if (files.Count == 0)
            return Err($"no files indexed for project #{projectId} — run 'codescan scan' first.");

        // Roll up file counts and extension histograms along every parent dir
        // in each file's path, so an intermediate directory shows the total
        // weight of its subtree (not just leaf files).
        var dirStats = new Dictionary<string, (int FileCount, Dictionary<string, int> Exts)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var f in files)
        {
            var norm = f.RelativePath.Replace('\\', '/');
            var parts = norm.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) continue;  // file at project root — no dir

            var ext = string.IsNullOrEmpty(f.Extension) ? "(none)" : f.Extension.TrimStart('.');
            var pathSoFar = "";
            for (var i = 0; i < parts.Length - 1; i++)
            {
                pathSoFar = i == 0 ? parts[0] : pathSoFar + "/" + parts[i];
                var depth = i + 1;
                if (depth > maxDepth) break;

                if (!dirStats.TryGetValue(pathSoFar, out var stat))
                {
                    stat = (0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                    dirStats[pathSoFar] = stat;
                }
                stat.FileCount++;
                stat.Exts[ext] = stat.Exts.TryGetValue(ext, out var v) ? v + 1 : 1;
                dirStats[pathSoFar] = stat;
            }
        }

        // Lexicographic order gives a stable, parent-first listing.
        var sortedDirs = dirStats.Keys.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        sb.Append(Path.GetFileName(project.RootPath.TrimEnd('\\', '/'))).Append("/\n");
        var lines = 1;
        var truncated = false;
        foreach (var dir in sortedDirs)
        {
            if (lines >= ProjectTreeLineCap)
            {
                sb.Append("  … (").Append(sortedDirs.Count - (lines - 1)).Append(" more dirs hidden)\n");
                truncated = true;
                break;
            }
            var depth = dir.Split('/').Length;
            var name = dir.Split('/').Last();
            var stat = dirStats[dir];
            var topExts = stat.Exts
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => $"{kv.Key}={kv.Value}");
            sb.Append(new string(' ', depth * 2))
              .Append(name).Append("/  (")
              .Append(stat.FileCount).Append(" files: ")
              .Append(string.Join(' ', topExts))
              .Append(")\n");
            lines++;
        }

        var resp = new JsonObject
        {
            ["ok"] = true,
            ["project_id"] = project.Id,
            ["project_root"] = project.RootPath,
            ["max_depth"] = maxDepth,
            ["dir_count"] = sortedDirs.Count,
            ["file_count"] = files.Count,
            ["tree"] = sb.ToString(),
        };
        if (truncated)
            resp["note"] = $"output capped at {ProjectTreeLineCap} dirs; pass a smaller max_depth or ask about a subtree by name in db_search.";
        return resp.ToJsonString(RelaxedJsonOptions);
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
        }.ToJsonString(RelaxedJsonOptions);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    // Build a file-not-found error that tells the model how to recover instead
    // of just stating the failure. Without this Gemma typically gives up with
    // a "sorry, file not found" `done` instead of re-issuing with an absolute
    // path — which the same DB lookup would have produced on the very next turn.
    //
    // We also do a basename lookup in the index — when the model passes a
    // path like ".../Project/Foo.ps1" but the actual file lives at
    // ".../Project/SubDir/Foo.ps1", surfacing the correct abs_path as a
    // "Did you mean" hint lets the next turn just copy-paste it. This is
    // the recovery path Nemotron 3 Nano 4B kept hitting in the
    // chat-20260603 log — it would db_search successfully but then
    // hallucinate a shortened abs_path on the follow-up read_file.
    private string BuildPathError(string tool, string rawPath)
    {
        var suggestions = SuggestPathsByBasename(rawPath);
        var hint = suggestions.Count == 0
            ? ""
            : "  Did you mean one of these (copy the path BYTE-FOR-BYTE):\n    - "
              + string.Join("\n    - ", suggestions);

        if (Path.IsPathRooted(rawPath))
            return $"file not found: {rawPath}. Path is absolute — verify it exists, " +
                   $"or call db_search to locate the correct file.{hint}";

        if (_projectRoot == null)
            return $"file not found: {rawPath}. PROJECT CONTEXT is (none) so '{tool}' only accepts ABSOLUTE paths. " +
                   "Call db_search and use the `abs_path` field of a hit, or call list_projects to get a root_path " +
                   $"and join it with the relative path.{hint}";

        return $"file not found: {rawPath} (resolved against project root {_projectRoot}). " +
               $"Re-check the path via db_search, or pass an absolute path.{hint}";
    }

    // Pulls candidate abs_paths from the index by basename. Caps at 3 hits —
    // any more and the tool-result payload starts pushing the chat window
    // around, which is what we're trying to avoid on small-model setups.
    private List<string> SuggestPathsByBasename(string rawPath)
    {
        var basename = Path.GetFileName(rawPath.Replace('\\', '/'));
        if (string.IsNullOrEmpty(basename)) return new List<string>();

        List<SearchResult> hits;
        try { hits = _db.Search(basename, type: "file", limit: 5); }
        catch { return new List<string>(); }
        if (hits.Count == 0) return new List<string>();

        Dictionary<long, string> roots;
        try { roots = _db.GetProjectRootsByScanIds(hits.Select(h => h.ScanId).Distinct()); }
        catch { return new List<string>(); }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picks = new List<string>();
        foreach (var h in hits)
        {
            if (string.IsNullOrEmpty(h.Path)) continue;
            if (!roots.TryGetValue(h.ScanId, out var root)) continue;
            string abs;
            try { abs = Path.GetFullPath(Path.Combine(root, h.Path)); }
            catch { continue; }
            // Skip the exact path the caller already tried — suggesting it
            // back would be confusing ("the file we couldn't find is the
            // file we couldn't find").
            if (string.Equals(abs, rawPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(abs)) picks.Add(abs);
            if (picks.Count >= 3) break;
        }
        return picks;
    }

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
        => new JsonObject { ["ok"] = false, ["error"] = msg }.ToJsonString(RelaxedJsonOptions);

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
