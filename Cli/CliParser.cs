using CodeScan.Commands;

namespace CodeScan.Cli;

internal static class CliParser
{
    internal static GlobalParseResult ParseGlobal(string[] args)
    {
        var options = new GlobalOptions();
        var remaining = new List<string>();
        var commandFound = false;

        foreach (var arg in args)
        {
            if (commandFound)
            {
                remaining.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "-h" or "--help": options.ShowHelp = true; break;
                case "-v" or "--version": options.ShowVersion = true; break;
                case "--verbose": options.Verbose = true; break;
                case "--devmode": options.DevMode = true; break;
                default:
                    remaining.Add(arg);
                    if (!arg.StartsWith('-')) commandFound = true;
                    break;
            }
        }

        return new GlobalParseResult(options, remaining.ToArray());
    }

    internal static ParseResult<ScanArguments> ParseScan(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
            return ParseResult<ScanArguments>.HelpResult();

        var forwarded = new List<string>();
        string? path = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "-i" or "--include" or "-e" or "--exclude" or "-d" or "--depth")
            {
                if (i + 1 >= args.Length)
                    return ParseResult<ScanArguments>.Failure($"option '{args[i]}' requires a value.");
                forwarded.Add(args[i]);
                forwarded.Add(args[++i]);
            }
            else if (!args[i].StartsWith('-') && path is null)
            {
                path = args[i];
            }
            else
            {
                forwarded.Add(args[i]);
            }
        }

        path ??= ".";
        forwarded.Insert(0, path);
        if (!forwarded.Contains("--detail")) forwarded.Add("--detail");
        if (!forwarded.Contains("--tree")) forwarded.Add("--tree");
        if (!forwarded.Contains("-s") && !forwarded.Contains("--stats")) forwarded.Add("--stats");
        return ParseResult<ScanArguments>.Success(new ScanArguments(forwarded.ToArray()));
    }

    internal static ParseResult<ListArguments> ParseList(string[] args)
    {
        var options = new ListOptions();
        string? path = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": return ParseResult<ListArguments>.HelpResult();
                case "-i" or "--include":
                    if (!TryNext(args, ref i, out var include)) return Missing<ListArguments>(args[i]);
                    options.Include = [.. include.Split(',', StringSplitOptions.RemoveEmptyEntries)];
                    break;
                case "-e" or "--exclude":
                    if (!TryNext(args, ref i, out var exclude)) return Missing<ListArguments>(args[i]);
                    options.Exclude = [.. exclude.Split(',', StringSplitOptions.RemoveEmptyEntries)];
                    break;
                case "-d" or "--depth":
                    if (!TryNext(args, ref i, out var depthText)) return Missing<ListArguments>(args[i]);
                    if (!int.TryParse(depthText, out var depth)) return Invalid<ListArguments>(args[i], depthText);
                    options.Depth = depth;
                    break;
                case "--tree": options.Tree = true; break;
                case "-s" or "--stats": options.Stats = true; break;
                case "--detail": options.Detail = true; break;
                case "--full" or "--rebuild": options.FullRebuild = true; break;
                case "--incremental": options.FullRebuild = false; break;
                case "--mode":
                    if (!TryNext(args, ref i, out var mode)) return Missing<ListArguments>(args[i]);
                    if (!mode.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                        !mode.Equals("incremental", StringComparison.OrdinalIgnoreCase))
                        return Invalid<ListArguments>(args[i], mode);
                    options.FullRebuild = mode.Equals("full", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    if (args[i].StartsWith('-')) return Unknown<ListArguments>(args[i]);
                    if (path is not null) return ParseResult<ListArguments>.Failure("only one path may be specified.");
                    path = args[i];
                    break;
            }
        }
        return path is null
            ? ParseResult<ListArguments>.Failure("path is required.")
            : ParseResult<ListArguments>.Success(new ListArguments(path, options));
    }

    internal static ParseResult<SearchArguments> ParseSearch(string[] args)
    {
        string? query = null;
        var options = new SearchOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": return ParseResult<SearchArguments>.HelpResult();
                case "-t" or "--type":
                    if (!TryNext(args, ref i, out var type)) return Missing<SearchArguments>(args[i]);
                    options.Type = type; break;
                case "-l" or "--limit":
                    if (!TryNext(args, ref i, out var limitText)) return Missing<SearchArguments>(args[i]);
                    if (!int.TryParse(limitText, out var limit)) return Invalid<SearchArguments>(args[i], limitText);
                    options.Limit = limit; break;
                case "-p" or "--project":
                    if (!TryNext(args, ref i, out var projectText)) return Missing<SearchArguments>(args[i]);
                    if (!long.TryParse(projectText, out var projectId)) return Invalid<SearchArguments>(args[i], projectText);
                    options.ProjectId = projectId; break;
                case "--graph": options.Graph = true; break;
                case "--query" or "--cypher": options.Graph = true; options.GraphQuery = true; break;
                case "--depth":
                    if (!TryNext(args, ref i, out var depthText)) return Missing<SearchArguments>(args[i]);
                    if (!int.TryParse(depthText, out var depth)) return Invalid<SearchArguments>(args[i], depthText);
                    options.GraphDepth = depth; break;
                default:
                    if (args[i].StartsWith('-')) return Unknown<SearchArguments>(args[i]);
                    if (query is not null) return ParseResult<SearchArguments>.Failure("only one search query may be specified.");
                    query = args[i]; break;
            }
        }
        return query is null
            ? ParseResult<SearchArguments>.Failure("search query is required.")
            : ParseResult<SearchArguments>.Success(new SearchArguments(query, options));
    }

    internal static ParseResult<GraphArguments> ParseGraph(string[] args, bool queryRequired)
    {
        var query = "";
        var options = new GraphOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": return ParseResult<GraphArguments>.HelpResult();
                case "-l" or "--limit":
                    if (!TryNext(args, ref i, out var limitText)) return Missing<GraphArguments>(args[i]);
                    if (!int.TryParse(limitText, out var limit)) return Invalid<GraphArguments>(args[i], limitText);
                    options.Limit = limit; break;
                case "-p" or "--project":
                    if (!TryNext(args, ref i, out var projectText)) return Missing<GraphArguments>(args[i]);
                    if (!long.TryParse(projectText, out var projectId)) return Invalid<GraphArguments>(args[i], projectText);
                    options.ProjectId = projectId; break;
                case "-d" or "--depth":
                    if (!TryNext(args, ref i, out var depthText)) return Missing<GraphArguments>(args[i]);
                    if (!int.TryParse(depthText, out var depth)) return Invalid<GraphArguments>(args[i], depthText);
                    options.Depth = depth; break;
                default:
                    if (args[i].StartsWith('-')) return Unknown<GraphArguments>(args[i]);
                    if (query.Length > 0) return ParseResult<GraphArguments>.Failure("only one graph query may be specified.");
                    query = args[i]; break;
            }
        }
        if (queryRequired && string.IsNullOrWhiteSpace(query))
            return ParseResult<GraphArguments>.Failure("graph query is required.");
        return ParseResult<GraphArguments>.Success(new GraphArguments(query, options));
    }

    private static bool TryNext(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length) { value = ""; return false; }
        value = args[++index];
        return true;
    }

    private static ParseResult<T> Missing<T>(string option) => ParseResult<T>.Failure($"option '{option}' requires a value.");
    private static ParseResult<T> Invalid<T>(string option, string value) => ParseResult<T>.Failure($"invalid value '{value}' for option '{option}'.");
    private static ParseResult<T> Unknown<T>(string option) => ParseResult<T>.Failure($"unknown option '{option}'.");
}

internal sealed record GlobalParseResult(GlobalOptions Options, string[] Remaining);
internal sealed record ScanArguments(string[] ForwardedArguments);
internal sealed record ListArguments(string Path, ListOptions Options);
internal sealed record SearchArguments(string Query, SearchOptions Options);
internal sealed record GraphArguments(string Query, GraphOptions Options);

internal sealed record ParseResult<T>(T? Value, bool Help, string? Error)
{
    internal bool IsSuccess => Value is not null && Error is null && !Help;
    internal static ParseResult<T> Success(T value) => new(value, false, null);
    internal static ParseResult<T> HelpResult() => new(default, true, null);
    internal static ParseResult<T> Failure(string error) => new(default, false, error);
}
