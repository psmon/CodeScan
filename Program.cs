using CodeScan.Commands;
using CodeScan.Services;
using CodeScan.Tui;

namespace CodeScan;

class Program
{
    const string Version = "0.2.0";

    static SqliteStore OpenDb() => new(
        Path.Combine(Directory.GetCurrentDirectory(), "codescan.db"));

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var globalArgs = ParseGlobalOptions(args, out var remaining);

        if (globalArgs.ShowHelp && remaining.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        if (globalArgs.ShowVersion)
        {
            Console.WriteLine($"codescan v{Version}");
            return 0;
        }

        if (remaining.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var command = remaining[0].ToLowerInvariant();
        var commandArgs = remaining[1..];

        return command switch
        {
            "list" => RunList(commandArgs, globalArgs),
            "search" => RunSearch(commandArgs),
            "projects" => RunProjects(),
            "tui" => RunTui(),
            "help" => RunHelp(commandArgs),
            _ => UnknownCommand(command)
        };
    }

    static int RunList(string[] args, GlobalOptions global)
    {
        if (global.ShowHelp)
        {
            PrintListHelp();
            return 0;
        }

        var options = new ListOptions();
        string? path = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    PrintListHelp();
                    return 0;
                case "-i" or "--include" when i + 1 < args.Length:
                    options.Include = [.. args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries)];
                    break;
                case "-e" or "--exclude" when i + 1 < args.Length:
                    options.Exclude = [.. args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries)];
                    break;
                case "-d" or "--depth" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var depth))
                        options.Depth = depth;
                    break;
                case "--tree":
                    options.Tree = true;
                    break;
                case "-s" or "--stats":
                    options.Stats = true;
                    break;
                case "--detail":
                    options.Detail = true;
                    break;
                default:
                    if (!args[i].StartsWith('-') && path == null)
                        path = args[i];
                    break;
            }
        }

        if (path == null)
        {
            Console.Error.WriteLine("Error: path is required.");
            Console.Error.WriteLine("Usage: codescan list <path> [options]");
            return 1;
        }

        IResultStore? store = null;
        if (global.DevMode)
        {
            var storeDir = Path.Combine(Directory.GetCurrentDirectory(), "Prompt", "TestFileDB");
            store = new FileResultStore(storeDir);
        }

        options.Verbose = global.Verbose;

        using var db = OpenDb();
        var cmd = new ListCommand(store, db);
        return cmd.Execute(path, options);
    }

    static int RunSearch(string[] args)
    {
        string? query = null;
        var options = new SearchOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-t" or "--type" when i + 1 < args.Length:
                    options.Type = args[++i];
                    break;
                case "-l" or "--limit" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var lim))
                        options.Limit = lim;
                    break;
                case "-h" or "--help":
                    PrintSearchHelp();
                    return 0;
                default:
                    if (!args[i].StartsWith('-') && query == null)
                        query = args[i];
                    break;
            }
        }

        if (query == null)
        {
            Console.Error.WriteLine("Error: search query is required.");
            Console.Error.WriteLine("Usage: codescan search <query> [options]");
            return 1;
        }

        using var db = OpenDb();
        var cmd = new SearchCommand(db);
        return cmd.Execute(query, options);
    }

    static int RunProjects()
    {
        using var db = OpenDb();
        var cmd = new ProjectsCommand(db);
        return cmd.Execute();
    }

    static int RunTui()
    {
        var app = new TuiApp();
        app.Run();
        return 0;
    }

    static int RunHelp(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list": PrintListHelp(); break;
            case "search": PrintSearchHelp(); break;
            case "projects": Console.WriteLine("  codescan projects - List all indexed projects."); break;
            case "tui": Console.WriteLine("  codescan tui - Interactive TUI mode."); break;
            default:
                Console.WriteLine($"Unknown command: {args[0]}");
                PrintHelp();
                break;
        }
        return 0;
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Run 'codescan --help' to see available commands.");
        return 1;
    }

    static GlobalOptions ParseGlobalOptions(string[] args, out string[] remaining)
    {
        var global = new GlobalOptions();
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": global.ShowHelp = true; break;
                case "-v" or "--version": global.ShowVersion = true; break;
                case "--verbose": global.Verbose = true; break;
                case "--devmode": global.DevMode = true; break;
                default: rest.Add(args[i]); break;
            }
        }

        remaining = rest.ToArray();
        return global;
    }

    static void PrintHelp()
    {
        Console.WriteLine($"""
        codescan v{Version} - Code Scanner & Indexer

        Usage: codescan [command] [options]

        Commands:
          list <path>       Scan directory, analyze, and index to DB
          search <query>    Search indexed methods, files, and docs
          projects          List all indexed projects
          tui               Interactive TUI mode (user mode)
          help [command]    Show help

        Global Options:
          -h, --help     Show help
          -v, --version  Show version
          --verbose      Verbose output
          --devmode      Also save results to Prompt/TestFileDB/ as log files

        Examples:
          codescan list ./src --tree --detail --stats
          codescan search "HttpClient" --type method
          codescan projects
          codescan tui
        """);
    }

    static void PrintListHelp()
    {
        Console.WriteLine("""
        codescan list - Scan directory, analyze source, and index to DB

        Usage: codescan list <path> [options]

        Options:
          -i, --include <exts>   Include extensions (comma-sep, e.g. .cs,.js)
          -e, --exclude <dirs>   Exclude directories (comma-sep, e.g. bin,obj)
          -d, --depth <n>        Max traversal depth
          --tree                 Tree format output
          -s, --stats            Include file/size statistics
          --detail               Analyze class:method + git blame per source file
          -h, --help             Show help

        Notes:
          Results are always saved to codescan.db (SQLite).
          .md files are always included even with --include filter.
          --detail shows last commit info when used in a git repository.

        Examples:
          codescan list D:\Code\MyProject --tree --stats
          codescan list ./src --include ".cs" --tree --detail
          codescan list ./src --depth 2 --stats --devmode
        """);
    }

    static void PrintSearchHelp()
    {
        Console.WriteLine("""
        codescan search - Search indexed methods, files, and docs

        Usage: codescan search <query> [options]

        Options:
          -t, --type <type>   Filter by: method, file, doc
          -l, --limit <n>     Max results (default: 30)
          -h, --help          Show help

        Examples:
          codescan search "HttpClient"
          codescan search "auth" --type method --limit 10
          codescan search "SSE" --type doc
        """);
    }
}

sealed class GlobalOptions
{
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool Verbose { get; set; }
    public bool DevMode { get; set; }
}
