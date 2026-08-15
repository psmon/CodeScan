namespace CodeScan.Services;

public sealed class GraphNode
{
    public long Id { get; init; }
    public long ProjectId { get; init; }
    // Last scan that observed this node (project-scoped graph is no longer
    // scan-partitioned; this is a provenance/ordering stamp, not an owner).
    public long ScanId { get; init; }
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public string Path { get; init; } = "";
    public string Detail { get; init; } = "";
    // 'active' (live) or 'stale' (auto row retired because it vanished from source).
    public string State { get; init; } = "active";
}

public sealed class GraphEdge
{
    public long Id { get; init; }
    public long ScanId { get; init; }
    public long From { get; init; }
    public long To { get; init; }
    public required string Kind { get; init; }
    public string Label { get; init; } = "";
    // Evidence strength: reinforced on each re-observation, ++ on curation strengthen.
    public int Weight { get; init; } = 1;
}

public sealed class GraphData
{
    public List<GraphNode> Nodes { get; init; } = [];
    public List<GraphEdge> Edges { get; init; } = [];
}
