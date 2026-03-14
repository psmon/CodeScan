namespace CodeScan.Services;

/// <summary>
/// Finds the first project doc (README.md, AGENT.md, CLAUDE.md)
/// searching from root directory upward-first (parent dirs have priority).
/// </summary>
public static class ProjectDocFinder
{
    private static readonly string[] DocNames =
    [
        "README.md", "AGENT.md", "CLAUDE.md",
        "readme.md", "agent.md", "claude.md",
        "Readme.md", "Agent.md", "Claude.md"
    ];

    /// <summary>
    /// Find the first matching doc file. Searches the root path first,
    /// then subdirectories in breadth-first order.
    /// </summary>
    public static string? FindDoc(string rootPath)
    {
        // Check root directory first
        foreach (var name in DocNames)
        {
            var path = Path.Combine(rootPath, name);
            if (File.Exists(path))
                return path;
        }

        // Walk parent directories (upper dirs have priority)
        var dir = Directory.GetParent(rootPath)?.FullName;
        var checkedCount = 0;
        while (!string.IsNullOrEmpty(dir) && checkedCount < 5)
        {
            foreach (var name in DocNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return path;
            }
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
            checkedCount++;
        }

        // Search subdirectories (breadth-first, max depth 2)
        try
        {
            var subDirs = new Queue<(string Path, int Depth)>();
            subDirs.Enqueue((rootPath, 0));

            while (subDirs.Count > 0)
            {
                var (current, depth) = subDirs.Dequeue();
                if (depth > 2) continue;

                try
                {
                    foreach (var name in DocNames)
                    {
                        var path = Path.Combine(current, name);
                        if (File.Exists(path))
                            return path;
                    }

                    if (depth < 2)
                    {
                        foreach (var sub in Directory.GetDirectories(current))
                        {
                            var subName = Path.GetFileName(sub);
                            if (subName is ".git" or "bin" or "obj" or "node_modules")
                                continue;
                            subDirs.Enqueue((sub, depth + 1));
                        }
                    }
                }
                catch { /* skip inaccessible */ }
            }
        }
        catch { /* skip */ }

        return null;
    }

    public static string? ReadDoc(string rootPath)
    {
        var docPath = FindDoc(rootPath);
        if (docPath == null) return null;

        try
        {
            var content = File.ReadAllText(docPath);
            var relativePath = Path.GetRelativePath(rootPath, docPath);
            return $"\n\n=== Project Doc: {relativePath} ===\n{content}\n";
        }
        catch
        {
            return null;
        }
    }
}
