using System.Diagnostics;

namespace CodeScan.Services;

public static class GitLogSearchService
{
    public static List<SearchResult> Search(string query, int limit = 20)
    {
        var results = new List<SearchResult>();

        // Search all indexed projects
        var dbPath = AppPaths.DbPath;
        if (!File.Exists(dbPath)) return results;

        using var db = new SqliteStore(dbPath);
        var projects = db.GetProjects();

        foreach (var project in projects)
        {
            if (!Directory.Exists(project.RootPath)) continue;

            // Check if git repo
            var gitDir = FindGitDir(project.RootPath);
            if (gitDir == null) continue;

            var gitResults = RunGitLogSearch(gitDir, query, limit);
            foreach (var r in gitResults)
            {
                results.Add(r);
                if (results.Count >= limit) return results;
            }
        }

        return results;
    }

    private static List<SearchResult> RunGitLogSearch(string repoRoot, string query, int limit)
    {
        var results = new List<SearchResult>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"log --all --oneline --grep=\"{EscapeArg(query)}\" -i -n {limit}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return results;

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { }
                return results;
            }

            if (proc.ExitCode != 0) return results;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 8) continue;

                var spaceIdx = trimmed.IndexOf(' ');
                if (spaceIdx < 0) continue;

                var hash = trimmed[..spaceIdx];
                var message = trimmed[(spaceIdx + 1)..];

                // Get changed files for this commit
                var files = GetCommitFiles(repoRoot, hash);

                results.Add(new SearchResult
                {
                    Type = "commit",
                    Name = $"{hash} {message}",
                    Excerpt = files,
                    Path = repoRoot,
                    ScanId = 0
                });
            }
        }
        catch { }

        return results;
    }

    private static string GetCommitFiles(string repoRoot, string hash)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"diff-tree --no-commit-id --name-only -r {hash}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "";

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(3000))
            {
                try { proc.Kill(); } catch { }
                return "";
            }

            var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Take(5)
                .ToArray();

            var result = string.Join(", ", files);
            if (output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 5)
                result += " ...";

            return result;
        }
        catch { return ""; }
    }

    private static string? FindGitDir(string path)
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

    private static string EscapeArg(string s) => s.Replace("\"", "\\\"");
}
