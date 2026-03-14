using CodeScan.Models;

namespace CodeScan.Services;

public sealed class DirectoryScanner
{
    private readonly HashSet<string> _includeExts;
    private readonly HashSet<string> _excludeDirs;
    private readonly bool _respectGitignore;
    private readonly int _maxDepth;
    private readonly List<string> _gitignorePatterns = [];

    public DirectoryScanner(
        IEnumerable<string>? includeExts = null,
        IEnumerable<string>? excludeDirs = null,
        bool respectGitignore = true,
        int maxDepth = int.MaxValue)
    {
        _includeExts = includeExts?.Select(NormalizeExt).ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? [];
        _excludeDirs = excludeDirs?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? [];
        _respectGitignore = respectGitignore;
        _maxDepth = maxDepth;

        // .md는 항상 포함 (핵심 원칙)
        if (_includeExts.Count > 0)
            _includeExts.Add(".md");
    }

    public List<FileEntry> Scan(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Path not found: {root}");

        if (_respectGitignore)
            LoadGitignore(root);

        var entries = new List<FileEntry>();
        ScanDirectory(root, root, 0, entries);
        return entries;
    }

    private void ScanDirectory(string rootPath, string currentPath, int depth, List<FileEntry> entries)
    {
        if (depth > _maxDepth)
            return;

        string[] dirs;
        string[] files;

        try
        {
            dirs = Directory.GetDirectories(currentPath);
            files = Directory.GetFiles(currentPath);
        }
        catch (Exception) // UnauthorizedAccess, IO, Security etc.
        {
            return;
        }

        // Sort by most recently modified first
        Array.Sort(dirs, (a, b) => SafeGetWriteTime(b).CompareTo(SafeGetWriteTime(a)));
        Array.Sort(files, (a, b) => SafeGetWriteTime(b).CompareTo(SafeGetWriteTime(a)));

        foreach (var dir in dirs)
        {
            var dirName = Path.GetFileName(dir);

            if (IsDefaultExcluded(dirName))
                continue;

            if (_excludeDirs.Contains(dirName))
                continue;

            if (_respectGitignore && IsGitignored(dir, rootPath, isDir: true))
                continue;

            var entry = new FileEntry
            {
                FullPath = dir,
                RelativePath = Path.GetRelativePath(rootPath, dir),
                Name = dirName,
                Size = 0,
                IsDirectory = true,
                Depth = depth
            };
            entries.Add(entry);

            ScanDirectory(rootPath, dir, depth + 1, entries);
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (_includeExts.Count > 0 && !_includeExts.Contains(ext))
                continue;

            if (_respectGitignore && IsGitignored(file, rootPath, isDir: false))
                continue;

            long size = 0;
            try { size = new FileInfo(file).Length; } catch { /* skip */ }

            entries.Add(new FileEntry
            {
                FullPath = file,
                RelativePath = Path.GetRelativePath(rootPath, file),
                Name = fileName,
                Size = size,
                IsDirectory = false,
                Depth = depth
            });
        }
    }

    private static bool IsDefaultExcluded(string dirName)
    {
        return dirName is ".git" or ".vs" or ".idea"
            or "bin" or "obj"
            or "node_modules"
            or ".next" or "dist" or "build"
            or "__pycache__";
    }

    private void LoadGitignore(string rootPath)
    {
        var gitignorePath = Path.Combine(rootPath, ".gitignore");
        if (!File.Exists(gitignorePath))
            return;

        foreach (var line in File.ReadAllLines(gitignorePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            _gitignorePatterns.Add(trimmed);
        }
    }

    private bool IsGitignored(string fullPath, string rootPath, bool isDir)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath)
                           .Replace('\\', '/');

        if (isDir)
            relative += "/";

        foreach (var pattern in _gitignorePatterns)
        {
            if (MatchGitignorePattern(pattern, relative))
                return true;
        }
        return false;
    }

    private static bool MatchGitignorePattern(string pattern, string path)
    {
        var p = pattern.TrimEnd('/');
        var isNegation = p.StartsWith('!');
        if (isNegation) return false; // 부정 패턴은 skip

        var isDirOnly = pattern.EndsWith('/');
        if (isDirOnly && !path.EndsWith('/'))
            return false;

        p = p.TrimStart('/');

        // 단순 디렉토리/파일명 매칭
        var fileName = path.TrimEnd('/');
        if (fileName.Contains('/'))
            fileName = fileName[(fileName.LastIndexOf('/') + 1)..];

        // 와일드카드 패턴
        if (p.Contains('*'))
            return WildcardMatch(p, path.TrimEnd('/')) || WildcardMatch(p, fileName);

        // 정확한 이름 매칭 (디렉토리명 또는 파일명)
        return string.Equals(p, fileName, StringComparison.OrdinalIgnoreCase)
            || path.TrimEnd('/').StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool WildcardMatch(string pattern, string input)
    {
        var pIdx = 0;
        var iIdx = 0;
        var starP = -1;
        var starI = -1;

        while (iIdx < input.Length)
        {
            if (pIdx < pattern.Length && (pattern[pIdx] == input[iIdx] || pattern[pIdx] == '?'))
            {
                pIdx++;
                iIdx++;
            }
            else if (pIdx < pattern.Length && pattern[pIdx] == '*')
            {
                starP = pIdx++;
                starI = iIdx;
            }
            else if (starP >= 0)
            {
                pIdx = starP + 1;
                iIdx = ++starI;
            }
            else
            {
                return false;
            }
        }

        while (pIdx < pattern.Length && pattern[pIdx] == '*')
            pIdx++;

        return pIdx == pattern.Length;
    }

    private static DateTime SafeGetWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string NormalizeExt(string ext)
    {
        return ext.StartsWith('.') ? ext.ToLowerInvariant() : $".{ext.ToLowerInvariant()}";
    }
}
