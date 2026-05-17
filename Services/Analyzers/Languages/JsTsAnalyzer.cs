using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

internal sealed partial class JsTsAnalyzer : ILanguageAnalyzer
{
    public string Language => "javascript";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx"
    };

    public List<MethodEntry> ExtractMethods(string[] lines)
    {
        var methods = new List<MethodEntry>();

        WalkBraceLanguage(
            lines,
            classPattern: ClassDecl(),
            isCommentLine: line => line.StartsWith("//") || line.StartsWith("*"),
            onClassEnter: (_, _, _) => { },
            onLine: (currentClass, lineNumber, line) =>
            {
                // Try multiple JS/TS member shapes in turn.
                string? methodName = null;

                if (ClassMember().Match(line) is { Success: true } m1
                    && !ControlFlow().IsMatch(line)
                    && !ReservedCall().IsMatch(line))
                {
                    methodName = m1.Groups[1].Value;
                }
                else if (FunctionDecl().Match(line) is { Success: true } m2)
                {
                    methodName = m2.Groups[1].Value;
                }
                else if (ArrowOrFnAssignment().Match(line) is { Success: true } m3)
                {
                    methodName = m3.Groups[1].Success ? m3.Groups[1].Value
                               : m3.Groups[2].Success ? m3.Groups[2].Value : null;
                }
                else if (ExportFunction().Match(line) is { Success: true } m4)
                {
                    methodName = m4.Groups[1].Value;
                }

                if (methodName is null) return;

                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = methodName,
                    StartLine = lineNumber,
                    EndLine = FindBraceEnd(lines, lineNumber - 1)
                });
            });

        return methods;
    }

    public List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();

        WalkBraceLanguage(
            lines,
            classPattern: ClassWithBase(),
            isCommentLine: line => line.StartsWith("//"),
            onClassEnter: (className, lineNumber, line) =>
            {
                var m = ClassWithBase().Match(line);
                var baseList = m.Groups.Count > 2 ? m.Groups[2].Value : "";
                foreach (var target in SplitTypeList(baseList))
                    list.Add(Dep(Language, "class", className, "inherits_or_implements", "type", target, lineNumber, "class declaration"));
            },
            onLine: (currentClass, lineNumber, line) =>
            {
                if (ImportPattern().Match(line) is { Success: true } im)
                {
                    var module = FirstSuccessfulGroup(im, 1, 2);
                    list.Add(Dep(Language, "file", file.Name, "imports", "module", module, lineNumber, "import"));
                }

                if (ClassWithBase().IsMatch(line)) return;

                foreach (Match c in NewObject().Matches(line))
                {
                    var target = NormalizeTypeName(c.Groups[1].Value);
                    if (!string.Equals(target, currentClass, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentClass, "creates", "type", target, lineNumber, "object creation"));
                }

                foreach (Match t in TypeUse().Matches(line))
                {
                    var typeName = NormalizeTypeName(FirstSuccessfulGroup(t, 1, 2));
                    if (IsLikelyType(typeName) && !string.Equals(typeName, currentClass, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentClass, "uses_type", "type", typeName, lineNumber, "type reference"));
                }
            });

        return list;
    }

    [GeneratedRegex(@"class\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassDecl();

    // Class method head: optional `async` + identifier + `(`. Excludes the things
    // ReservedCall() filters out (super/return/throw/yield/new/delete/typeof/void/await/in/of).
    [GeneratedRegex(@"^\s*(?:async\s+)?(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex ClassMember();

    [GeneratedRegex(@"function\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex FunctionDecl();

    [GeneratedRegex(@"(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[^=])\s*=>|(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?function", RegexOptions.Compiled)]
    private static partial Regex ArrowOrFnAssignment();

    [GeneratedRegex(@"export\s+(?:default\s+)?(?:async\s+)?function\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex ExportFunction();

    [GeneratedRegex(@"^\s*(?:if|else|for|while|switch|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex ControlFlow();

    // Reserved word "calls" that look like methods but are not.
    [GeneratedRegex(@"^\s*(?:super|return|throw|yield|await|new|delete|typeof|void|in|of)\s*\(", RegexOptions.Compiled)]
    private static partial Regex ReservedCall();

    [GeneratedRegex(@"class\s+(\w+)(?:\s+(?:extends|implements)\s+([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex ClassWithBase();

    [GeneratedRegex(@"^\s*import\s+.*?\s+from\s+['""]([^'""]+)['""]|^\s*import\s+['""]([^'""]+)['""]", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"\bnew\s+([A-Z]\w*)\s*(?:<|\()", RegexOptions.Compiled)]
    private static partial Regex NewObject();

    [GeneratedRegex(@"\b([A-Z]\w*)\s*[;=,\)]|\b:\s*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}
