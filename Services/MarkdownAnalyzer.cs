using System.Text;
using System.Text.RegularExpressions;
using CodeScan.Models;

namespace CodeScan.Services;

/// <summary>
/// Parses markdown files into a searchable structure: YAML frontmatter
/// (metadata), ATX headings (# .. ######) and the remaining body text.
/// Regex is [GeneratedRegex] for native-AOT safety.
/// </summary>
public static partial class MarkdownAnalyzer
{
    // ATX heading: 1-6 leading '#', a space, then the heading text.
    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    // Strip markdown emphasis/inline-code markers for cleaner heading text.
    [GeneratedRegex(@"[*_`~]")]
    private static partial Regex EmphasisRegex();

    public static MarkdownDoc Analyze(string filePath)
    {
        try
        {
            return AnalyzeContent(File.ReadAllText(filePath));
        }
        catch
        {
            return new MarkdownDoc();
        }
    }

    public static MarkdownDoc AnalyzeContent(string content)
    {
        var doc = new MarkdownDoc();
        var body = content;

        // --- YAML frontmatter: a leading '---' line, terminated by '---' or '...' ---
        var fmBody = ExtractFrontMatter(content, out var frontMatter);
        if (frontMatter != null)
        {
            doc.FrontMatter = FlattenFrontMatter(frontMatter);
            body = fmBody;
        }

        doc.Body = body;

        // --- Headings ---
        // Line numbers are relative to the ORIGINAL file so they point at the
        // real source location (frontmatter offset added back).
        var lineOffset = content.Length - body.Length > 0
            ? CountLines(content[..(content.Length - body.Length)])
            : 0;

        foreach (Match m in HeadingRegex().Matches(body))
        {
            var level = m.Groups[1].Value.Length;
            var text = EmphasisRegex().Replace(m.Groups[2].Value, "").Trim();
            if (text.Length == 0)
                continue;

            var line = CountLines(body[..m.Index]) + 1 + lineOffset;
            doc.Headings.Add(new MarkdownHeading { Level = level, Text = text, Line = line });
        }

        return doc;
    }

    /// <summary>
    /// Returns the body with the leading YAML frontmatter block removed.
    /// Sets <paramref name="frontMatter"/> to the raw frontmatter (null if absent).
    /// </summary>
    private static string ExtractFrontMatter(string content, out string? frontMatter)
    {
        frontMatter = null;

        // Must start with '---' on the very first line (allow leading BOM/whitespace-free).
        var text = content.StartsWith('﻿') ? content[1..] : content;
        if (!text.StartsWith("---"))
            return content;

        var firstNl = text.IndexOf('\n');
        if (firstNl < 0)
            return content;

        // First line must be exactly '---' (ignoring trailing CR).
        var firstLine = text[..firstNl].TrimEnd('\r');
        if (firstLine != "---")
            return content;

        // Find the closing delimiter line ('---' or '...').
        var rest = text[(firstNl + 1)..];
        var lines = rest.Split('\n');
        var sb = new StringBuilder();
        var closed = false;
        var consumed = 0;
        foreach (var raw in lines)
        {
            consumed += raw.Length + 1; // +1 for the '\n' removed by Split
            var line = raw.TrimEnd('\r');
            if (line is "---" or "...")
            {
                closed = true;
                break;
            }
            sb.Append(raw).Append('\n');
        }

        if (!closed)
            return content;

        frontMatter = sb.ToString();
        var bodyStart = (firstNl + 1) + Math.Min(consumed, rest.Length);
        return bodyStart <= text.Length ? text[bodyStart..] : "";
    }

    /// <summary>
    /// Flattens YAML frontmatter into a single searchable string: keeps keys and
    /// values, unwraps simple "- item" lists and "[[wiki links]]". Not a full YAML
    /// parser — just enough to make tags/aliases/title/etc. matchable in FTS.
    /// </summary>
    private static string FlattenFrontMatter(string frontMatter)
    {
        var parts = new List<string>();
        foreach (var raw in frontMatter.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            // List item: "- value" or "- [[wiki link]]"
            if (line.StartsWith("- "))
            {
                var item = CleanScalar(line[2..]);
                if (item.Length > 0)
                    parts.Add(item);
                continue;
            }

            // "key: value" or "key:" (block list follows)
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var key = line[..colon].Trim();
                var val = CleanScalar(line[(colon + 1)..]);
                parts.Add(val.Length > 0 ? $"{key}: {val}" : key);
            }
            else
            {
                parts.Add(CleanScalar(line));
            }
        }
        return string.Join(" | ", parts.Where(p => p.Length > 0));
    }

    private static string CleanScalar(string s)
    {
        s = s.Trim();
        // strip surrounding quotes
        if (s.Length >= 2 && ((s[0] == '\'' && s[^1] == '\'') || (s[0] == '"' && s[^1] == '"')))
            s = s[1..^1];
        // unwrap [[wiki link]]
        s = s.Replace("[[", "").Replace("]]", "");
        return s.Trim();
    }

    private static int CountLines(string s)
    {
        var n = 0;
        foreach (var c in s)
            if (c == '\n') n++;
        return n;
    }
}
