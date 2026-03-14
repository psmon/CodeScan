namespace CodeScan.Models;

public sealed class FileEntry
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public required long Size { get; init; }
    public required bool IsDirectory { get; init; }
    public required int Depth { get; init; }
    public string Extension => Path.GetExtension(Name).ToLowerInvariant();
    public List<MethodEntry> Methods { get; set; } = [];
}
