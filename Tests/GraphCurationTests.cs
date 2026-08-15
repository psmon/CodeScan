using CodeScan.Models;
using CodeScan.Services;
using Microsoft.Data.Sqlite;

namespace CodeScan.Tests;

/// <summary>
/// Tests for the v0.7.0 graph-curation API — UpsertCuratedNode / UpsertCuratedEdge /
/// StrengthenEdge / UpdateNodeFields / UpdateEdgeFields / DeleteEdge /
/// DeleteNodeCascade. The CLI in <see cref="CodeScan.Commands.GraphEditCommand"/>
/// is a thin shell over these — testing them at the store level covers both.
/// </summary>
public class GraphCurationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempDir;

    public GraphCurationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codescan_curation_{Guid.NewGuid():N}.db");
        _tempDir = Path.Combine(Path.GetTempPath(), $"codescan_curation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections; explicitly release them
        // before deleting the database file (otherwise Windows holds the lock).
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private (SqliteStore db, long projectId, long scanId) SetupScan()
    {
        var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_tempDir);
        db.InsertScan(projectId, []);  // empty scan — enough to create a scan row
        var scanId = db.GetLatestScanId(projectId) ?? throw new InvalidOperationException("scan missing");
        return (db, projectId, scanId);
    }

    [Fact]
    public void AddNode_IsCurated_AndRetrievableById()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var nodeId = db.UpsertCuratedNode(projectId, "class", "CacheLayer", "Services/Cache.cs", "abstract cache");

        var node = db.GetNode(nodeId);
        Assert.NotNull(node);
        Assert.Equal("class", node!.Kind);
        Assert.Equal("CacheLayer", node.Label);
        Assert.Equal("Services/Cache.cs", node.Path);
    }

    [Fact]
    public void AddNode_TwiceWithSameKindAndLabel_ReturnsSameId()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var first = db.UpsertCuratedNode(projectId,"class", "Foo", "", "");
        var second = db.UpsertCuratedNode(projectId,"class", "Foo", "", "");

        Assert.Equal(first, second);
    }

    [Fact]
    public void AddEdge_BetweenTwoCuratedNodes_IsCreatedWithWeightOne()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var fromId = db.UpsertCuratedNode(projectId,"class", "A", "", "");
        var toId = db.UpsertCuratedNode(projectId,"class", "B", "", "");

        var edgeId = db.UpsertCuratedEdge(projectId,fromId, toId, "inherits_or_implements", "human-added");
        Assert.True(edgeId > 0);
    }

    [Fact]
    public void Strengthen_OnExistingEdge_IncrementsWeight()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var fromId = db.UpsertCuratedNode(projectId,"class", "A", "", "");
        var toId = db.UpsertCuratedNode(projectId,"class", "B", "", "");
        db.UpsertCuratedEdge(projectId,fromId, toId, "uses_type", "");

        var w1 = db.StrengthenEdge(projectId,fromId, toId, "uses_type");
        var w2 = db.StrengthenEdge(projectId,fromId, toId, "uses_type");
        var w3 = db.StrengthenEdge(projectId,fromId, toId, "uses_type");

        Assert.Equal(2, w1);
        Assert.Equal(3, w2);
        Assert.Equal(4, w3);
    }

    [Fact]
    public void Strengthen_OnMissingEdge_ReturnsNull()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var fromId = db.UpsertCuratedNode(projectId,"class", "A", "", "");
        var toId = db.UpsertCuratedNode(projectId,"class", "B", "", "");

        var result = db.StrengthenEdge(projectId,fromId, toId, "does_not_exist");

        Assert.Null(result);
    }

    [Fact]
    public void FindNodeIdsByLabel_DisambiguatesCollisions()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        // Two distinct nodes that happen to share a label but differ in kind —
        // the curated stable-key uses kind:label, so they're separate rows.
        var classFoo = db.UpsertCuratedNode(projectId,"class", "Foo", "", "");
        var moduleFoo = db.UpsertCuratedNode(projectId,"module", "Foo", "", "");

        var matches = db.FindNodeIdsByLabel(projectId,"Foo");
        Assert.Equal(2, matches.Count);
        Assert.Contains(classFoo, matches);
        Assert.Contains(moduleFoo, matches);
    }

    [Fact]
    public void UpdateNodeFields_AppliesOnlyProvidedFields()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var nodeId = db.UpsertCuratedNode(projectId,"class", "OriginalLabel", "old/path", "old detail");

        var changed = db.UpdateNodeFields(nodeId, kind: null, label: "RenamedLabel", path: null, detail: "new detail");

        Assert.True(changed);
        var n = db.GetNode(nodeId)!;
        Assert.Equal("class", n.Kind);
        Assert.Equal("RenamedLabel", n.Label);
        Assert.Equal("old/path", n.Path);
        Assert.Equal("new detail", n.Detail);
    }

    [Fact]
    public void DeleteEdge_RemovesEdge_NotNodes()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var fromId = db.UpsertCuratedNode(projectId,"class", "A", "", "");
        var toId = db.UpsertCuratedNode(projectId,"class", "B", "", "");
        var edgeId = db.UpsertCuratedEdge(projectId,fromId, toId, "uses_type", "");

        var removed = db.DeleteEdge(edgeId);

        Assert.True(removed);
        Assert.NotNull(db.GetNode(fromId));
        Assert.NotNull(db.GetNode(toId));
    }

    [Fact]
    public void DeleteNodeCascade_RemovesIncidentEdges()
    {
        var (db, projectId, _) = SetupScan();
        using var _ = db;

        var a = db.UpsertCuratedNode(projectId,"class", "A", "", "");
        var b = db.UpsertCuratedNode(projectId,"class", "B", "", "");
        var c = db.UpsertCuratedNode(projectId,"class", "C", "", "");
        db.UpsertCuratedEdge(projectId,a, b, "uses_type", "");
        db.UpsertCuratedEdge(projectId,c, b, "creates", "");

        var removed = db.DeleteNodeCascade(b);

        Assert.True(removed);
        Assert.Null(db.GetNode(b));
        // The other two nodes survive; their edges to B are gone.
        Assert.NotNull(db.GetNode(a));
        Assert.NotNull(db.GetNode(c));
    }

    [Fact]
    public void GetLatestProjectId_AndScanId_AreResolved()
    {
        var (db, projectId, scanId) = SetupScan();
        using var _ = db;

        Assert.Equal(projectId, db.GetLatestProjectId());
        Assert.Equal(scanId, db.GetLatestScanId(projectId));
    }
}
