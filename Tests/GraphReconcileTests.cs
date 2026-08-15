using CodeScan.Models;
using CodeScan.Services;
using Microsoft.Data.Sqlite;

namespace CodeScan.Tests;

/// <summary>
/// Phase 2 reconciliation behaviour: edge weight is corroborating-scan evidence
/// (reinforced once per scan), unobserved auto edges decay with grace, and a
/// depleted edge retires. See harness/knowledge/graph-reconciliation.md.
/// </summary>
public class GraphReconcileTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempDir;

    public GraphReconcileTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codescan_reconcile_{Guid.NewGuid():N}.db");
        _tempDir = Path.Combine(Path.GetTempPath(), $"codescan_reconcile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // One file whose class uses HttpClient — yields a class -[uses_type]-> type edge.
    private static List<FileEntry> SampleEntries() =>
    [
        new FileEntry
        {
            FullPath = "Client.cs",
            RelativePath = "Client.cs",
            Name = "Client.cs",
            Size = 1,
            IsDirectory = false,
            Depth = 0,
            Dependencies =
            [
                new SourceDependency
                {
                    FromKind = "class", FromName = "Client",
                    EdgeKind = "uses_type", ToKind = "type", ToName = "HttpClient",
                    Strategy = "test", Detail = "type reference", Line = 1
                }
            ]
        }
    ];

    private static int? UsesTypeWeight(SqliteStore db)
    {
        var graph = db.QueryGraph("MATCH (c:class)-[r:uses_type]->(t:type) WHERE t.label = 'HttpClient' LIMIT 10");
        var edge = graph.Edges.FirstOrDefault(e => e.Kind == "uses_type");
        return edge?.Weight;
    }

    [Fact]
    public void Reinforce_IsOncePerScan_NotPerObservation()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_tempDir);

        db.InsertScan(projectId, SampleEntries());
        Assert.Equal(1, UsesTypeWeight(db));

        db.InsertScan(projectId, SampleEntries());
        db.InsertScan(projectId, SampleEntries());
        // Three scans that each observe the edge → weight 3 (not more).
        Assert.Equal(3, UsesTypeWeight(db));
    }

    [Fact]
    public void UnobservedEdge_DecaysWithGrace_ThenRetires()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_tempDir);

        // Build up 3 units of evidence.
        db.InsertScan(projectId, SampleEntries());
        db.InsertScan(projectId, SampleEntries());
        db.InsertScan(projectId, SampleEntries());
        Assert.Equal(3, UsesTypeWeight(db));

        // First miss: decays but survives (resilience against transient noise).
        db.InsertScan(projectId, []);
        Assert.Equal(2, UsesTypeWeight(db));

        // Two more misses deplete the weight budget → edge retires (leaves active view).
        db.InsertScan(projectId, []);
        db.InsertScan(projectId, []);
        Assert.Null(UsesTypeWeight(db));
    }

    [Fact]
    public void FullRebuild_RetiresImmediately_NoGrace()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_tempDir);

        db.InsertScan(projectId, SampleEntries());
        db.InsertScan(projectId, SampleEntries());
        Assert.Equal(2, UsesTypeWeight(db));

        // Full rebuild with the edge absent drops it at once — no decay grace.
        db.InsertScan(projectId, [], fullRebuild: true);
        Assert.Null(UsesTypeWeight(db));
    }

    [Fact]
    public void ReobservingRetiredEdge_ReactivatesIt()
    {
        using var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_tempDir);

        db.InsertScan(projectId, SampleEntries());
        db.InsertScan(projectId, [], fullRebuild: true);
        Assert.Null(UsesTypeWeight(db));

        // Source brings the relationship back → edge is live again.
        db.InsertScan(projectId, SampleEntries());
        Assert.Equal(1, UsesTypeWeight(db));
    }
}
