using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ListCommand
{
    private readonly IResultStore? _store;

    public ListCommand(IResultStore? store = null)
    {
        _store = store;
    }

    public int Execute(string path, ListOptions options)
    {
        try
        {
            var scanner = new DirectoryScanner(
                includeExts: options.Include,
                excludeDirs: options.Exclude,
                respectGitignore: true,
                maxDepth: options.Depth);

            var entries = scanner.Scan(path);

            // --detail: 소스 파일에서 클래스:함수 추출 + git blame
            if (options.Detail)
            {
                var gitBlame = new GitBlameService(path);
                var sourceFiles = entries.Where(e =>
                    !e.IsDirectory && SourceAnalyzer.IsSourceFile(e.Extension)).ToList();

                var total = sourceFiles.Count;
                var count = 0;

                foreach (var file in sourceFiles)
                {
                    count++;
                    if (options.Verbose)
                        Console.Error.Write($"\rAnalyzing... {count}/{total} {file.Name}                ");

                    file.Methods = SourceAnalyzer.ExtractMethods(file.FullPath);

                    if (gitBlame.IsAvailable && file.Methods.Count > 0)
                        gitBlame.EnrichWithBlame(file.FullPath, file.Methods);
                }

                if (options.Verbose)
                    Console.Error.WriteLine();
            }

            var output = options.Tree
                ? TreeFormatter.Format(path, entries, options.Stats)
                : TreeFormatter.FormatFlat(path, entries, options.Stats);

            Console.Write(output);

            _store?.Save("list", output);

            return 0;
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public sealed class ListOptions
{
    public List<string>? Include { get; set; }
    public List<string>? Exclude { get; set; }
    public int Depth { get; set; } = int.MaxValue;
    public bool Tree { get; set; }
    public bool Stats { get; set; }
    public bool Detail { get; set; }
    public bool Verbose { get; set; }
}
