namespace CodeScan.Models;

/// <summary>
/// A single ATX heading (# .. ######) extracted from a markdown file.
/// </summary>
public sealed class MarkdownHeading
{
    public required int Level { get; init; }   // 1 = '#', 2 = '##', ...
    public required string Text { get; init; }  // emphasis/backticks stripped
    public required int Line { get; init; }     // 1-based line number
}

/// <summary>
/// Parsed structure of a markdown file: YAML frontmatter (metadata),
/// headings, and the body text (frontmatter removed).
/// </summary>
public sealed class MarkdownDoc
{
    /// <summary>Flattened, searchable "key: value" frontmatter text. Empty when none.</summary>
    public string FrontMatter { get; set; } = "";

    /// <summary>Markdown body with the YAML frontmatter block stripped.</summary>
    public string Body { get; set; } = "";

    public List<MarkdownHeading> Headings { get; set; } = [];

    public bool HasContent => FrontMatter.Length > 0 || Body.Length > 0 || Headings.Count > 0;
}
