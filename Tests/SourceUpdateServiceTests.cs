using CodeScan.Services;

namespace CodeScan.Tests;

public class SourceUpdateServiceTests
{
    // ========================
    // FindGitRoot tests
    // ========================

    [Fact]
    public void FindGitRoot_ReturnsNull_ForNonGitDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = SourceUpdateService.FindGitRoot(tempDir);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindGitRoot_ReturnsRoot_ForGitDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        var gitDir = Path.Combine(tempDir, ".git");
        Directory.CreateDirectory(gitDir);
        try
        {
            var result = SourceUpdateService.FindGitRoot(tempDir);
            Assert.NotNull(result);
            Assert.Equal(tempDir, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindGitRoot_FindsParentGitRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        var gitDir = Path.Combine(tempDir, ".git");
        var subDir = Path.Combine(tempDir, "src", "sub");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(subDir);
        try
        {
            var result = SourceUpdateService.FindGitRoot(subDir);
            Assert.NotNull(result);
            Assert.Equal(tempDir, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ========================
    // RunGitPull tests
    // ========================

    [Fact]
    public void RunGitPull_FailsGracefully_ForNonGitDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var (success, message) = SourceUpdateService.RunGitPull(tempDir);
            Assert.False(success);
            Assert.False(string.IsNullOrEmpty(message));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ========================
    // Execute tests
    // ========================

    [Fact]
    public void Execute_ReturnsError_WhenProjectNotFound()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteStore(dbPath);
            var svc = new SourceUpdateService(db);
            var result = svc.Execute(9999);

            Assert.False(result.Success);
            Assert.Contains("not found", result.ErrorMessage);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_ReturnsError_WhenDirectoryNotFound()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject("/nonexistent/path/xyz");
            var svc = new SourceUpdateService(db);
            var result = svc.Execute(projectId, "/nonexistent/path/xyz");

            Assert.False(result.Success);
            Assert.Contains("not found", result.ErrorMessage);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_SucceedsWithWarning_ForNonGitDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        // Create a dummy source file
        File.WriteAllText(Path.Combine(tempDir, "test.cs"), "public class Foo { public void Bar() {} }");

        var dbPath = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}.db");

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var logs = new List<string>();
            var svc = new SourceUpdateService(db, msg => logs.Add(msg));
            var result = svc.Execute(projectId, tempDir);

            Assert.True(result.Success);
            Assert.False(result.GitPullAttempted);
            Assert.NotNull(result.GitPullWarning);
            Assert.Contains("Not a git repository", result.GitPullWarning);
            Assert.True(result.FileCount >= 1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_LogsProgress()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "hello.cs"), "class A { void M() {} }");

        var dbPath = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}.db");

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var logs = new List<string>();
            var svc = new SourceUpdateService(db, msg => logs.Add(msg));
            svc.Execute(projectId, tempDir);

            Assert.Contains(logs, l => l.Contains("[scan]"));
            Assert.Contains(logs, l => l.Contains("[db]"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
            TryDeleteFile(dbPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
