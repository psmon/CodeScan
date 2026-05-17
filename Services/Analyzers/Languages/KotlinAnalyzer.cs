using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

internal sealed partial class KotlinAnalyzer : ILanguageAnalyzer
{
    public string Language => "kotlin";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".kt", ".kts"
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
                var mm = MethodDecl().Match(line);
                if (!mm.Success || ControlFlow().IsMatch(line)) return;

                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = mm.Groups[1].Value,
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
            classPattern: ClassDecl(),
            isCommentLine: line => line.StartsWith("//"),
            onClassEnter: (className, lineNumber, line) =>
            {
                // Kotlin: class X(arg: T) : Base(...) — consume optional primary-constructor
                // parameter list BEFORE looking for inheritance, otherwise the first `:`
                // would be the type annotation of a constructor parameter.
                var m = ClassWithBase().Match(line);
                if (!m.Success || !m.Groups[2].Success) return;

                foreach (var target in SplitTypeList(m.Groups[2].Value))
                    list.Add(Dep(Language, "class", className, "inherits_or_implements", "type", target, lineNumber, "class declaration"));
            },
            onLine: (currentClass, lineNumber, line) =>
            {
                if (ImportPattern().Match(line) is { Success: true } im)
                    list.Add(Dep(Language, "file", file.Name, "imports", "module", im.Groups[1].Value, lineNumber, "import"));

                // Skip creates/uses on declaration lines — class/fun/object.
                if (ClassDecl().IsMatch(line)) return;
                if (FunctionDecl().IsMatch(line)) return;

                // Kotlin has no `new` keyword: any `Type(...)` call is a construction.
                foreach (Match c in CreateCall().Matches(line))
                {
                    var target = NormalizeTypeName(c.Groups[1].Value);
                    if (!string.Equals(target, currentClass, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentClass, "creates", "type", target, lineNumber, "object creation"));
                }

                foreach (Match t in TypeUse().Matches(line))
                {
                    var typeName = NormalizeTypeName(t.Groups[1].Value);
                    if (IsLikelyType(typeName) && !string.Equals(typeName, currentClass, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentClass, "uses_type", "type", typeName, lineNumber, "type reference"));
                }
            });

        return list;
    }

    [GeneratedRegex(@"(?:class|interface|object|enum\s+class|data\s+class|sealed\s+class)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"(?:fun|suspend\s+fun|override\s+fun|private\s+fun|internal\s+fun|protected\s+fun)\s+(?:<[^>]*>\s+)?(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodDecl();

    [GeneratedRegex(@"^\s*(?:if|else|for|while|when|catch|finally|do|try)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex ControlFlow();

    // Consume optional `(constructorParams)` before looking for inheritance via `:`.
    [GeneratedRegex(@"(?:class|interface|object|enum\s+class|data\s+class|sealed\s+class)\s+(\w+)(?:\s*\([^)]*\))?(?:\s*:\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex ClassWithBase();

    [GeneratedRegex(@"^\s*(?:fun|suspend\s+fun|override\s+fun|private\s+fun|internal\s+fun|protected\s+fun)\b", RegexOptions.Compiled)]
    private static partial Regex FunctionDecl();

    [GeneratedRegex(@"^\s*import\s+([\w\.\*]+)", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"\b([A-Z]\w*)\s*\(", RegexOptions.Compiled)]
    private static partial Regex CreateCall();

    [GeneratedRegex(@"\b\w+\s*:\s*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}
