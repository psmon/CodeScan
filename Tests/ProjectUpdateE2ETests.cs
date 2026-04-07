using CodeScan.Commands;
using CodeScan.Services;

namespace CodeScan.Tests;

/// <summary>
/// E2E tests for project-update --source using the actual CodeScan project.
/// </summary>
public class ProjectUpdateE2ETests
{
    // The CodeScan project root (this repo itself)
    private static readonly string ProjectRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "CodeScan.csproj")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string CreateTempDb() =>
        Path.Combine(Path.GetTempPath(), $"codescan_e2e_{Guid.NewGuid():N}.db");

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ========================
    // CLI E2E: register -> update -> source update -> verify
    // ========================

    [Fact]
    public void E2E_RegisterProject_ThenSourceUpdate()
    {
        var dbPath = CreateTempDb();
        try
        {
            using var db = new SqliteStore(dbPath);

            // Step 1: Register the project (like 'codescan scan')
            var listCmd = new ListCommand(db: db);
            var listResult = listCmd.Execute(ProjectRoot, new ListOptions
            {
                Tree = true,
                Detail = true,
                Stats = true,
                Verbose = false
            });
            Assert.Equal(0, listResult);

            // Step 2: Verify project was registered
            var projects = db.GetProjects();
            Assert.True(projects.Count >= 1);
            var project = projects.FirstOrDefault(p => p.RootPath == Path.GetFullPath(ProjectRoot));
            Assert.NotNull(project);
            var projectId = project.Id;
            Assert.True(project.FileCount > 0);

            // Step 3: Run source update
            var sw = new StringWriter();
            Console.SetOut(sw);
            var updateCmd = new ProjectUpdateCommand(db);
            var updateResult = updateCmd.Execute(projectId, null, null, sourceUpdate: true);
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

            Assert.Equal(0, updateResult);
            var output = sw.ToString();
            Assert.Contains("Source update complete", output);
            Assert.Contains("methods indexed", output);

            // Step 4: Verify DB was updated with new scan
            var updatedProject = db.GetProject(projectId);
            Assert.NotNull(updatedProject);
            Assert.True(updatedProject.FileCount > 0);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void E2E_SourceUpdateService_WithGitPull()
    {
        var dbPath = CreateTempDb();
        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(ProjectRoot));

            var logs = new List<string>();
            var svc = new SourceUpdateService(db, msg => logs.Add(msg));
            var result = svc.Execute(projectId, ProjectRoot);

            Assert.True(result.Success);
            Assert.True(result.GitPullAttempted); // CodeScan is a git repo
            Assert.True(result.FileCount > 10);   // Has many files
            Assert.True(result.MethodCount > 50);  // Has many methods

            // Verify git pull was attempted
            Assert.Contains(logs, l => l.Contains("[git]"));
            Assert.Contains(logs, l => l.Contains("[scan]"));
            Assert.Contains(logs, l => l.Contains("[db]"));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void E2E_ProjectDelete_AfterSourceUpdate()
    {
        var dbPath = CreateTempDb();
        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(ProjectRoot));

            // Source update
            var svc = new SourceUpdateService(db);
            var result = svc.Execute(projectId, ProjectRoot);
            Assert.True(result.Success);

            // Verify project exists
            var project = db.GetProject(projectId);
            Assert.NotNull(project);

            // Delete project
            var deleteCmd = new ProjectDeleteCommand(db);
            deleteCmd.Execute(projectId, force: true);

            // Verify deleted
            var deleted = db.GetProject(projectId);
            Assert.Null(deleted);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public void E2E_SourceUpdate_PreservesAddInfo()
    {
        var dbPath = CreateTempDb();
        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(ProjectRoot));

            // Set addinfo first
            db.SetProjectAddInfo(projectId, "Test project description");

            // Run source update
            var svc = new SourceUpdateService(db);
            var result = svc.Execute(projectId, ProjectRoot);
            Assert.True(result.Success);

            // Verify addinfo is preserved
            var project = db.GetProject(projectId);
            Assert.NotNull(project);
            Assert.Equal("Test project description", project.AddInfo);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }
}
