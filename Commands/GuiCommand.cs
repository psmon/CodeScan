using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeScan.Services;

namespace CodeScan.Commands;

public static class GuiCommand
{
    public static int Start(int port)
    {
        var pidPath = GetPidPath(port);
        if (IsExistingServerRunning(pidPath, out var existingPid))
        {
            Console.WriteLine($"CodeScan GUI is already running on port {port} (PID {existingPid}).");
            Console.WriteLine($"Open http://127.0.0.1:{port}/");
            return 0;
        }

        File.WriteAllText(pidPath, Environment.ProcessId.ToString());

        using var listener = new TcpListener(IPAddress.Loopback, port);

        try
        {
            listener.Start();
        }
        catch
        {
            TryDelete(pidPath);
            throw;
        }

        var stopping = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping = true;
            TryDelete(pidPath);
            listener.Stop();
        };

        Console.WriteLine($"CodeScan GUI listening at http://127.0.0.1:{port}/");
        Console.WriteLine("Press Ctrl+C to stop, or run: codescan gui stop");

        try
        {
            while (!stopping)
            {
                try
                {
                    var client = listener.AcceptTcpClient();
                    _ = Task.Run(() => HandleClient(client));
                }
                catch (SocketException ex) when (!stopping)
                {
                    Console.Error.WriteLine($"[gui] socket accept warning: {ex.SocketErrorCode}");
                    Thread.Sleep(100);
                }
                catch (ObjectDisposedException) when (!stopping)
                {
                    Thread.Sleep(100);
                }
            }
        }
        catch (SocketException) when (stopping)
        {
            return 0;
        }
        catch (ObjectDisposedException) when (stopping)
        {
            return 0;
        }
        finally
        {
            TryDelete(pidPath);
        }

        return 0;
    }

    public static int Stop(int port)
    {
        var pidPath = GetPidPath(port);
        if (!File.Exists(pidPath))
        {
            Console.WriteLine($"No CodeScan GUI PID file found for port {port}.");
            return 1;
        }

        var text = File.ReadAllText(pidPath).Trim();
        if (!int.TryParse(text, out var pid))
        {
            TryDelete(pidPath);
            Console.WriteLine("Stale PID file removed.");
            return 1;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            Console.WriteLine($"Stopped CodeScan GUI on port {port} (PID {pid}).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not stop PID {pid}: {ex.Message}");
            return 1;
        }
        finally
        {
            TryDelete(pidPath);
        }

        return 0;
    }

    private static void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var requestLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(requestLine))
                    return;

                string? line;
                while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }

                var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8", "Only GET is supported.");
                    return;
                }

                var uri = new Uri("http://127.0.0.1" + parts[1]);
                var payload = HandleRequest(uri.AbsolutePath, ParseQuery(uri.Query));
                WriteResponse(stream, payload.StatusCode, payload.StatusText, payload.ContentType, payload.Body);
            }
            catch (Exception ex)
            {
                try
                {
                    using var stream = client.GetStream();
                    WriteResponse(stream, 500, "Internal Server Error", "text/plain; charset=utf-8", ex.Message);
                }
                catch { }
            }
        }
    }

    private static ResponsePayload HandleRequest(string path, Dictionary<string, string> queryString)
    {
        if (path == "/")
            return HtmlResponse(Html);

        if (path == "/favicon.ico")
            return new ResponsePayload(204, "No Content", "image/x-icon", "");

        if (path == "/api/search")
        {
            var query = queryString.GetValueOrDefault("q") ?? "";
            var type = EmptyToNull(queryString.GetValueOrDefault("type"));
            var limit = ParseInt(queryString.GetValueOrDefault("limit"), 50);
            var projectId = ParseLong(queryString.GetValueOrDefault("project"));
            using var db = new SqliteStore(AppPaths.DbPath);
            var results = db.Search(query, type, limit, projectId);
            return JsonResponse(new SearchApiResponse { Results = results });
        }

        if (path == "/api/graph")
        {
            var query = queryString.GetValueOrDefault("q") ?? "";
            var depth = ParseInt(queryString.GetValueOrDefault("depth"), 1);
            var limit = ParseInt(queryString.GetValueOrDefault("limit"), 100);
            var projectId = ParseLong(queryString.GetValueOrDefault("project"));
            using var db = new SqliteStore(AppPaths.DbPath);
            return JsonResponse(db.SearchGraph(query, projectId, depth, limit));
        }

        if (path == "/api/query")
        {
            var query = queryString.GetValueOrDefault("q") ?? "";
            var depth = ParseInt(queryString.GetValueOrDefault("depth"), 0);
            var limit = ParseInt(queryString.GetValueOrDefault("limit"), 100);
            var projectId = ParseLong(queryString.GetValueOrDefault("project"));
            using var db = new SqliteStore(AppPaths.DbPath);
            try
            {
                return JsonResponse(db.QueryGraph(query, projectId, depth, limit));
            }
            catch (GraphQueryParseException ex)
            {
                return new ResponsePayload(400, "Bad Request", "text/plain; charset=utf-8", ex.Message);
            }
        }

        if (path == "/api/projects")
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            return JsonResponse(new ProjectsApiResponse { Projects = db.GetProjects() });
        }

        return new ResponsePayload(404, "Not Found", "text/plain; charset=utf-8", "Not found");
    }

    private static bool IsExistingServerRunning(string pidPath, out int pid)
    {
        pid = 0;
        if (!File.Exists(pidPath)) return false;
        if (!int.TryParse(File.ReadAllText(pidPath).Trim(), out pid))
        {
            TryDelete(pidPath);
            return false;
        }

        try
        {
            _ = Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            TryDelete(pidPath);
            return false;
        }
    }

    private static string GetPidPath(int port) => Path.Combine(AppPaths.GetRunDir(), $"gui-{port}.pid");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static long? ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : null;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return result;
        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            var key = idx >= 0 ? part[..idx] : part;
            var value = idx >= 0 ? part[(idx + 1)..] : "";
            result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
        }
        return result;
    }

    private static ResponsePayload HtmlResponse(string html)
        => new(200, "OK", "text/html; charset=utf-8", html);

    private static ResponsePayload JsonResponse<T>(T value)
    {
        var typeInfo = CodeScanJsonContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata is missing for {typeof(T).Name}.");
        var json = JsonSerializer.Serialize(value, typeInfo);
        return new ResponsePayload(200, "OK", "application/json; charset=utf-8", json);
    }

    private static void WriteResponse(NetworkStream stream, int statusCode, string statusText, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {statusText}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n");
        stream.Write(header);
        stream.Write(bytes);
    }

    private sealed record ResponsePayload(int StatusCode, string StatusText, string ContentType, string Body);

    internal sealed class SearchApiResponse
    {
        public List<SearchResult> Results { get; init; } = [];
    }

    internal sealed class ProjectsApiResponse
    {
        public List<ProjectInfo> Projects { get; init; } = [];
    }

    private const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>CodeScan Graph</title>
  <style>
    :root {
      color-scheme: light;
      --bg:#e9eef5; --surface:#ffffff; --surface-2:#fbfcfe; --stage:#f8fafc;
      --ink:#15202b; --ink-2:#46545f; --muted:#657386;
      --line:#d8e0ea; --soft:#f3f6fa; --chip:#eef3f8;
      --accent:#0b7285; --accent-strong:#095c6b; --accent-soft:#e6f6f8;
      --accent2:#7048e8; --warn:#b7791f; --good:#2f9e44;
      --focus:#15aabf; --focus-ring:#9decf9;
      --r:10px; --r-sm:8px;
      --shadow-sm:0 1px 3px rgba(21,32,38,.08);
      --shadow-md:0 2px 8px rgba(18,37,58,.14);
      --ease:cubic-bezier(.4,0,.2,1);
    }
    * { box-sizing:border-box; }
    body { margin:0; font:14px/1.45 'Inter',system-ui,-apple-system,Segoe UI,sans-serif; color:var(--ink); background:var(--bg); }
    /* Header */
    header { height:60px; display:flex; align-items:center; gap:12px; padding:0 20px; background:var(--surface); border-bottom:1px solid var(--line); box-shadow:var(--shadow-sm); }
    .brand { display:flex; align-items:center; gap:10px; }
    .brand-mark { width:28px; height:28px; border-radius:8px; background:var(--accent); color:#fff; display:flex; align-items:center; justify-content:center; flex:0 0 auto; }
    h1 { font-size:17px; line-height:1; margin:0; font-weight:700; }
    .ver { font:600 11px ui-monospace,SFMono-Regular,Menlo,monospace; color:var(--muted); background:var(--chip); padding:3px 8px; border-radius:999px; }
    .status-pill { margin-left:auto; display:flex; align-items:center; gap:7px; padding:5px 12px; border-radius:999px; background:var(--accent-soft); color:var(--accent-strong); font-size:12px; font-weight:600; }
    .status-pill .live { width:7px; height:7px; border-radius:50%; background:var(--good); flex:0 0 auto; }
    /* Layout */
    main { display:grid; grid-template-columns:340px minmax(420px,1fr) 340px; height:calc(100vh - 60px); min-height:620px; }
    aside, .detail { background:var(--surface); overflow:auto; }
    aside { border-right:1px solid var(--line); padding:16px; display:flex; flex-direction:column; gap:14px; }
    .detail { border-left:1px solid var(--line); display:flex; flex-direction:column; }
    .stage { min-width:0; position:relative; background:var(--stage); transition:background .25s var(--ease); }
    .stage.mode-3d {
      background:
        radial-gradient(ellipse at 18% 12%, rgba(139,92,246,.23), transparent 42%),
        radial-gradient(ellipse at 82% 16%, rgba(236,72,153,.15), transparent 44%),
        radial-gradient(ellipse at 50% 100%, rgba(34,211,238,.15), transparent 52%),
        #060914;
    }
    /* Form controls */
    .field { display:flex; flex-direction:column; gap:6px; }
    .micro, label.micro { display:block; font-size:10px; font-weight:700; letter-spacing:.06em; color:var(--muted); text-transform:uppercase; margin:0; }
    .search-wrap { display:flex; align-items:center; gap:8px; height:40px; padding:0 12px; border:1px solid var(--line); border-radius:var(--r); background:var(--surface-2); transition:border-color .15s,box-shadow .15s; }
    .search-wrap:focus-within { border-color:var(--focus); box-shadow:0 0 0 3px var(--focus-ring); }
    .search-wrap svg { color:var(--accent); flex:0 0 auto; }
    .search-wrap input { flex:1; min-width:0; border:0; outline:0; background:transparent; height:100%; font:12px ui-monospace,SFMono-Regular,Menlo,monospace; color:var(--ink); padding:0; }
    .icon-x { width:22px; height:22px; flex:0 0 auto; border:0; background:transparent; color:var(--muted); cursor:pointer; padding:0; border-radius:6px; font-size:16px; line-height:1; }
    .icon-x:hover { background:var(--chip); }
    .hint-sm { font-size:11px; color:var(--muted); }
    select { width:100%; height:38px; border:1px solid var(--line); border-radius:var(--r); padding:0 32px 0 12px; background-color:var(--surface-2); color:var(--ink); font-size:13px; cursor:pointer; appearance:none; -webkit-appearance:none; background-image:url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='14' height='14' viewBox='0 0 24 24' fill='none' stroke='%23657386' stroke-width='2' stroke-linecap='round'><polyline points='6 9 12 15 18 9'/></svg>"); background-repeat:no-repeat; background-position:right 11px center; }
    /* Segmented mode control */
    .seg { display:flex; gap:0; padding:3px; border-radius:var(--r); background:var(--chip); }
    .seg button { flex:1; height:32px; border:0; border-radius:var(--r-sm); background:transparent; color:var(--muted); font-weight:500; font-size:13px; cursor:pointer; transition:all .15s var(--ease); }
    .seg button.active { background:var(--accent); color:#fff; font-weight:600; box-shadow:var(--shadow-sm); }
    /* Depth pills */
    .pills { display:flex; gap:6px; }
    .pills button { flex:1; height:32px; border:0; border-radius:var(--r-sm); background:var(--chip); color:var(--ink-2); font-weight:500; font-size:13px; cursor:pointer; }
    .pills button.active { background:var(--accent); color:#fff; font-weight:700; }
    /* Checks */
    .checks { display:flex; flex-direction:column; gap:8px; }
    .check { display:flex; gap:8px; align-items:center; color:var(--muted); font-size:12px; cursor:pointer; }
    .check input { width:16px; height:16px; accent-color:var(--accent); }
    /* Legend */
    .legend { display:flex; flex-wrap:wrap; gap:8px; }
    .chip { display:flex; align-items:center; gap:6px; border:1px solid var(--line); border-radius:999px; padding:5px 10px; font-size:12px; font-weight:600; cursor:pointer; user-select:none; background:var(--surface); transition:opacity .15s; }
    .chip .cnt { font:700 11px ui-monospace,SFMono-Regular,Menlo,monospace; }
    .chip.off { opacity:.4; }
    .dot { display:inline-block; width:9px; height:9px; border-radius:50%; flex:0 0 auto; }
    /* Result list cards */
    .list { display:flex; flex-direction:column; gap:8px; }
    .item { display:flex; gap:0; border:1px solid var(--line); border-radius:var(--r); background:var(--surface-2); overflow:hidden; cursor:pointer; transition:border-color .15s,box-shadow .15s; }
    .item:hover { box-shadow:var(--shadow-sm); }
    .item.selected { border-color:var(--accent); background:var(--accent-soft); }
    .item .bar { width:4px; flex:0 0 4px; background:var(--muted); }
    .item .body { padding:9px 12px; min-width:0; flex:1; }
    .item .tag { display:block; font-size:10px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; }
    .item .name { font-size:13px; font-weight:600; color:var(--ink); margin-top:1px; overflow-wrap:anywhere; }
    .meta { color:var(--muted); margin-top:1px; overflow-wrap:anywhere; font:11px ui-monospace,SFMono-Regular,Menlo,monospace; }
    /* Floating toolbar / hint */
    .toolbar { position:absolute; left:16px; top:16px; display:flex; gap:6px; align-items:center; padding:6px; border-radius:12px; background:var(--surface); border:1px solid var(--line); box-shadow:var(--shadow-md); }
    .toolbar button { height:30px; border:1px solid var(--line); background:var(--surface-2); border-radius:8px; padding:0 13px; color:var(--ink-2); cursor:pointer; font-size:12px; font-weight:500; }
    .toolbar button.active { background:var(--accent); color:#fff; border-color:var(--accent); font-weight:600; }
    .hintbar { position:absolute; left:16px; bottom:14px; right:16px; display:flex; justify-content:space-between; gap:10px; pointer-events:none; }
    .hintbar span { background:var(--surface); border:1px solid var(--line); border-radius:999px; padding:7px 12px; color:var(--muted); font-size:12px; box-shadow:var(--shadow-sm); }
    .stage.mode-3d .toolbar { background:rgba(10,16,34,.8); border-color:rgba(125,211,252,.3); box-shadow:0 0 18px rgba(34,211,238,.17); backdrop-filter:blur(10px); }
    .stage.mode-3d .toolbar button { color:#dbeafe; border-color:rgba(125,211,252,.26); background:rgba(10,16,34,.72); }
    .stage.mode-3d .toolbar button.active { color:#67e8f9; border-color:#22d3ee; background:rgba(8,47,73,.74); box-shadow:0 0 16px rgba(34,211,238,.24); }
    .stage.mode-3d .hintbar span { background:rgba(10,16,34,.8); border-color:rgba(125,211,252,.24); color:#b6c7e6; text-shadow:0 0 14px rgba(34,211,238,.36); backdrop-filter:blur(8px); }
    /* Detail */
    .panel-head { padding:16px; border-bottom:1px solid var(--line); background:var(--surface-2); }
    .kind-badge { display:inline-flex; align-items:center; gap:6px; padding:4px 10px; border-radius:999px; font-size:11px; font-weight:700; letter-spacing:.04em; background:var(--chip); color:var(--muted); }
    .panel-head h2 { margin:8px 0 4px; font-size:18px; line-height:1.2; overflow-wrap:anywhere; }
    .panel-head .meta { display:flex; align-items:center; gap:6px; }
    .panel-body { padding:16px; overflow:auto; }
    .kv { display:flex; flex-direction:column; gap:0; margin:0 0 6px; }
    .kv > div { display:flex; flex-direction:column; gap:3px; padding:10px 0; border-bottom:1px solid var(--line); }
    .kv > div:last-child { border-bottom:0; }
    .kv .k { font-size:10px; font-weight:700; letter-spacing:.06em; text-transform:uppercase; color:var(--muted); }
    .kv .v { font-size:13px; color:var(--ink); overflow-wrap:anywhere; }
    .rel h3 { font-size:10px; font-weight:700; letter-spacing:.06em; color:var(--muted); margin:14px 0 6px; text-transform:uppercase; }
    .rel-row { display:flex; align-items:center; gap:8px; padding:8px; border-radius:var(--r-sm); cursor:pointer; transition:background .12s; }
    .rel-row:hover { background:var(--accent-soft); }
    .rel-kind { font:600 10px ui-monospace,SFMono-Regular,Menlo,monospace; color:var(--accent-strong); background:var(--chip); padding:2px 7px; border-radius:6px; flex:0 0 auto; }
    .rel-row .arrow { color:var(--muted); flex:0 0 auto; }
    .rel-row .tgt { font-size:12px; font-weight:500; color:var(--ink); flex:1; overflow-wrap:anywhere; }
    .empty { color:var(--muted); padding:12px 0; font-size:13px; }
    #graphCanvas { width:100%; height:100%; display:block; cursor:grab; }
    #graphCanvas.space-mode { background:#060914; }
    #graphCanvas.dragging { cursor:grabbing; }
    *:focus-visible { outline:2px solid var(--focus); outline-offset:2px; }
    @media (prefers-reduced-motion: reduce) { * { transition:none !important; animation:none !important; } }
    @media (max-width: 980px) { main { grid-template-columns:1fr; grid-template-rows:auto 70vh auto; height:auto; } aside, .detail { border:0; border-bottom:1px solid var(--line); } }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <span class="brand-mark"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><circle cx="18" cy="6" r="3"/><path d="M18 9v1a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V9"/><path d="M12 12v3"/></svg></span>
      <h1>CodeScan Graph</h1>
      <span class="ver">v0.11</span>
    </div>
    <div class="status-pill"><span class="live"></span><span id="stats">Ready</span></div>
  </header>
  <main>
    <aside>
      <div class="field">
        <div class="search-wrap">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
          <input id="query" placeholder="keyword or MATCH (c:class)-[r]->(t:type)" />
          <button id="clear" class="icon-x" title="Clear" aria-label="Clear search">&times;</button>
        </div>
        <div class="hint-sm">Press Enter to run &middot; MATCH auto-routes to Query</div>
      </div>
      <div class="seg" role="group" aria-label="Search mode">
        <button id="graph" class="active">Graph</button>
        <button id="graphQuery">Query</button>
        <button id="keyword">Keyword</button>
      </div>
      <div class="field">
        <label class="micro" for="project">Project</label>
        <select id="project"><option value="">All latest projects</option></select>
      </div>
      <div class="field">
        <label class="micro" for="type">Keyword Type</label>
        <select id="type">
          <option value="">All</option><option value="method">Method</option><option value="file">File</option><option value="doc">Doc</option><option value="comment">Comment</option><option value="commit">Commit</option>
        </select>
      </div>
      <div class="field">
        <label class="micro">Graph Depth</label>
        <div class="pills" id="depthPills">
          <button type="button" data-d="0">0</button><button type="button" data-d="1" class="active">1</button><button type="button" data-d="2">2</button><button type="button" data-d="3">3</button><button type="button" data-d="4">4</button>
        </div>
        <input type="hidden" id="depth" value="1" />
      </div>
      <div class="field">
        <label class="micro" for="querySample">Graph Query Samples</label>
        <select id="querySample">
          <option value="">Choose a MATCH query sample...</option>
          <option value="MATCH (f:file)-[r:imports]->(m:module) WHERE m.label CONTAINS 'System' LIMIT 40">Files importing System modules</option>
          <option value="MATCH (c:class)-[r:uses_type]->(t:type) WHERE t.label CONTAINS 'Actor' LIMIT 40">Classes using Actor-related types</option>
          <option value="MATCH (c:class)-[r:creates]->(t:type) LIMIT 40">Classes creating object types</option>
          <option value="MATCH (c:class)-[r:inherits_or_implements]->(t:type) LIMIT 40">Inheritance and interface links</option>
          <option value="MATCH (a:author)-[r:authored]->(m:method) LIMIT 40">Authors and authored methods</option>
          <option value="MATCH (f:file)-[r:has_comment]->(cm:comment) LIMIT 40">Files with comment nodes</option>
        </select>
      </div>
      <div class="checks">
        <label class="check"><input type="checkbox" id="labels" checked /> <span>Show node labels</span></label>
        <label class="check"><input type="checkbox" id="edgeLabels" checked /> <span>Show edge labels</span></label>
      </div>
      <div class="field"><label class="micro">Node Types</label><div class="legend" id="legend"></div></div>
      <div class="field"><label class="micro">Results</label><div class="list" id="list"></div></div>
    </aside>
    <section class="stage" id="stage">
      <canvas id="graphCanvas"></canvas>
      <div class="toolbar">
        <button id="view2d" class="active">2D</button>
        <button id="view3d">3D</button>
        <button id="fit">Fit</button>
        <button id="resetCamera">Reset Camera</button>
      </div>
      <div class="hintbar"><span id="modeHint">2D: drag canvas to pan, wheel to zoom, drag node to reposition</span><span>Click node or edge for detail</span></div>
    </section>
    <section class="detail">
      <div class="panel-head">
        <span class="kind-badge" id="detailKind">DETAIL</span>
        <h2 id="detailTitle">No selection</h2>
        <div class="meta" id="detailMeta">Run graph search, then click a node or edge.</div>
      </div>
      <div class="panel-body" id="detailBody"></div>
    </section>
  </main>
  <script>
    const $ = id => document.getElementById(id);
    const canvas = $("graphCanvas"), ctx = canvas.getContext("2d");
    const colors = { project:"#0b7285", directory:"#5c7cfa", file:"#2f9e44", class:"#f08c00", method:"#c2255c", comment:"#7048e8", doc:"#087f5b", author:"#495057", type:"#b7791f", module:"#1971c2" };
    const state = {
      graph:{nodes:[], edges:[]}, visibleKinds:new Set(), selected:null, hovered:null, mode:"2d",
      view:{x:0,y:0,zoom:1}, camera:{yaw:-0.45,pitch:0.55,distance:720,x:0,y:0},
      pointer:null, dragNode:null, screen:new Map(), edgeScreen:[], stars:[], animation:null, time:0
    };
    let cw = 800, ch = 600;
    function fitCanvas(){ const r=canvas.getBoundingClientRect(); const d=devicePixelRatio||1; cw=Math.max(320,r.width); ch=Math.max(260,r.height); const w=Math.round(cw*d), h=Math.round(ch*d); if(canvas.width!==w || canvas.height!==h){ canvas.width=w; canvas.height=h; if(state.mode==="3d") state.stars=[]; } ctx.setTransform(d,0,0,d,0,0); }
    addEventListener("resize", () => { fitCanvas(); draw(); });
    fitCanvas();
    async function api(path){ const r=await fetch(path); if(!r.ok) throw new Error(await r.text()); return await r.json(); }
    async function loadProjects(){ const data=await api("/api/projects"); for(const p of data.projects){ const o=document.createElement("option"); o.value=p.id; o.textContent=`#${p.id} ${p.rootPath}`; $("project").appendChild(o); } }
    function params(){ const q=encodeURIComponent($("query").value.trim()); const p=$("project").value; return `q=${q}${p?`&project=${p}`:""}`; }
    $("graph").onclick = async () => runGraphSearch();
    $("graphQuery").onclick = async () => runGraphQuery();
    $("querySample").onchange = () => { const v=$("querySample").value; if(!v) return; $("query").value=v; setDepth(1); $("query").focus(); };
    function setDepth(v){ $("depth").value=String(v); document.querySelectorAll("#depthPills button").forEach(b=>b.classList.toggle("active",b.dataset.d===String(v))); }
    document.querySelectorAll("#depthPills button").forEach(b=>b.onclick=()=>{ setDepth(b.dataset.d); runGraphSearch(); });
    ["graph","graphQuery","keyword"].forEach(id=>$(id).addEventListener("click",()=>{ ["graph","graphQuery","keyword"].forEach(x=>$(x).classList.toggle("active",x===id)); }));
    $("keyword").onclick = async () => { const t=$("type").value; const data=await api(`/api/search?limit=80${t?`&type=${t}`:""}&${params()}`); renderKeywordResults(data.results); };
    $("clear").onclick = () => { $("query").value=""; setGraph({nodes:[],edges:[]}); renderDetail(null); };
    $("query").addEventListener("keydown", e => { if(e.key==="Enter") ($("query").value.trim().toLowerCase().startsWith("match ") ? $("graphQuery") : $("graph")).click(); });
    $("labels").onchange = draw; $("edgeLabels").onchange = draw;
    $("fit").onclick = () => { fitView(); draw(); };
    $("resetCamera").onclick = () => { state.view={x:0,y:0,zoom:1}; state.camera={yaw:-0.45,pitch:0.55,distance:720,x:0,y:0}; fitView(); draw(); };
    $("view2d").onclick = () => setMode("2d");
    $("view3d").onclick = () => setMode("3d");
    async function runGraphSearch(){ try{ const data=await api(`/api/graph?depth=${$("depth").value}&limit=180&${params()}`); setGraph(data); } catch(e){ $("stats").textContent=e.message; } }
    async function runGraphQuery(){ try{ const data=await api(`/api/query?depth=${$("depth").value}&limit=180&${params()}`); setGraph(data); } catch(e){ $("stats").textContent=`Query error: ${e.message}`; } }
    function setMode(mode){
      state.mode=mode;
      const is3d=mode==="3d";
      $("view2d").classList.toggle("active",!is3d); $("view3d").classList.toggle("active",is3d);
      $("stage").classList.toggle("mode-3d",is3d); canvas.classList.toggle("space-mode",is3d);
      $("modeHint").textContent = is3d ? "3D: drag to orbit, Shift/right-drag to pan, wheel to zoom" : "2D: drag canvas to pan, wheel to zoom, drag node to reposition";
      if(is3d) startSpaceAnimation(); else stopSpaceAnimation();
      draw();
    }
    function setGraph(data){
      state.graph = { nodes:(data.nodes||[]).map(n=>({...n})), edges:(data.edges||[]).map(e=>({...e})) };
      state.selected=null; layoutGraph(); buildLegend(); renderList(); fitView(); draw();
      $("stats").textContent = `${state.graph.nodes.length} nodes, ${state.graph.edges.length} edges`;
      if(state.graph.nodes[0]) selectNode(state.graph.nodes[0]);
    }
    function layoutGraph(){
      const nodes=state.graph.nodes, edges=state.graph.edges, map=new Map(nodes.map(n=>[n.id,n]));
      nodes.forEach((n,i)=>{ const a=i*2.399, r=60+22*Math.sqrt(i+1); n.x=Math.cos(a)*r; n.y=Math.sin(a)*r; n.z=(i%7-3)*42; n.vx=0; n.vy=0; n.r=nodeRadius(n); });
      for(let iter=0; iter<180 && nodes.length<260; iter++){
        for(let i=0;i<nodes.length;i++) for(let j=i+1;j<nodes.length;j++){ const a=nodes[i], b=nodes[j], dx=a.x-b.x, dy=a.y-b.y, d2=dx*dx+dy*dy+0.1, f=900/d2; a.vx+=dx*f*.002; a.vy+=dy*f*.002; b.vx-=dx*f*.002; b.vy-=dy*f*.002; }
        for(const e of edges){ const a=map.get(e.from), b=map.get(e.to); if(!a||!b) continue; const dx=b.x-a.x, dy=b.y-a.y, d=Math.hypot(dx,dy)||1, target=90; const f=(d-target)*0.006; a.vx+=dx/d*f; a.vy+=dy/d*f; b.vx-=dx/d*f; b.vy-=dy/d*f; }
        for(const n of nodes){ n.vx*=0.82; n.vy*=0.82; n.x+=n.vx; n.y+=n.vy; }
      }
    }
    function nodeRadius(n){ return n.kind==="project"?11:n.kind==="directory"?8:n.kind==="class"?8:n.kind==="method"?6:n.kind==="type"?7:5; }
    function buildLegend(){ const counts={}; for(const n of state.graph.nodes) counts[n.kind]=(counts[n.kind]||0)+1; const kinds=Object.keys(counts).sort(); state.visibleKinds=new Set(kinds); $("legend").innerHTML=""; for(const k of kinds){ const c=colors[k]||"#748094"; const el=document.createElement("span"); el.className="chip"; el.style.background=c+"1f"; el.style.borderColor=c+"55"; el.innerHTML=`<span class="dot" style="background:${c}"></span>${escapeHtml(k)}<span class="cnt" style="color:${c}">${counts[k]}</span>`; el.onclick=()=>{ if(state.visibleKinds.has(k)) state.visibleKinds.delete(k); else state.visibleKinds.add(k); el.classList.toggle("off",!state.visibleKinds.has(k)); renderList(); draw(); }; $("legend").appendChild(el); } }
    function visibleNode(n){ return state.visibleKinds.size===0 || state.visibleKinds.has(n.kind); }
    function visibleEdge(e,map){ const a=map.get(e.from), b=map.get(e.to); return a&&b&&visibleNode(a)&&visibleNode(b); }
    function renderList(){ const root=$("list"); root.innerHTML=""; for(const n of state.graph.nodes.filter(visibleNode).slice(0,120)){ const c=colors[n.kind]||"#748094"; const d=document.createElement("div"); d.className="item"; d.dataset.id=n.id; d.innerHTML=`<span class="bar" style="background:${c}"></span><div class="body"><span class="tag" style="color:${c}">${escapeHtml(n.kind)}</span><div class="name">${escapeHtml(n.label)}</div><div class="meta">${escapeHtml(n.path||n.detail||"")}</div></div>`; d.onclick=()=>selectNode(n); root.appendChild(d); } }
    function renderKeywordResults(rows){ $("stats").textContent=`${rows.length} keyword results`; $("list").innerHTML=""; for(const r of rows){ const c=colors[r.type]||"#748094"; const d=document.createElement("div"); d.className="item"; d.innerHTML=`<span class="bar" style="background:${c}"></span><div class="body"><span class="tag" style="color:${c}">${escapeHtml(r.type)}</span><div class="name">${escapeHtml(r.name)}</div><div class="meta">${escapeHtml(r.path||"")}</div>${r.excerpt?`<div class="meta">${escapeHtml(r.excerpt)}</div>`:""}</div>`; $("list").appendChild(d); } }
    function fitView(){ const ns=state.graph.nodes.filter(visibleNode); if(!ns.length){state.view={x:0,y:0,zoom:1}; return;} let minX=Infinity,maxX=-Infinity,minY=Infinity,maxY=-Infinity; for(const n of ns){minX=Math.min(minX,n.x);maxX=Math.max(maxX,n.x);minY=Math.min(minY,n.y);maxY=Math.max(maxY,n.y);} const sx=cw/Math.max(80,maxX-minX+120), sy=ch/Math.max(80,maxY-minY+120); state.view.zoom=Math.min(2.2,Math.max(.35,Math.min(sx,sy))); state.view.x=cw/2-(minX+maxX)/2*state.view.zoom; state.view.y=ch/2-(minY+maxY)/2*state.view.zoom; }
    function worldToScreen(n){ if(state.mode==="2d") return {x:n.x*state.view.zoom+state.view.x,y:n.y*state.view.zoom+state.view.y,s:state.view.zoom,z:0}; const cam=state.camera, cyaw=Math.cos(cam.yaw), syaw=Math.sin(cam.yaw), cp=Math.cos(cam.pitch), sp=Math.sin(cam.pitch); let x=n.x, y=n.y, z=n.z||0; let x1=x*cyaw-z*syaw, z1=x*syaw+z*cyaw; let y1=y*cp-z1*sp, z2=y*sp+z1*cp; const s=cam.distance/(cam.distance+z2+320); return {x:cw/2+cam.x+x1*s,y:ch/2+cam.y+y1*s,s,z:z2}; }
    function draw(){ fitCanvas(); ctx.clearRect(0,0,cw,ch); if(state.mode==="3d") drawSpaceBackground(); else { ctx.fillStyle="#f8fafc"; ctx.fillRect(0,0,cw,ch); drawGrid(); } const map=new Map(state.graph.nodes.map(n=>[n.id,n])); state.screen.clear(); state.edgeScreen=[]; const edges=state.graph.edges.filter(e=>visibleEdge(e,map)); for(const e of edges){ const a=map.get(e.from), b=map.get(e.to), p=worldToScreen(a), q=worldToScreen(b); state.edgeScreen.push({edge:e,p,q}); drawEdge(e,p,q); } const nodes=state.graph.nodes.filter(visibleNode).map(n=>({n,p:worldToScreen(n)})).sort((a,b)=>a.p.z-b.p.z); for(const it of nodes){ state.screen.set(it.n.id,it.p); drawNode(it.n,it.p); } ctx.shadowBlur=0; ctx.globalAlpha=1; }
    function drawGrid(){ ctx.strokeStyle="#e8edf3"; ctx.lineWidth=1; const step=48; const ox=state.mode==="2d"?state.view.x%step:0, oy=state.mode==="2d"?state.view.y%step:0; for(let x=ox;x<cw;x+=step){ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x,ch);ctx.stroke();} for(let y=oy;y<ch;y+=step){ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(cw,y);ctx.stroke();} }
    function drawSpaceBackground(){
      ensureStars();
      const g=ctx.createRadialGradient(cw*.5,ch*.48,20,cw*.5,ch*.5,Math.max(cw,ch)*.72);
      g.addColorStop(0,"#111a3a"); g.addColorStop(.46,"#080d20"); g.addColorStop(1,"#03050c");
      ctx.fillStyle=g; ctx.fillRect(0,0,cw,ch);
      const t=state.time||performance.now()*.001;
      for(const s of state.stars){
        const drift=(t*s.speed*10)%cw, x=(s.x+drift*s.z)%cw, y=s.y+Math.sin(t*s.speed+s.phase)*5*s.z;
        const tw=.44+.46*Math.sin(t*(1.2+s.speed)+s.phase);
        ctx.globalAlpha=Math.max(.18,tw)*s.z;
        ctx.fillStyle=s.hue%3===0?"#e0f2fe":s.hue%3===1?"#c4b5fd":"#fbcfe8";
        ctx.beginPath(); ctx.arc(x,y,s.size*s.z,0,Math.PI*2); ctx.fill();
      }
      ctx.globalAlpha=1;
      drawSpaceGrid(t);
    }
    function drawSpaceGrid(t){
      const cx=cw/2+state.camera.x*.16, cy=ch/2+state.camera.y*.16, maxR=Math.max(cw,ch)*.72;
      ctx.save();
      ctx.translate(cx,cy);
      ctx.rotate(state.camera.yaw*.18);
      for(let r=96;r<maxR;r+=96){
        ctx.strokeStyle=`rgba(34,211,238,${Math.max(.025,.11-r/maxR*.08)})`;
        ctx.lineWidth=1;
        ctx.beginPath(); ctx.ellipse(0,0,r,r*.34,0,0,Math.PI*2); ctx.stroke();
      }
      for(let a=0;a<Math.PI*2;a+=Math.PI/8){
        ctx.strokeStyle="rgba(167,139,250,.055)";
        ctx.beginPath(); ctx.moveTo(0,0); ctx.lineTo(Math.cos(a)*maxR,Math.sin(a)*maxR*.34); ctx.stroke();
      }
      ctx.restore();
      const scanY=(t*42)%Math.max(1,ch);
      const beam=ctx.createLinearGradient(0,scanY-38,0,scanY+38);
      beam.addColorStop(0,"rgba(34,211,238,0)"); beam.addColorStop(.5,"rgba(34,211,238,.055)"); beam.addColorStop(1,"rgba(34,211,238,0)");
      ctx.fillStyle=beam; ctx.fillRect(0,scanY-38,cw,76);
    }
    function ensureStars(){
      const target=Math.max(150,Math.min(320,Math.round(cw*ch/4200)));
      if(state.stars.length===target) return;
      state.stars=[];
      for(let i=0;i<target;i++) state.stars.push({ x:rnd(i,1)*cw, y:rnd(i,2)*ch, z:.28+rnd(i,3)*.9, size:.55+rnd(i,4)*1.75, phase:rnd(i,5)*Math.PI*2, speed:.12+rnd(i,6)*.72, hue:i });
    }
    function rnd(i,s){ const x=Math.sin((i+1)*(s*97.13))*10000; return x-Math.floor(x); }
    function startSpaceAnimation(){ if(state.animation) return; if(window.matchMedia&&window.matchMedia("(prefers-reduced-motion: reduce)").matches){ draw(); return; } const tick=now=>{ state.animation=requestAnimationFrame(tick); state.time=now*.001; if(state.mode==="3d") draw(); }; state.animation=requestAnimationFrame(tick); }
    function stopSpaceAnimation(){ if(!state.animation) return; cancelAnimationFrame(state.animation); state.animation=null; }
    function drawEdge(e,p,q){
      const active=state.selected?.type==="edge"&&state.selected.item.id===e.id;
      if(state.mode==="3d"){
        const depth=Math.max(.28,Math.min(1.15,(p.s+q.s)/2));
        const grd=ctx.createLinearGradient(p.x,p.y,q.x,q.y);
        grd.addColorStop(0,active?"#f472b6":"rgba(34,211,238,.78)");
        grd.addColorStop(1,active?"#67e8f9":"rgba(167,139,250,.62)");
        ctx.strokeStyle=grd; ctx.lineWidth=(active?2.9:1.15)*depth; ctx.shadowColor=active?"rgba(236,72,153,.68)":"rgba(34,211,238,.32)"; ctx.shadowBlur=active?18:8;
        ctx.beginPath(); ctx.moveTo(p.x,p.y); ctx.lineTo(q.x,q.y); ctx.stroke(); ctx.shadowBlur=0;
        if($("edgeLabels").checked && (active || e.kind)){ const mx=(p.x+q.x)/2, my=(p.y+q.y)/2; const text=e.kind||e.label||""; ctx.font="11px system-ui"; const w=ctx.measureText(text).width+12; ctx.fillStyle="rgba(4,9,22,.78)"; ctx.fillRect(mx-w/2,my-10,w,18); ctx.fillStyle=active?"#f9a8d4":"#bae6fd"; ctx.fillText(text,mx-w/2+6,my+3); }
        return;
      }
      ctx.strokeStyle=active?"#0b7285":"#b9c4d0"; ctx.lineWidth=active?2.8:1.2; ctx.beginPath(); ctx.moveTo(p.x,p.y); ctx.lineTo(q.x,q.y); ctx.stroke(); if($("edgeLabels").checked && (active || e.kind)){ const mx=(p.x+q.x)/2, my=(p.y+q.y)/2; ctx.fillStyle="rgba(255,255,255,.9)"; const text=e.kind||e.label||""; ctx.font="11px system-ui"; const w=ctx.measureText(text).width+10; ctx.fillRect(mx-w/2,my-9,w,16); ctx.fillStyle="#536173"; ctx.fillText(text,mx-w/2+5,my+3); }
    }
    function drawNode(n,p){
      const active=state.selected?.type==="node"&&state.selected.item.id===n.id;
      const r=Math.max(4,n.r*(state.mode==="3d"?p.s:1));
      if(state.mode==="3d"){
        const base=colors[n.kind]||"#748094", pulse=active?1+Math.sin((state.time||0)*4)*.08:1;
        const grad=ctx.createRadialGradient(p.x-r*.35,p.y-r*.45,1,p.x,p.y,r*1.6*pulse);
        grad.addColorStop(0,"#ffffff"); grad.addColorStop(.22,base); grad.addColorStop(1,"rgba(12,18,36,.18)");
        ctx.fillStyle=grad; ctx.strokeStyle=active?"#f9a8d4":"rgba(219,234,254,.86)"; ctx.lineWidth=active?2.8:1.15; ctx.shadowColor=active?"rgba(236,72,153,.78)":"rgba(34,211,238,.34)"; ctx.shadowBlur=active?28:12;
        ctx.beginPath(); if(n.kind==="file"||n.kind==="doc") roundedRect(p.x-r,p.y-r*.75,r*2,r*1.5,4); else ctx.arc(p.x,p.y,r*pulse,0,Math.PI*2); ctx.fill(); ctx.stroke(); ctx.shadowBlur=0;
        if(active){ ctx.strokeStyle="rgba(34,211,238,.45)"; ctx.lineWidth=1.1; ctx.beginPath(); ctx.arc(p.x,p.y,r*2.15+Math.sin((state.time||0)*3)*3,0,Math.PI*2); ctx.stroke(); }
        if($("labels").checked || active){ ctx.font=active?"700 12px system-ui":"12px system-ui"; ctx.fillStyle=active?"#fce7f3":"#dbeafe"; ctx.shadowColor="rgba(3,7,18,.9)"; ctx.shadowBlur=8; ctx.fillText(trim(n.label,36),p.x+r+7,p.y-6); ctx.shadowBlur=0; }
        return;
      }
      ctx.fillStyle=colors[n.kind]||"#748094"; ctx.strokeStyle=active?"#111827":"#fff"; ctx.lineWidth=active?3:1.5; ctx.beginPath(); if(n.kind==="file"||n.kind==="doc") roundedRect(p.x-r,p.y-r*.75,r*2,r*1.5,4); else ctx.arc(p.x,p.y,r,0,Math.PI*2); ctx.fill(); ctx.stroke(); if($("labels").checked || active){ ctx.font=active?"700 12px system-ui":"12px system-ui"; ctx.fillStyle="#15202b"; ctx.fillText(trim(n.label,36),p.x+r+6,p.y-6); }
    }
    function roundedRect(x,y,w,h,r){ ctx.beginPath(); ctx.moveTo(x+r,y); ctx.lineTo(x+w-r,y); ctx.quadraticCurveTo(x+w,y,x+w,y+r); ctx.lineTo(x+w,y+h-r); ctx.quadraticCurveTo(x+w,y+h,x+w-r,y+h); ctx.lineTo(x+r,y+h); ctx.quadraticCurveTo(x,y+h,x,y+h-r); ctx.lineTo(x,y+r); ctx.quadraticCurveTo(x,y,x+r,y); }
    function pickNode(x,y){ let best=null, bd=Infinity; for(const n of state.graph.nodes.filter(visibleNode)){ const p=state.screen.get(n.id); if(!p) continue; const d=Math.hypot(x-p.x,y-p.y), r=Math.max(7,n.r*(state.mode==="3d"?p.s:state.view.zoom)); if(d<r+5 && d<bd){ best=n; bd=d; } } return best; }
    function pickEdge(x,y){ let best=null, bd=9; for(const it of state.edgeScreen){ const d=distToSegment(x,y,it.p.x,it.p.y,it.q.x,it.q.y); if(d<bd){best=it.edge;bd=d;} } return best; }
    canvas.addEventListener("mousedown", e=>{ const p=pos(e); canvas.classList.add("dragging"); const n=pickNode(p.x,p.y); state.pointer={x:p.x,y:p.y,button:e.button,shift:e.shiftKey}; if(n&&state.mode==="2d"&&e.button===0){state.dragNode=n; selectNode(n);} else if(n){selectNode(n);} else { const edge=pickEdge(p.x,p.y); if(edge) selectEdge(edge); } });
    canvas.addEventListener("mousemove", e=>{ const p=pos(e); if(!state.pointer){ const n=pickNode(p.x,p.y), edge=n?null:pickEdge(p.x,p.y); state.hovered=n||edge; draw(); return; } const dx=p.x-state.pointer.x, dy=p.y-state.pointer.y; if(state.dragNode&&state.mode==="2d"){ state.dragNode.x=(p.x-state.view.x)/state.view.zoom; state.dragNode.y=(p.y-state.view.y)/state.view.zoom; } else if(state.mode==="2d"){ state.view.x+=dx; state.view.y+=dy; } else if(state.pointer.shift||state.pointer.button===2){ state.camera.x+=dx; state.camera.y+=dy; } else { state.camera.yaw+=dx*.008; state.camera.pitch=Math.max(-1.2,Math.min(1.2,state.camera.pitch+dy*.008)); } state.pointer.x=p.x; state.pointer.y=p.y; draw(); });
    addEventListener("mouseup",()=>{ state.pointer=null; state.dragNode=null; canvas.classList.remove("dragging"); });
    canvas.addEventListener("contextmenu", e=>e.preventDefault());
    canvas.addEventListener("wheel", e=>{ e.preventDefault(); const p=pos(e); if(state.mode==="2d"){ const old=state.view.zoom, next=Math.max(.15,Math.min(5,old*(e.deltaY<0?1.12:.88))); state.view.x=p.x-(p.x-state.view.x)*(next/old); state.view.y=p.y-(p.y-state.view.y)*(next/old); state.view.zoom=next; } else { state.camera.distance=Math.max(180,Math.min(2200,state.camera.distance*(e.deltaY<0?.9:1.1))); } draw(); }, {passive:false});
    function selectNode(n){ state.selected={type:"node",item:n}; document.querySelectorAll(".item").forEach(i=>i.classList.toggle("selected",i.dataset.id==n.id)); renderDetail(state.selected); draw(); }
    function selectEdge(e){ state.selected={type:"edge",item:e}; renderDetail(state.selected); draw(); }
    function renderDetail(sel){ const kb=$("detailKind"); if(!sel){ kb.className="kind-badge"; kb.removeAttribute("style"); kb.textContent="DETAIL"; $("detailTitle").textContent="No selection"; $("detailMeta").textContent="Run graph search, then click a node or edge."; $("detailBody").innerHTML=""; return; } if(sel.type==="node"){ const n=sel.item, c=colors[n.kind]||"#748094"; kb.className="kind-badge"; kb.style.background=c+"1f"; kb.style.color=c; kb.innerHTML=`<span class="dot" style="background:${c}"></span>${escapeHtml(n.kind.toUpperCase())}`; $("detailTitle").textContent=n.label; $("detailMeta").textContent=n.path||`scan ${n.scanId}`; const rel=relations(n); $("detailBody").innerHTML=`<div class="kv"><div><span class="k">ID</span><span class="v">${n.id}</span></div><div><span class="k">Kind</span><span class="v">${escapeHtml(n.kind)}</span></div>${n.path?`<div><span class="k">Path</span><span class="v">${escapeHtml(n.path)}</span></div>`:""}${n.detail?`<div><span class="k">Detail</span><span class="v">${escapeHtml(n.detail)}</span></div>`:""}</div>${rel}`; } else { const e=sel.item, map=new Map(state.graph.nodes.map(n=>[n.id,n])), a=map.get(e.from), b=map.get(e.to); kb.className="kind-badge"; kb.removeAttribute("style"); kb.textContent="EDGE"; $("detailTitle").textContent=e.kind||e.label||"relationship"; $("detailMeta").textContent=`${a?.label||e.from} -> ${b?.label||e.to}`; $("detailBody").innerHTML=`<div class="kv"><div><span class="k">From</span><span class="v">${escapeHtml(a?.label||e.from)}</span></div><div><span class="k">To</span><span class="v">${escapeHtml(b?.label||e.to)}</span></div><div><span class="k">Kind</span><span class="v">${escapeHtml(e.kind||"")}</span></div>${e.label?`<div><span class="k">Label</span><span class="v">${escapeHtml(e.label)}</span></div>`:""}</div>`; } }
    function relations(n){ const map=new Map(state.graph.nodes.map(x=>[x.id,x])); const rows=state.graph.edges.filter(e=>e.from===n.id||e.to===n.id).slice(0,18).map(e=>{ const other=map.get(e.from===n.id?e.to:e.from); const c=other?(colors[other.kind]||"#748094"):"#748094"; return `<div class="rel-row" data-id="${other?.id||""}"><span class="rel-kind">${escapeHtml(e.kind)}</span><svg class="arrow" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg><span class="dot" style="background:${c}"></span><span class="tgt">${escapeHtml(other?.label||"")}</span></div>`; }).join(""); setTimeout(()=>document.querySelectorAll(".rel-row[data-id]").forEach(el=>el.onclick=()=>{ const nn=state.graph.nodes.find(x=>String(x.id)===el.dataset.id); if(nn) selectNode(nn); }),0); return `<div class="rel"><h3>Relationships</h3>${rows||'<div class="empty">No visible relationships.</div>'}</div>`; }
    function pos(e){ const r=canvas.getBoundingClientRect(); return {x:e.clientX-r.left,y:e.clientY-r.top}; }
    function distToSegment(px,py,x1,y1,x2,y2){ const dx=x2-x1, dy=y2-y1, l2=dx*dx+dy*dy||1; let t=((px-x1)*dx+(py-y1)*dy)/l2; t=Math.max(0,Math.min(1,t)); return Math.hypot(px-(x1+t*dx),py-(y1+t*dy)); }
    function trim(v,n){ v=String(v||""); return v.length>n?v.slice(0,n-1)+"...":v; }
    function escapeHtml(v){ return String(v??"").replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
    loadProjects().then(()=>$("graph").click()).catch(e=>$("stats").textContent=e.message);
  </script>
</body>
</html>
""";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GuiCommand.SearchApiResponse))]
[JsonSerializable(typeof(GuiCommand.ProjectsApiResponse))]
[JsonSerializable(typeof(GraphData))]
[JsonSerializable(typeof(GraphNode))]
[JsonSerializable(typeof(GraphEdge))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(ProjectInfo))]
internal partial class CodeScanJsonContext : JsonSerializerContext
{
}
