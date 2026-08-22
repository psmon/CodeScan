using CodeScan.Services;
using Microsoft.Data.Sqlite;

namespace CodeScan.Tests;

/// <summary>
/// Tests for the architecture-analysis store API — UpsertArchitecture /
/// GetArchitecture / GetAnalyzedProjects, the projects.analysis_state lifecycle
/// (none → analyzed → stale on rescan), and DeleteProject cascade. The CLI in
/// <see cref="CodeScan.Commands.ArchCommand"/> is a thin shell over these.
/// </summary>
public class ArchitectureStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempDir;

    public ArchitectureStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codescan_arch_{Guid.NewGuid():N}.db");
        _tempDir = Path.Combine(Path.GetTempPath(), $"codescan_arch_{Guid.NewGuid():N}");
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

    private (SqliteStore db, long projectId) Setup()
    {
        var db = new SqliteStore(_dbPath);
        var projectId = db.UpsertProject(_tempDir);
        db.InsertScan(projectId, []); // empty scan — creates a scan row
        return (db, projectId);
    }

    [Fact]
    public void Upsert_Then_Get_RoundTrips_AndMarksAnalyzed()
    {
        var (db, projectId) = Setup();
        using var _ = db;

        db.UpsertArchitecture(projectId, "graph TD\n A-->B", "## Layers\n- one", null, "mermaid", db.GetLatestScanId(projectId));

        var arch = db.GetArchitecture(projectId);
        Assert.NotNull(arch);
        Assert.Equal("mermaid", arch!.Format);
        Assert.Equal("graph TD\n A-->B", arch.Diagram);
        Assert.Contains("Layers", arch.Summary);
        Assert.Equal("analyzed", arch.State);

        // The project row now reports the analyzed state.
        var project = db.GetProject(projectId);
        Assert.Equal("analyzed", project!.AnalysisState);
    }

    [Fact]
    public void Upsert_Twice_Replaces_SingleRow()
    {
        var (db, projectId) = Setup();
        using var _ = db;

        db.UpsertArchitecture(projectId, "graph TD\n A-->B", "first", null);
        db.UpsertArchitecture(projectId, "graph TD\n C-->D", "second", null);

        var arch = db.GetArchitecture(projectId);
        Assert.NotNull(arch);
        Assert.Equal("graph TD\n C-->D", arch!.Diagram);
        Assert.Equal("second", arch.Summary);
    }

    [Fact]
    public void GetArchitecture_ReturnsNull_WhenNoneStored()
    {
        var (db, projectId) = Setup();
        using var _ = db;
        Assert.Null(db.GetArchitecture(projectId));
    }

    [Fact]
    public void GetAnalyzedProjects_ReturnsOnlyAnalyzed()
    {
        var db = new SqliteStore(_dbPath);
        using var _ = db;

        var analyzedDir = Path.Combine(_tempDir, "a");
        var plainDir = Path.Combine(_tempDir, "b");
        Directory.CreateDirectory(analyzedDir);
        Directory.CreateDirectory(plainDir);

        var analyzedId = db.UpsertProject(analyzedDir);
        var plainId = db.UpsertProject(plainDir);
        db.InsertScan(analyzedId, []);
        db.InsertScan(plainId, []);

        db.UpsertArchitecture(analyzedId, "graph TD\n A-->B", null, null);

        var analyzed = db.GetAnalyzedProjects();
        Assert.Contains(analyzed, p => p.Id == analyzedId);
        Assert.DoesNotContain(analyzed, p => p.Id == plainId);
    }

    [Fact]
    public void Rescan_DowngradesAnalyzed_ToStale()
    {
        var (db, projectId) = Setup();
        using var _ = db;

        db.UpsertArchitecture(projectId, "graph TD\n A-->B", null, null);
        Assert.Equal("analyzed", db.GetProject(projectId)!.AnalysisState);

        // A new scan means the code changed → analysis becomes stale.
        db.InsertScan(projectId, []);

        Assert.Equal("stale", db.GetProject(projectId)!.AnalysisState);
        // The stored diagram is retained, flagged stale via the project state.
        Assert.Equal("stale", db.GetArchitecture(projectId)!.State);
    }

    [Fact]
    public void DeleteProject_RemovesArchitectureRow()
    {
        var (db, projectId) = Setup();
        using var _ = db;

        db.UpsertArchitecture(projectId, "graph TD\n A-->B", null, null);
        Assert.NotNull(db.GetArchitecture(projectId));

        db.DeleteProject(projectId);
        Assert.Null(db.GetArchitecture(projectId));
    }
}
