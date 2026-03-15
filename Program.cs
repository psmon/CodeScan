using CodeScan.Commands;
using CodeScan.Services;
using CodeScan.Tui;

namespace CodeScan;

class Program
{
    const string Version = "0.3.0";

    static SqliteStore OpenDb() => new(AppPaths.DbPath);

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
            "projects" => RunProjects(commandArgs),
            "project" => RunProject(commandArgs),
            "project-addinfo" => RunProjectAddInfo(commandArgs),
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
            store = new FileResultStore(AppPaths.GetLogDir());
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
                case "-p" or "--project" when i + 1 < args.Length:
                    if (long.TryParse(args[++i], out var pid))
                        options.ProjectId = pid;
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

    static int RunProjects(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            PrintProjectsHelp();
            return 0;
        }

        using var db = OpenDb();
        var cmd = new ProjectsCommand(db);
        return cmd.Execute();
    }

    static int RunProject(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintProjectHelp();
            return args.Length > 0 ? 0 : 1;
        }

        if (!long.TryParse(args[0], out var projectId))
        {
            Console.Error.WriteLine($"Error: invalid project ID '{args[0]}'");
            return 1;
        }

        using var db = OpenDb();
        var cmd = new ProjectCommand(db);
        return cmd.Execute(projectId);
    }

    static int RunProjectAddInfo(string[] args)
    {
        if (args.Length < 2 || args[0] is "-h" or "--help")
        {
            PrintProjectAddInfoHelp();
            return args.Length > 0 && args[0] is "-h" or "--help" ? 0 : 1;
        }

        if (!long.TryParse(args[0], out var projectId))
        {
            Console.Error.WriteLine($"Error: invalid project ID '{args[0]}'");
            return 1;
        }

        var description = string.Join(" ", args[1..]);

        using var db = OpenDb();
        var cmd = new ProjectAddInfoCommand(db);
        return cmd.Execute(projectId, description);
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
            case "projects": PrintProjectsHelp(); break;
            case "project": PrintProjectHelp(); break;
            case "project-addinfo": PrintProjectAddInfoHelp(); break;
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
        bool commandFound = false;

        for (int i = 0; i < args.Length; i++)
        {
            // Once a command word is found, pass everything else through
            if (commandFound)
            {
                rest.Add(args[i]);
                continue;
            }

            switch (args[i])
            {
                case "-h" or "--help": global.ShowHelp = true; break;
                case "-v" or "--version": global.ShowVersion = true; break;
                case "--verbose": global.Verbose = true; break;
                case "--devmode": global.DevMode = true; break;
                default:
                    rest.Add(args[i]);
                    if (!args[i].StartsWith('-'))
                        commandFound = true;
                    break;
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
          list <path>                  Scan directory, analyze, and index to DB
          search <query>               Search indexed methods, files, and docs
          projects                     List all indexed projects
          project <id>                 Show project detail info
          project-addinfo <id> <text>  Add description to a project
          tui                          Interactive TUI mode (user mode)
          help [command]               Show help

        Global Options:
          -h, --help     Show help
          -v, --version  Show version
          --verbose      Verbose output
          --devmode      Also save results to ~/.codescan/logs/ as log files

        Data:
          DB:   ~/.codescan/db/codescan.db
          Logs: ~/.codescan/logs/

        Examples:
          codescan list ./src --tree --detail --stats
          codescan search "HttpClient" --type method
          codescan search "auth" --project 1
          codescan project 1
          codescan project-addinfo 1 "Main web API backend service"
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
          Results are always saved to ~/.codescan/db/codescan.db (SQLite).
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
          -t, --type <type>      Filter by: method, file, doc, comment, commit
          -l, --limit <n>        Max results (default: 30)
          -p, --project <id>     Search within a specific project only
          -h, --help             Show help

        Examples:
          codescan search "HttpClient"
          codescan search "auth" --type method --limit 10
          codescan search "SSE" --type doc
          codescan search "TODO" --project 1 --type comment
        """);
    }

    static void PrintProjectsHelp()
    {
        Console.WriteLine("""
        codescan projects - List all indexed projects

        Usage: codescan projects

        Shows a table of all projects that have been scanned and indexed:
          - Project ID, file/dir count, total size
          - Last scan timestamp
          - Project path

        Use project IDs with other commands:
          codescan project <id>                 View project detail
          codescan project-addinfo <id> <text>  Add description
          codescan search <query> --project <id>  Search within project

        Examples:
          codescan projects
        """);
    }

    static void PrintProjectHelp()
    {
        Console.WriteLine("""
        codescan project - Show project detail info

        Usage: codescan project <id>

        Shows all collected information for a specific project:
          - Project path and scan statistics
          - Additional description (addinfo) if set
          - Scan history
          - Prompt to add description via LLM if no addinfo exists

        Examples:
          codescan project 1
          codescan project 2
        """);
    }

    static void PrintProjectAddInfoHelp()
    {
        Console.WriteLine("""
        codescan project-addinfo - Add description to a project

        Usage: codescan project-addinfo <id> <description>

        Adds or replaces a single description for the specified project.
        Only one description per project is stored (overwrites previous).

        This is useful for providing context that cannot be derived from
        code alone. LLMs can use 'codescan project <id>' to understand
        the project, then add their analysis as addinfo:

          1. codescan project <id>          # Review project info
          2. codescan search "" --project <id>  # Browse indexed data
          3. codescan project-addinfo <id> "..."  # Save understanding

        Examples:
          codescan project-addinfo 1 "ASP.NET Core + Akka.NET web API with LLM chatbot"
          codescan project-addinfo 2 "React frontend with TypeScript, i18n support"
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
