using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class SearchCommand
{
    private readonly SqliteStore _db;

    public SearchCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(string query, SearchOptions options)
    {
        // 1) DB search (FTS5 + LIKE fallback)
        var dbResults = _db.Search(query, options.Type, options.Limit, options.ProjectId);

        // 2) Git log search (hybrid) — skip if type filter excludes commits or project-scoped
        var gitResults = new List<SearchResult>();
        if (options.Type is null or "commit" && !options.ProjectId.HasValue)
        {
            var gitLimit = Math.Max(5, options.Limit - dbResults.Count);
            gitResults = GitLogSearchService.Search(query, gitLimit);
        }

        var totalDb = dbResults.Count;
        var totalGit = gitResults.Count;

        if (totalDb == 0 && totalGit == 0)
        {
            Console.WriteLine($"No results for: {query}");
            return 0;
        }

        // Print DB results
        if (totalDb > 0)
        {
            var scope = options.ProjectId.HasValue ? $" (project #{options.ProjectId})" : "";
            Console.WriteLine($"=== DB Index ({totalDb} results){scope} ===\n");
            PrintResults(dbResults);
        }

        // Print Git results
        if (totalGit > 0)
        {
            Console.WriteLine($"=== Git Log ({totalGit} results) ===\n");
            PrintResults(gitResults);
        }

        return 0;
    }

    public static void PrintResults(List<SearchResult> results)
    {
        foreach (var r in results)
        {
            var typeTag = r.Type switch
            {
                "method" => "[METHOD]",
                "file"   => "[FILE]  ",
                "doc"    => "[DOC]   ",
                "commit" => "[COMMIT]",
                "comment" => "[COMMENT]",
                _ => $"[{r.Type.ToUpper()}]"
            };

            Console.WriteLine($"  {typeTag} {r.Name}");
            if (!string.IsNullOrEmpty(r.Path))
                Console.WriteLine($"           {r.Path}");
            if (!string.IsNullOrEmpty(r.Excerpt))
                Console.WriteLine($"           {r.Excerpt}");
            Console.WriteLine();
        }
    }
}

public sealed class SearchOptions
{
    public string? Type { get; set; } // method, file, doc, commit
    public int Limit { get; set; } = 30;
    public long? ProjectId { get; set; }
}
