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

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            TryDelete(pidPath);
            listener.Stop();
        };

        Console.WriteLine($"CodeScan GUI listening at http://127.0.0.1:{port}/");
        Console.WriteLine("Press Ctrl+C to stop, or run: codescan gui stop");

        try
        {
            while (true)
            {
                var client = listener.AcceptTcpClient();
                _ = Task.Run(() => HandleClient(client));
            }
        }
        catch (SocketException)
        {
            return 0;
        }
        finally
        {
            TryDelete(pidPath);
        }
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
  <title>CodeScan GUI</title>
  <style>
    :root { color-scheme: light; --line:#d7dde5; --ink:#17202a; --muted:#647284; --accent:#0b7285; --panel:#f6f8fb; --surface:#ffffff; }
    * { box-sizing: border-box; }
    body { margin:0; font:14px/1.45 system-ui,-apple-system,Segoe UI,sans-serif; color:var(--ink); background:#eef2f6; }
    header { height:52px; display:flex; align-items:center; gap:16px; padding:0 18px; background:#ffffff; border-bottom:1px solid var(--line); }
    h1 { font-size:18px; margin:0; font-weight:700; }
    main { display:grid; grid-template-columns:360px 1fr; height:calc(100vh - 52px); min-height:520px; }
    aside { border-right:1px solid var(--line); background:var(--surface); padding:14px; overflow:auto; }
    section { min-width:0; display:grid; grid-template-rows:1fr 160px; }
    label { display:block; font-size:12px; color:var(--muted); margin:12px 0 5px; }
    input, select { width:100%; height:34px; border:1px solid var(--line); border-radius:6px; padding:0 9px; background:white; color:var(--ink); }
    .row { display:grid; grid-template-columns:1fr 84px; gap:8px; align-items:end; }
    .actions { display:flex; gap:8px; margin:14px 0; }
    button { height:34px; border:1px solid #aab6c4; background:#ffffff; border-radius:6px; padding:0 12px; color:var(--ink); cursor:pointer; }
    button.primary { background:var(--accent); color:white; border-color:var(--accent); }
    canvas { width:100%; height:100%; display:block; background:#fbfcfe; }
    #results { border-top:1px solid var(--line); background:var(--surface); overflow:auto; padding:10px 12px; font-family:Consolas,monospace; font-size:12px; }
    .item { border-bottom:1px solid var(--line); padding:9px 0; }
    .tag { display:inline-block; min-width:72px; color:var(--accent); font-weight:700; }
    .meta { color:var(--muted); margin-top:2px; overflow-wrap:anywhere; }
    .stats { margin-left:auto; color:var(--muted); }
    @media (max-width: 760px) { main { grid-template-columns:1fr; grid-template-rows:auto 1fr; height:auto; min-height:calc(100vh - 52px); } aside { border-right:0; border-bottom:1px solid var(--line); } section { height:70vh; } }
  </style>
</head>
<body>
  <header><h1>CodeScan</h1><span class="stats" id="stats">Ready</span></header>
  <main>
    <aside>
      <label for="project">Project</label>
      <select id="project"><option value="">All latest projects</option></select>
      <label for="query">Search</label>
      <div class="row"><input id="query" placeholder="keyword, method, file, author" /><button id="clear">Clear</button></div>
      <label for="type">Keyword type</label>
      <select id="type">
        <option value="">All</option><option value="method">Method</option><option value="file">File</option><option value="doc">Doc</option><option value="comment">Comment</option><option value="commit">Commit</option>
      </select>
      <label for="view">Graph view</label>
      <select id="view"><option value="2d">2D graph</option><option value="3d">3D graph</option></select>
      <label for="depth">Graph depth</label>
      <select id="depth"><option>0</option><option selected>1</option><option>2</option><option>3</option><option>4</option></select>
      <div class="actions"><button class="primary" id="keyword">Keyword Search</button><button id="graph">Graph Search</button></div>
      <div id="list"></div>
    </aside>
    <section>
      <canvas id="canvas"></canvas>
      <div id="results"></div>
    </section>
  </main>
  <script>
    const $ = id => document.getElementById(id);
    const canvas = $("canvas"), ctx = canvas.getContext("2d");
    let graph = {nodes:[], edges:[]}, tick = 0, anim = 0;
    const color = {project:"#0b7285", directory:"#5c7cfa", file:"#2f9e44", class:"#f08c00", method:"#c2255c", comment:"#7048e8", doc:"#087f5b", author:"#495057"};
    function fit(){ const r=canvas.getBoundingClientRect(); canvas.width=Math.max(320,r.width*devicePixelRatio); canvas.height=Math.max(260,r.height*devicePixelRatio); }
    addEventListener("resize", () => { fit(); draw(); });
    fit();
    async function api(path){ const r=await fetch(path); if(!r.ok) throw new Error(await r.text()); return await r.json(); }
    async function loadProjects(){ const data=await api("/api/projects"); for(const p of data.projects){ const o=document.createElement("option"); o.value=p.id; o.textContent=`#${p.id} ${p.rootPath}`; $("project").appendChild(o); } }
    function params(extra=""){ const q=encodeURIComponent($("query").value.trim()); const p=$("project").value; return `q=${q}${p?`&project=${p}`:""}${extra}`; }
    $("keyword").onclick = async () => { const t=$("type").value; const data=await api(`/api/search?limit=80${t?`&type=${t}`:""}&${params()}`); renderResults(data.results); };
    $("graph").onclick = async () => { const data=await api(`/api/graph?depth=${$("depth").value}&limit=140&${params()}`); setGraph(data); };
    $("clear").onclick = () => { $("query").value=""; $("results").textContent=""; setGraph({nodes:[], edges:[]}); };
    $("view").onchange = draw;
    $("query").addEventListener("keydown", e => { if(e.key==="Enter") $("keyword").click(); });
    function renderResults(rows){ $("stats").textContent=`${rows.length} keyword results`; $("list").innerHTML=""; $("results").textContent=""; for(const r of rows){ const d=document.createElement("div"); d.className="item"; d.innerHTML=`<span class="tag">${escapeHtml(r.type)}</span>${escapeHtml(r.name)}<div class="meta">${escapeHtml(r.path||"")}</div><div class="meta">${escapeHtml(r.excerpt||"")}</div>`; $("list").appendChild(d); } }
    function setGraph(data){ graph=data; layout(); $("stats").textContent=`${graph.nodes.length} nodes, ${graph.edges.length} edges`; $("list").innerHTML=""; for(const n of graph.nodes.slice(0,80)){ const d=document.createElement("div"); d.className="item"; d.innerHTML=`<span class="tag">${escapeHtml(n.kind)}</span>${escapeHtml(n.label)}<div class="meta">${escapeHtml(n.path||n.detail||"")}</div>`; $("list").appendChild(d); } cancelAnimationFrame(anim); loop(); }
    function layout(){ const w=canvas.width,h=canvas.height,cx=w/2,cy=h/2; graph.nodes.forEach((n,i)=>{ const a=i*2.399, r=Math.min(w,h)*(0.12+0.36*Math.sqrt((i+1)/Math.max(1,graph.nodes.length))); n.x=cx+Math.cos(a)*r; n.y=cy+Math.sin(a)*r; n.z=Math.sin(a*1.7)*180; }); }
    function loop(){ tick+=0.01; draw(); if($("view").value==="3d") anim=requestAnimationFrame(loop); }
    function draw(){ fit(); const w=canvas.width,h=canvas.height; ctx.clearRect(0,0,w,h); const map=new Map(graph.nodes.map(n=>[n.id,n])); ctx.lineWidth=1*devicePixelRatio; ctx.strokeStyle="#bac4d0"; for(const e of graph.edges){ const a=map.get(e.from), b=map.get(e.to); if(!a||!b) continue; const p=project(a,w,h), q=project(b,w,h); ctx.beginPath(); ctx.moveTo(p.x,p.y); ctx.lineTo(q.x,q.y); ctx.stroke(); } for(const n of graph.nodes){ const p=project(n,w,h); const radius=(n.kind==="project"?8:5)*devicePixelRatio*p.s; ctx.fillStyle=color[n.kind]||"#343a40"; ctx.beginPath(); ctx.arc(p.x,p.y,Math.max(3,radius),0,Math.PI*2); ctx.fill(); ctx.fillStyle="#17202a"; ctx.font=`${12*devicePixelRatio}px system-ui`; ctx.fillText(n.label.slice(0,38),p.x+8*devicePixelRatio,p.y-6*devicePixelRatio); } }
    function project(n,w,h){ if($("view").value==="2d") return {x:n.x,y:n.y,s:1}; const a=tick, x=(n.x-w/2)*Math.cos(a)-n.z*Math.sin(a), z=(n.x-w/2)*Math.sin(a)+n.z*Math.cos(a); const s=450/(450+z); return {x:w/2+x*s,y:h/2+(n.y-h/2)*s,s}; }
    function escapeHtml(v){ return String(v).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
    loadProjects().then(() => $("graph").click()).catch(e => $("stats").textContent=e.message);
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
