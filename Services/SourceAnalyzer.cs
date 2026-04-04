using System.Text.RegularExpressions;
using CodeScan.Models;

namespace CodeScan.Services;

public static partial class SourceAnalyzer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",                          // C#
        ".java", ".kt", ".kts",        // Java, Kotlin
        ".js", ".ts", ".tsx", ".jsx",   // JavaScript, TypeScript
        ".php",                         // PHP
        ".py",                          // Python
    };

    public static bool IsSourceFile(string extension)
        => SupportedExtensions.Contains(extension);

    public static List<MethodEntry> ExtractMethods(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string[] lines;
        try { lines = File.ReadAllLines(filePath); }
        catch { return []; }
        return ExtractMethods(lines, ext);
    }

    public static List<MethodEntry> ExtractMethods(string[] lines, string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext switch
        {
            ".cs" => ExtractWithBraces(lines, CsClassPattern(), CsMethodPattern(), CsControlFlow()),
            ".java" => ExtractWithBraces(lines, JavaClassPattern(), JavaMethodPattern(), JavaControlFlow()),
            ".kt" or ".kts" => ExtractWithBraces(lines, KtClassPattern(), KtMethodPattern(), KtControlFlow()),
            ".js" or ".jsx" => ExtractJsTsMethods(lines),
            ".ts" or ".tsx" => ExtractJsTsMethods(lines),
            ".php" => ExtractWithBraces(lines, PhpClassPattern(), PhpMethodPattern(), PhpControlFlow()),
            ".py" => ExtractPythonMethods(lines),
            _ => []
        };
    }

    // ============================
    // Brace-based languages (C#, Java, Kotlin, PHP)
    // ============================
    private static List<MethodEntry> ExtractWithBraces(
        string[] lines, Regex classPattern, Regex methodPattern, Regex controlFlow)
    {
        var methods = new List<MethodEntry>();

        var currentClass = "Global";
        var classStack = new Stack<string>();
        var braceDepth = 0;
        var classBraceDepth = new Stack<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#") || line.StartsWith("*"))
                continue;

            var classMatch = classPattern.Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
                classStack.Push(currentClass);
                classBraceDepth.Push(braceDepth);
            }

            var methodMatch = methodPattern.Match(line);
            if (methodMatch.Success && !controlFlow.IsMatch(line))
            {
                var methodName = methodMatch.Groups[1].Value;
                var startLine = i + 1;
                var endLine = FindBraceEnd(lines, i);

                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = methodName,
                    StartLine = startLine,
                    EndLine = endLine
                });
            }

            braceDepth += CountChar(line, '{') - CountChar(line, '}');

            if (classBraceDepth.Count > 0 && braceDepth <= classBraceDepth.Peek())
            {
                classBraceDepth.Pop();
                classStack.Pop();
                currentClass = classStack.Count > 0 ? classStack.Peek() : "Global";
            }
        }

        return methods;
    }

    // ============================
    // JavaScript / TypeScript
    // ============================
    private static List<MethodEntry> ExtractJsTsMethods(string[] lines)
    {
        var methods = new List<MethodEntry>();

        var currentClass = "Global";
        var classStack = new Stack<string>();
        var braceDepth = 0;
        var classBraceDepth = new Stack<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("*"))
                continue;

            // class detection
            var classMatch = JsClassPattern().Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
                classStack.Push(currentClass);
                classBraceDepth.Push(braceDepth);
            }

            // Try multiple JS/TS patterns
            string? methodName = null;

            // 1. class method: methodName(...) {  or  async methodName(...)
            var m1 = JsClassMethodPattern().Match(line);
            if (m1.Success && !JsControlFlow().IsMatch(line))
                methodName = m1.Groups[1].Value;

            // 2. function declaration: function name(...)
            if (methodName == null)
            {
                var m2 = JsFunctionPattern().Match(line);
                if (m2.Success)
                    methodName = m2.Groups[1].Value;
            }

            // 3. arrow/const: const name = (...) => or const name = function
            if (methodName == null)
            {
                var m3 = JsArrowPattern().Match(line);
                if (m3.Success)
                {
                    // Group 1 = arrow, Group 2 = function assignment
                    methodName = m3.Groups[1].Success ? m3.Groups[1].Value
                               : m3.Groups[2].Success ? m3.Groups[2].Value : null;
                }
            }

            // 4. export function / export default function
            if (methodName == null)
            {
                var m4 = JsExportFunctionPattern().Match(line);
                if (m4.Success)
                    methodName = m4.Groups[1].Value;
            }

            if (methodName != null)
            {
                var startLine = i + 1;
                var endLine = FindBraceEnd(lines, i);
                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = methodName,
                    StartLine = startLine,
                    EndLine = endLine
                });
            }

            braceDepth += CountChar(line, '{') - CountChar(line, '}');

            if (classBraceDepth.Count > 0 && braceDepth <= classBraceDepth.Peek())
            {
                classBraceDepth.Pop();
                classStack.Pop();
                currentClass = classStack.Count > 0 ? classStack.Peek() : "Global";
            }
        }

        return methods;
    }

    // ============================
    // Python (indentation-based)
    // ============================
    private static List<MethodEntry> ExtractPythonMethods(string[] lines)
    {
        var methods = new List<MethodEntry>();

        var currentClass = "Global";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // class
            var classMatch = PyClassPattern().Match(trimmed);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
                continue;
            }

            // def / async def
            var methodMatch = PyMethodPattern().Match(trimmed);
            if (methodMatch.Success)
            {
                var methodName = methodMatch.Groups[1].Value;
                var startLine = i + 1;
                var indent = line.Length - trimmed.Length;
                var endLine = FindPythonEnd(lines, i, indent);

                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = methodName,
                    StartLine = startLine,
                    EndLine = endLine
                });
            }

            // Reset class if we encounter a non-indented non-class line
            if (trimmed.Length > 0 && line.Length == trimmed.Length
                && !trimmed.StartsWith("class ") && !trimmed.StartsWith("def ")
                && !trimmed.StartsWith("@") && !trimmed.StartsWith("#")
                && !trimmed.StartsWith("import") && !trimmed.StartsWith("from"))
            {
                currentClass = "Global";
            }
        }

        return methods;
    }

    private static int FindPythonEnd(string[] lines, int startIndex, int defIndent)
    {
        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var currentIndent = line.Length - line.TrimStart().Length;
            if (currentIndent <= defIndent)
                return i; // 1-based would be i, but previous line is end
        }
        return lines.Length;
    }

    // ============================
    // Shared utilities
    // ============================
    private static int FindBraceEnd(string[] lines, int startIndex)
    {
        var depth = 0;
        var foundOpen = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            foreach (var ch in lines[i])
            {
                if (ch == '{') { depth++; foundOpen = true; }
                if (ch == '}') depth--;
                if (foundOpen && depth == 0) return i + 1;
            }
        }

        // expression-bodied / arrow: find semicolon
        for (int i = startIndex; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd().EndsWith(';'))
                return i + 1;
        }

        return startIndex + 1;
    }

    private static int CountChar(string s, char c)
    {
        var count = 0;
        foreach (var ch in s) if (ch == c) count++;
        return count;
    }

    // ============================
    // C# patterns
    // ============================
    [GeneratedRegex(@"(?:class|struct|record|interface)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex CsClassPattern();

    [GeneratedRegex(@"(?:public|private|protected|internal|static|virtual|override|abstract|async|sealed|partial|\s)+\s+\S+\s+(\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled)]
    private static partial Regex CsMethodPattern();

    [GeneratedRegex(@"^\s*(?:if|else|for|foreach|while|switch|lock|using|catch|finally|do)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex CsControlFlow();

    // ============================
    // Java patterns
    // ============================
    [GeneratedRegex(@"(?:class|interface|enum)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex JavaClassPattern();

    [GeneratedRegex(@"(?:public|private|protected|static|final|abstract|synchronized|native|\s)+\s+\S+\s+(\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled)]
    private static partial Regex JavaMethodPattern();

    [GeneratedRegex(@"^\s*(?:if|else|for|while|switch|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex JavaControlFlow();

    // ============================
    // Kotlin patterns
    // ============================
    [GeneratedRegex(@"(?:class|interface|object|enum\s+class|data\s+class|sealed\s+class)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex KtClassPattern();

    [GeneratedRegex(@"(?:fun|suspend\s+fun|override\s+fun|private\s+fun|internal\s+fun|protected\s+fun)\s+(?:<[^>]*>\s+)?(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex KtMethodPattern();

    [GeneratedRegex(@"^\s*(?:if|else|for|while|when|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex KtControlFlow();

    // ============================
    // PHP patterns
    // ============================
    [GeneratedRegex(@"(?:class|interface|trait)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex PhpClassPattern();

    [GeneratedRegex(@"(?:public|private|protected|static|abstract|final|\s)*\s*function\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex PhpMethodPattern();

    [GeneratedRegex(@"^\s*(?:if|else|elseif|for|foreach|while|switch|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex PhpControlFlow();

    // ============================
    // JavaScript / TypeScript patterns
    // ============================
    [GeneratedRegex(@"class\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex JsClassPattern();

    // class method: async? methodName(
    [GeneratedRegex(@"^\s*(?:async\s+)?(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex JsClassMethodPattern();

    // function name(
    [GeneratedRegex(@"function\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex JsFunctionPattern();

    // const/let/var name = (...) => or = function
    [GeneratedRegex(@"(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[^=])\s*=>|(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?function", RegexOptions.Compiled)]
    private static partial Regex JsArrowPatternRaw();

    // export function name( or export default function name(
    [GeneratedRegex(@"export\s+(?:default\s+)?(?:async\s+)?function\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex JsExportFunctionPattern();

    [GeneratedRegex(@"^\s*(?:if|else|for|while|switch|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex JsControlFlow();

    // Wrapper to handle arrow pattern with 2 capture groups
    private static Regex JsArrowPattern() => JsArrowPatternRaw();

    // ============================
    // Python patterns
    // ============================
    [GeneratedRegex(@"^class\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex PyClassPattern();

    [GeneratedRegex(@"^(?:async\s+)?def\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex PyMethodPattern();
}
