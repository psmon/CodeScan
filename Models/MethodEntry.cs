namespace CodeScan.Models;

public sealed class MethodEntry
{
    public required string ClassName { get; init; }
    public required string MethodName { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public string? LastAuthor { get; set; }
    public string? LastDate { get; set; }
    public string? LastCommitSummary { get; set; }

    public string DisplayName => $"{ClassName}:{MethodName}";

    public string BlameInfo =>
        LastAuthor != null
            ? $"{LastDate} {LastAuthor} - {LastCommitSummary}"
            : "(no git blame)";
}
