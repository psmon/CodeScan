using System.Diagnostics;
using CodeScan.Models;

namespace CodeScan.Services;

/// <summary>
/// Core service for source update: git pull + full rescan.
/// Used by both CLI (ProjectUpdateCommand) and TUI.
/// </summary>
public sealed class SourceUpdateService
{
    private readonly SqliteStore _db;
    private readonly Action<string>? _log;

    public SourceUpdateService(SqliteStore db, Action<string>? log = null)
    {
        _db = db;
        _log = log;
    }

    public sealed class SourceUpdateResult
    {
        public bool Success { get; init; }
        public bool GitPullAttempted { get; init; }
        public bool GitPullSucceeded { get; init; }
        public string? GitPullWarning { get; init; }
        public int FileCount { get; init; }
        public int MethodCount { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public SourceUpdateResult Execute(long projectId)
    {
        var project = _db.GetProject(projectId);
        if (project == null)
            return new SourceUpdateResult { Success = false, ErrorMessage = $"Project #{projectId} not found." };

        return Execute(projectId, project.RootPath);
    }

    public SourceUpdateResult Execute(long projectId, string projectPath)
    {
        if (!Directory.Exists(projectPath))
            return new SourceUpdateResult { Success = false, ErrorMessage = $"Directory not found: {projectPath}" };

        // Step 1: Try git pull (optional)
        bool gitAttempted = false;
        bool gitSucceeded = false;
        string? gitWarning = null;

        var gitRoot = FindGitRoot(projectPath);
        if (gitRoot != null)
        {
            gitAttempted = true;
            _log?.Invoke($"[git] Pulling latest from {gitRoot} ...");

            var (success, message) = RunGitPull(gitRoot);
            gitSucceeded = success;

            if (!success)
            {
                gitWarning = $"Warning: git pull failed - {message}. Proceeding with current source.";
                _log?.Invoke(gitWarning);
                _log?.Invoke("[git] Please update to the latest source manually.");
            }
            else
            {
                _log?.Invoke($"[git] Pull succeeded: {message}");
            }
        }
        else
        {
            gitWarning = "Warning: Not a git repository. Scanning current source as-is.";
            _log?.Invoke(gitWarning);
        }

        // Step 2: Full scan
        _log?.Invoke("[scan] Starting full scan...");

        var scanner = new DirectoryScanner(
            includeExts: null,
            excludeDirs: null,
            respectGitignore: true);

        var entries = scanner.Scan(projectPath);

        // Analyze source files
        var gitBlame = new GitBlameService(projectPath);
        var sourceFiles = entries.Where(e =>
            !e.IsDirectory && SourceAnalyzer.IsSourceFile(e.Extension)).ToList();

        var total = sourceFiles.Count;
        for (int i = 0; i < sourceFiles.Count; i++)
        {
            var file = sourceFiles[i];
            file.Methods = SourceAnalyzer.ExtractMethods(file.FullPath);
            file.Comments = CommentExtractor.Extract(file.FullPath);

            if (gitBlame.IsAvailable && file.Methods.Count > 0)
                gitBlame.EnrichWithBlame(file.FullPath, file.Methods);

            if ((i + 1) % 10 == 0 || i == sourceFiles.Count - 1)
                _log?.Invoke($"  [{i + 1}/{total}] Analyzed {file.Name}");
        }

        // Step 3: Save to DB
        _log?.Invoke("[db] Saving scan results...");

        var fullPath = Path.GetFullPath(projectPath);
        var existingProjectId = _db.UpsertProject(fullPath);
        // Use the actual project ID (might differ if path changed)
        var targetProjectId = existingProjectId > 0 ? existingProjectId : projectId;
        var scanId = _db.InsertScan(targetProjectId, entries);

        // Index project docs
        var docPath = ProjectDocFinder.FindDoc(projectPath);
        if (docPath != null)
        {
            try
            {
                var docContent = File.ReadAllText(docPath);
                _db.InsertProjectDoc(scanId, Path.GetRelativePath(projectPath, docPath), docContent);
            }
            catch { }
        }

        var fileCount = entries.Count(e => !e.IsDirectory);
        var methodCount = entries.SelectMany(e => e.Methods).Count();

        _log?.Invoke($"[db] Indexed: {fileCount} files, {methodCount} methods");

        return new SourceUpdateResult
        {
            Success = true,
            GitPullAttempted = gitAttempted,
            GitPullSucceeded = gitSucceeded,
            GitPullWarning = gitWarning,
            FileCount = fileCount,
            MethodCount = methodCount
        };
    }

    internal static string? FindGitRoot(string path)
    {
        var dir = path;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    internal static (bool Success, string Message) RunGitPull(string gitRoot)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "pull",
                WorkingDirectory = gitRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Failed to start git process");

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(30000))
            {
                try { proc.Kill(); } catch { }
                return (false, "git pull timed out (30s)");
            }

            if (proc.ExitCode != 0)
            {
                var errorMsg = stderr.Trim();
                if (string.IsNullOrEmpty(errorMsg)) errorMsg = stdout.Trim();
                return (false, errorMsg);
            }

            var output = stdout.Trim();
            if (string.IsNullOrEmpty(output)) output = "Up to date";
            return (true, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
