using CodeScan.Models;

namespace CodeScan.Services;

public static class CommentExtractor
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".java", ".kt", ".kts", ".js", ".ts", ".tsx", ".jsx", ".php", ".py"
    };

    public static bool IsSupported(string extension) => Supported.Contains(extension);

    public static List<CommentBlock> Extract(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string[] lines;
        try { lines = File.ReadAllLines(filePath); }
        catch { return []; }

        return ext switch
        {
            ".py" => ExtractPython(lines),
            _ => ExtractCStyle(lines) // C#, Java, Kotlin, JS, TS, PHP all use // and /* */
        };
    }

    /// <summary>
    /// Extract // and /* */ comments with surrounding code context
    /// </summary>
    private static List<CommentBlock> ExtractCStyle(string[] lines)
    {
        var results = new List<CommentBlock>();
        var inBlock = false;
        var blockStart = 0;
        var commentLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // Block comment start
            if (!inBlock && trimmed.StartsWith("/*"))
            {
                inBlock = true;
                blockStart = i;
                commentLines.Clear();
                commentLines.Add(trimmed);

                if (trimmed.Contains("*/"))
                {
                    inBlock = false;
                    AddBlock(results, lines, commentLines, blockStart, i);
                }
                continue;
            }

            // Block comment continuation
            if (inBlock)
            {
                commentLines.Add(trimmed);
                if (trimmed.Contains("*/"))
                {
                    inBlock = false;
                    AddBlock(results, lines, commentLines, blockStart, i);
                }
                continue;
            }

            // Single-line comment (// or /// )
            if (trimmed.StartsWith("//"))
            {
                // Collect consecutive // lines
                var start = i;
                commentLines.Clear();
                while (i < lines.Length && lines[i].Trim().StartsWith("//"))
                {
                    commentLines.Add(lines[i].Trim());
                    i++;
                }
                i--; // back one since loop will increment
                AddBlock(results, lines, commentLines, start, i);
            }
        }

        return results;
    }

    /// <summary>
    /// Extract # comments and """ docstrings in Python
    /// </summary>
    private static List<CommentBlock> ExtractPython(string[] lines)
    {
        var results = new List<CommentBlock>();
        var commentLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // # comments
            if (trimmed.StartsWith('#'))
            {
                var start = i;
                commentLines.Clear();
                while (i < lines.Length && lines[i].Trim().StartsWith('#'))
                {
                    commentLines.Add(lines[i].Trim());
                    i++;
                }
                i--;
                AddBlock(results, lines, commentLines, start, i);
                continue;
            }

            // Triple-quote docstrings
            if (trimmed.StartsWith("\"\"\"") || trimmed.StartsWith("'''"))
            {
                var quote = trimmed[..3];
                var start = i;
                commentLines.Clear();
                commentLines.Add(trimmed);

                // Check if single-line docstring
                if (trimmed.Length > 3 && trimmed.EndsWith(quote) && trimmed.Length > 6)
                {
                    AddBlock(results, lines, commentLines, start, i);
                    continue;
                }

                i++;
                while (i < lines.Length)
                {
                    commentLines.Add(lines[i].Trim());
                    if (lines[i].Trim().EndsWith(quote)) break;
                    i++;
                }
                AddBlock(results, lines, commentLines, start, i);
            }
        }

        return results;
    }

    private static void AddBlock(List<CommentBlock> results, string[] lines,
        List<string> commentLines, int startLine, int endLine)
    {
        var comment = string.Join("\n", commentLines)
            .Replace("//", "").Replace("/*", "").Replace("*/", "")
            .Replace("///", "").Replace("#", "").Replace("\"\"\"", "").Replace("'''", "")
            .Trim();

        // Skip trivial comments (too short or auto-generated)
        if (comment.Length < 5) return;

        // Get nearby code context (up to 3 lines after comment)
        var contextLines = new List<string>();
        for (int j = endLine + 1; j < Math.Min(endLine + 4, lines.Length); j++)
        {
            var line = lines[j].Trim();
            if (line.Length > 0)
                contextLines.Add(line);
        }

        // Get code before comment (1 line)
        var beforeContext = "";
        if (startLine > 0)
        {
            var prev = lines[startLine - 1].Trim();
            if (prev.Length > 0 && !prev.StartsWith("//") && !prev.StartsWith("#") && !prev.StartsWith("/*"))
                beforeContext = prev;
        }

        var nearbyCode = string.Join("\n", contextLines);

        results.Add(new CommentBlock
        {
            Comment = comment,
            NearbyCode = nearbyCode,
            BeforeCode = beforeContext,
            StartLine = startLine + 1, // 1-based
            EndLine = endLine + 1
        });
    }
}
