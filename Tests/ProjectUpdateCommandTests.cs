using CodeScan.Commands;
using CodeScan.Services;

namespace CodeScan.Tests;

public class ProjectUpdateCommandTests
{
    private static string CreateTempDb() =>
        Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}.db");

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public void Execute_ReturnsError_WhenProjectNotFound()
    {
        var dbPath = CreateTempDb();
        try
        {
            using var db = new SqliteStore(dbPath);
            var cmd = new ProjectUpdateCommand(db);

            var sw = new StringWriter();
            Console.SetError(sw);
            var result = cmd.Execute(9999, null, null);
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

            Assert.Equal(1, result);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_UpdatesPath()
    {
        var dbPath = CreateTempDb();
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        var newDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(newDir);

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var sw = new StringWriter();
            Console.SetOut(sw);
            var cmd = new ProjectUpdateCommand(db);
            var result = cmd.Execute(projectId, newDir, null);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

            Assert.Equal(0, result);
            var project = db.GetProject(projectId);
            Assert.Equal(Path.GetFullPath(newDir), project!.RootPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            Directory.Delete(newDir, true);
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_UpdatesAddInfo()
    {
        var dbPath = CreateTempDb();
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var sw = new StringWriter();
            Console.SetOut(sw);
            var cmd = new ProjectUpdateCommand(db);
            var result = cmd.Execute(projectId, null, "Test description");
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

            Assert.Equal(0, result);
            var project = db.GetProject(projectId);
            Assert.Equal("Test description", project!.AddInfo);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_ReturnsError_WhenNothingSpecified()
    {
        var dbPath = CreateTempDb();
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var sw = new StringWriter();
            Console.SetError(sw);
            var cmd = new ProjectUpdateCommand(db);
            var result = cmd.Execute(projectId, null, null, false);
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

            Assert.Equal(1, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_SourceUpdate_FullScan()
    {
        var dbPath = CreateTempDb();
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Program.cs"),
            "namespace Test;\npublic class MyClass\n{\n    public void Hello() { }\n    public int Add(int a) { return a; }\n}\n");

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var sw = new StringWriter();
            Console.SetOut(sw);
            var cmd = new ProjectUpdateCommand(db);
            var result = cmd.Execute(projectId, null, null, sourceUpdate: true);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

            Assert.Equal(0, result);

            // Verify scan results were saved
            var project = db.GetProject(projectId);
            Assert.NotNull(project);
            Assert.True(project.FileCount >= 1);

            var output = sw.ToString();
            Assert.Contains("Source update complete", output);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void Execute_SourceUpdate_WithPathAndAddInfo()
    {
        var dbPath = CreateTempDb();
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.cs"), "class A {}");

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));

            var sw = new StringWriter();
            Console.SetOut(sw);
            var cmd = new ProjectUpdateCommand(db);
            // Can combine --source with --addinfo
            var result = cmd.Execute(projectId, null, "Updated desc", sourceUpdate: true);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

            Assert.Equal(0, result);
            var project = db.GetProject(projectId);
            Assert.Equal("Updated desc", project!.AddInfo);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            TryDeleteFile(dbPath);
        }
    }
}
