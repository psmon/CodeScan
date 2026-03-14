namespace CodeScan.Models;

public sealed class CommentBlock
{
    public required string Comment { get; init; }
    public string NearbyCode { get; init; } = "";
    public string BeforeCode { get; init; } = "";
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
}
