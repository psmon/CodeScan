namespace CodeScan.Services;

public sealed class GraphNode
{
    public long Id { get; init; }
    public long ScanId { get; init; }
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public string Path { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed class GraphEdge
{
    public long Id { get; init; }
    public long ScanId { get; init; }
    public long From { get; init; }
    public long To { get; init; }
    public required string Kind { get; init; }
    public string Label { get; init; } = "";
}

public sealed class GraphData
{
    public List<GraphNode> Nodes { get; init; } = [];
    public List<GraphEdge> Edges { get; init; } = [];
}
