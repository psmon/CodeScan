using CodeScan.Commands;
using CodeScan.Services;
using CodeScan.Tui;

namespace CodeScan;

class Program
{
    const string Version = "0.1.0";

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
            var storeDir = Path.Combine(AppContext.BaseDirectory, "Prompt", "TestFileDB");
            if (!Directory.Exists(Path.GetDirectoryName(storeDir)))
                storeDir = Path.Combine(Directory.GetCurrentDirectory(), "Prompt", "TestFileDB");

            store = new FileResultStore(storeDir);
        }

        options.Verbose = global.Verbose;

        var cmd = new ListCommand(store);
        return cmd.Execute(path, options);
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
            case "list":
                PrintListHelp();
                break;
            case "tui":
                Console.WriteLine("  codescan tui - Interactive TUI mode for browsing and scanning code.");
                break;
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
                case "-h" or "--help":
                    global.ShowHelp = true;
                    break;
                case "-v" or "--version":
                    global.ShowVersion = true;
                    break;
                case "--verbose":
                    global.Verbose = true;
                    break;
                case "--devmode":
                    global.DevMode = true;
                    break;
                default:
                    rest.Add(args[i]);
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
          list <path>    Scan directory and list files (CLI/AI mode)
          tui            Interactive TUI mode (user mode)
          help [command] Show help

        Global Options:
          -h, --help     Show help
          -v, --version  Show version
          --verbose      Verbose output
          --devmode      Dev mode (save results to Prompt/TestFileDB/)

        Examples:
          codescan list ./src --tree --stats
          codescan list ./src --include ".cs,.md" --exclude "bin,obj" --tree
          codescan list ./src --tree --detail --devmode
          codescan tui
        """);
    }

    static void PrintListHelp()
    {
        Console.WriteLine("""
        codescan list - Scan directory and list files

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
          .md files are always included even with --include filter.
          .git, bin, obj, node_modules etc. are excluded by default.
          --detail shows last commit info when used in a git repository.

        Examples:
          codescan list D:\Code\MyProject --tree --stats
          codescan list ./src --include ".cs" --tree --detail
          codescan list ./src --depth 2 --stats --devmode
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
