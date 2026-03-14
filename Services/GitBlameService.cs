using System.Diagnostics;
using CodeScan.Models;

namespace CodeScan.Services;

public sealed class GitBlameService
{
    private readonly string _repoRoot;
    private readonly bool _isGitRepo;

    public GitBlameService(string scanPath)
    {
        _repoRoot = FindGitRoot(scanPath);
        _isGitRepo = _repoRoot.Length > 0;
    }

    public bool IsAvailable => _isGitRepo;

    public void EnrichWithBlame(string filePath, List<MethodEntry> methods)
    {
        if (!_isGitRepo || methods.Count == 0)
            return;

        // 파일 전체의 blame을 한 번만 실행
        var blameLines = RunGitBlame(filePath);
        if (blameLines == null)
            return;

        foreach (var method in methods)
        {
            EnrichMethod(method, blameLines);
        }
    }

    private void EnrichMethod(MethodEntry method, List<BlameLine> blameLines)
    {
        // 메서드 범위(startLine~endLine) 내에서 가장 최근 커밋을 찾는다
        BlameLine? latest = null;

        foreach (var bl in blameLines)
        {
            if (bl.LineNo >= method.StartLine && bl.LineNo <= method.EndLine)
            {
                if (latest == null || string.CompareOrdinal(bl.DateIso, latest.DateIso) > 0)
                    latest = bl;
            }
        }

        if (latest != null)
        {
            method.LastAuthor = latest.Author;
            method.LastDate = latest.DateIso;
            method.LastCommitSummary = latest.Summary;
        }
    }

    private List<BlameLine>? RunGitBlame(string filePath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_repoRoot, filePath).Replace('\\', '/');

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"blame --porcelain \"{relativePath}\"",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { }
                return null;
            }

            if (proc.ExitCode != 0)
                return null;

            return ParsePorcelainBlame(output);
        }
        catch
        {
            return null;
        }
    }

    private static List<BlameLine> ParsePorcelainBlame(string output)
    {
        var result = new List<BlameLine>();
        var lines = output.Split('\n');

        // 커밋 해시별 정보 캐시 (porcelain은 같은 커밋의 상세를 첫 등장 시에만 출력)
        var commitCache = new Dictionary<string, (string Author, string Date, string Summary)>();

        string currentHash = "";
        string? currentAuthor = null;
        string? currentDate = null;
        string? currentSummary = null;
        int currentLine = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');

            // 커밋 해시 라인: "abc123 origLine resultLine numLines"
            if (line.Length >= 40 && IsHexLine(line))
            {
                // 이전 블록 저장
                if (currentLine > 0)
                {
                    // 캐시에서 가져오거나 현재 파싱 결과 사용
                    if (currentAuthor != null)
                    {
                        commitCache[currentHash] = (currentAuthor, currentDate ?? "", currentSummary ?? "");
                    }

                    if (commitCache.TryGetValue(currentHash, out var cached))
                    {
                        result.Add(new BlameLine
                        {
                            LineNo = currentLine,
                            Author = cached.Author,
                            DateIso = cached.Date,
                            Summary = cached.Summary
                        });
                    }
                }

                var parts = line.Split(' ');
                currentHash = parts[0];
                if (parts.Length >= 3 && int.TryParse(parts[2], out var ln))
                    currentLine = ln;

                currentAuthor = null;
                currentDate = null;
                currentSummary = null;
                continue;
            }

            if (line.StartsWith("author "))
                currentAuthor = line["author ".Length..];
            else if (line.StartsWith("author-time "))
            {
                if (long.TryParse(line["author-time ".Length..], out var epoch))
                    currentDate = DateTimeOffset.FromUnixTimeSeconds(epoch).ToString("yyyy-MM-dd");
            }
            else if (line.StartsWith("summary "))
                currentSummary = line["summary ".Length..];
        }

        // 마지막 블록
        if (currentLine > 0)
        {
            if (currentAuthor != null)
                commitCache[currentHash] = (currentAuthor, currentDate ?? "", currentSummary ?? "");

            if (commitCache.TryGetValue(currentHash, out var cached))
            {
                result.Add(new BlameLine
                {
                    LineNo = currentLine,
                    Author = cached.Author,
                    DateIso = cached.Date,
                    Summary = cached.Summary
                });
            }
        }

        return result;
    }

    private static bool IsHexLine(string line)
    {
        // 첫 40자가 hex인지 확인
        for (int i = 0; i < 40 && i < line.Length; i++)
        {
            var c = line[i];
            if (!char.IsAsciiHexDigit(c))
                return false;
        }
        return line.Length > 40 && line[40] == ' ';
    }

    private static string FindGitRoot(string path)
    {
        // Quick check: walk up looking for .git directory first (no process needed)
        var dir = path;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir; // found git repo
            }
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
        }
        // No .git directory found — not a git repo, skip entirely
        return "";
    }

    private sealed class BlameLine
    {
        public int LineNo { get; init; }
        public required string Author { get; init; }
        public required string DateIso { get; init; }
        public required string Summary { get; init; }
    }
}
