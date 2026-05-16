using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class GraphCommand
{
    private readonly SqliteStore _db;

    public GraphCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(string query, GraphOptions options)
    {
        var graph = _db.SearchGraph(query, options.ProjectId, options.Depth, options.Limit);
        if (graph.Nodes.Count == 0)
        {
            Console.WriteLine(string.IsNullOrWhiteSpace(query)
                ? "No graph data found. Run 'codescan scan --detail' first."
                : $"No graph results for: {query}");
            return 0;
        }

        var scope = options.ProjectId.HasValue ? $" (project #{options.ProjectId})" : "";
        var title = string.IsNullOrWhiteSpace(query) ? "Graph Overview" : $"Graph Search: {query}";
        Console.WriteLine($"=== {title}{scope} ===");
        Console.WriteLine($"Nodes: {graph.Nodes.Count}, Edges: {graph.Edges.Count}\n");

        Console.WriteLine("--- Nodes ---");
        foreach (var n in graph.Nodes)
        {
            Console.WriteLine($"  #{n.Id} [{n.Kind}] {n.Label}");
            if (!string.IsNullOrWhiteSpace(n.Path))
                Console.WriteLine($"       {n.Path}");
            if (!string.IsNullOrWhiteSpace(n.Detail))
                Console.WriteLine($"       {Trim(n.Detail, 120)}");
        }

        if (graph.Edges.Count > 0)
        {
            var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
            Console.WriteLine("\n--- Edges ---");
            foreach (var e in graph.Edges)
            {
                labels.TryGetValue(e.From, out var from);
                labels.TryGetValue(e.To, out var to);
                Console.WriteLine($"  {from ?? e.From.ToString()} -[{e.Kind}]-> {to ?? e.To.ToString()}");
            }
        }

        return 0;
    }

    private static string Trim(string value, int max)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= max ? value : value[..max] + "...";
    }
}

public sealed class GraphOptions
{
    public long? ProjectId { get; set; }
    public int Depth { get; set; } = 1;
    public int Limit { get; set; } = 80;
}
