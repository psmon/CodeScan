using System.Text.RegularExpressions;
using CodeScan.Models;

namespace CodeScan.Services.Analyzers;

/// <summary>
/// Stateless helpers shared by all <see cref="ILanguageAnalyzer"/> implementations.
/// Anything that depends on language-specific syntax stays in the analyzer itself.
/// </summary>
internal static class AnalyzerHelpers
{
    private static readonly HashSet<string> PrimitiveLikeNames = new(StringComparer.Ordinal)
    {
        "string", "int", "long", "bool", "double", "float", "decimal",
        "void", "var", "object", "byte", "short", "char", "uint", "ulong"
    };

    public static int CountChar(string s, char c)
    {
        var count = 0;
        foreach (var ch in s) if (ch == c) count++;
        return count;
    }

    public static int FindBraceEnd(string[] lines, int startIndex)
    {
        var depth = 0;
        var foundOpen = false;

        for (var i = startIndex; i < lines.Length; i++)
        {
            foreach (var ch in lines[i])
            {
                switch (ch)
                {
                    case '{': depth++; foundOpen = true; break;
                    case '}': depth--; break;
                }
                if (foundOpen && depth == 0) return i + 1;
            }
        }

        // expression-bodied / arrow: find semicolon
        for (var i = startIndex; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd().EndsWith(';'))
                return i + 1;
        }

        return startIndex + 1;
    }

    public static int FindIndentBlockEnd(string[] lines, int startIndex, int defIndent)
    {
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var currentIndent = line.Length - line.TrimStart().Length;
            if (currentIndent <= defIndent) return i;
        }
        return lines.Length;
    }

    public static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    public static string StripPythonComment(string line)
    {
        var idx = line.IndexOf('#');
        return idx >= 0 ? line[..idx] : line;
    }

    public static string NormalizeTypeName(string value)
    {
        var text = value.Trim();
        var generic = text.IndexOfAny(['<', '(', '?', '[']);
        if (generic >= 0) text = text[..generic];
        text = text.Trim().TrimStart('\\').TrimEnd(',', ';', ' ', '\t');
        return text.Split('.').Last().Split(':').Last().Trim();
    }

    public static bool IsLikelyType(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (PrimitiveLikeNames.Contains(value)) return false;
        return char.IsUpper(value[0])
            || (value.StartsWith('I') && value.Length > 1 && char.IsUpper(value[1]));
    }

    public static string FirstSuccessfulGroup(Match match, params int[] groupIndexes)
    {
        foreach (var index in groupIndexes)
        {
            if (index < match.Groups.Count && match.Groups[index].Success)
                return match.Groups[index].Value;
        }
        return "";
    }

    public static IEnumerable<string> SplitTypeList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;

        foreach (var part in value.Split([',', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = NormalizeTypeName(part);
            if (IsLikelyType(normalized))
                yield return normalized;
        }
    }

    public static SourceDependency Dep(
        string strategy,
        string fromKind, string fromName,
        string edgeKind,
        string toKind, string toName,
        int line, string detail) => new()
    {
        FromKind = fromKind,
        FromName = fromName,
        EdgeKind = edgeKind,
        ToKind = toKind,
        ToName = toKind == "module" ? toName.Trim() : NormalizeTypeName(toName),
        Strategy = strategy,
        Detail = detail,
        Line = line
    };

    /// <summary>
    /// Walks brace-delimited source line-by-line, tracking the current enclosing class.
    /// Supports both K&amp;R (<c>class X {</c>) and Allman (<c>class X</c> then <c>{</c>) styles
    /// by delaying class activation until the first <c>{</c> appears after the declaration.
    /// </summary>
    public static void WalkBraceLanguage(
        string[] lines,
        Regex classPattern,
        Func<string, bool> isCommentLine,
        Action<string, int, string> onClassEnter,
        Action<string, int, string> onLine)
    {
        var classStack = new Stack<string>();
        var classBraceDepth = new Stack<int>();
        var braceDepth = 0;
        string? pendingClassName = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0 || isCommentLine(line)) continue;

            var cm = classPattern.Match(line);
            if (cm.Success)
            {
                pendingClassName = cm.Groups[1].Value;
                onClassEnter(pendingClassName, i + 1, line);
            }

            var opens = CountChar(line, '{');
            var closes = CountChar(line, '}');

            if (pendingClassName is not null && opens > 0)
            {
                classStack.Push(pendingClassName);
                classBraceDepth.Push(braceDepth);
                pendingClassName = null;
            }

            var currentClass = classStack.Count > 0 ? classStack.Peek() : "Global";
            onLine(currentClass, i + 1, line);

            braceDepth += opens - closes;

            while (classBraceDepth.Count > 0 && braceDepth <= classBraceDepth.Peek())
            {
                classBraceDepth.Pop();
                classStack.Pop();
            }
        }
    }
}
