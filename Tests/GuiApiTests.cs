using CodeScan.Commands;
using CodeScan.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace CodeScan.Tests;

public sealed class GuiApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"codescan_gui_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codescan_gui_{Guid.NewGuid():N}");

    public GuiApiTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Projects_AnalyzedFilter_ReturnsOnlyAnalyzedProject()
    {
        using var db = new SqliteStore(_dbPath);
        var analyzed = db.UpsertProject(Path.Combine(_root, "analyzed"));
        var plain = db.UpsertProject(Path.Combine(_root, "plain"));
        db.InsertScan(analyzed, []);
        db.InsertScan(plain, []);
        db.UpsertArchitecture(analyzed, "graph TD\nA-->B", "summary", null);

        var response = Request("/api/projects", ("analyzed", "1"));

        Assert.Equal(200, response.StatusCode);
        Assert.Contains($"\"id\":{analyzed}", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"\"id\":{plain}", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Architecture_RequiresProjectId()
    {
        var response = Request("/api/architecture");

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("project is required", response.Body);
    }

    [Fact]
    public void Architecture_ReturnsNotFoundWhenMissing()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_root);
        db.InsertScan(projectId, []);

        var response = Request("/api/architecture", ("project", projectId.ToString()));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public void Architecture_RoundTripsUnicodeAndMermaid()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_root);
        db.InsertScan(projectId, []);
        db.UpsertArchitecture(projectId, "graph TD\nA[검색]-->B", "요약", null);

        var response = Request("/api/architecture", ("project", projectId.ToString()));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Contains("검색", json.RootElement.GetProperty("diagram").GetString());
        Assert.Equal("요약", json.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public void Query_ReturnsBadRequestForInvalidSyntax()
    {
        var response = Request("/api/query", ("q", "not cypher"));

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public void MermaidAsset_IsEmbeddedAndOfflineSafe()
    {
        var response = Request("/assets/mermaid.js");

        Assert.Equal(200, response.StatusCode);
        Assert.StartsWith("application/javascript", response.ContentType);
        Assert.True(response.Body.Length > 100_000);
    }

    [Fact]
    public void File_RejectsTraversalOutsideProjectRoot()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_root);
        db.InsertScan(projectId, []);

        var response = Request("/api/file", ("project", projectId.ToString()), ("path", "..\\outside.txt"));

        Assert.Equal(403, response.StatusCode);
    }

    private GuiCommand.ResponsePayload Request(string path, params (string Key, string Value)[] query) =>
        GuiCommand.HandleRequest(path, query.ToDictionary(x => x.Key, x => x.Value), _dbPath);
}
