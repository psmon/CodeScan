using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

internal sealed partial class CSharpAnalyzer : ILanguageAnalyzer
{
    public string Language => "csharp";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs"
    };

    public List<MethodEntry> ExtractMethods(string[] lines)
    {
        var methods = new List<MethodEntry>();

        WalkBraceLanguage(
            lines,
            classPattern: ClassDecl(),
            isCommentLine: line => line.StartsWith("//") || line.StartsWith("#") || line.StartsWith("*"),
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
            classPattern: ClassWithBase(),
            isCommentLine: line => line.StartsWith("//"),
            onClassEnter: (className, lineNumber, line) =>
            {
                var m = ClassWithBase().Match(line);
                if (!m.Success) return;
                var baseList = m.Groups.Count > 2 ? m.Groups[2].Value : "";
                foreach (var target in SplitTypeList(baseList))
                    list.Add(Dep(Language, "class", className, "inherits_or_implements", "type", target, lineNumber, "class declaration"));
            },
            onLine: (currentClass, lineNumber, line) =>
            {
                if (ImportPattern().Match(line) is { Success: true } im)
                    list.Add(Dep(Language, "file", file.Name, "imports", "module", im.Groups[1].Value, lineNumber, "import"));

                // Skip creates/uses on the class declaration line itself.
                if (ClassWithBase().IsMatch(line)) return;

                foreach (Match c in NewObject().Matches(line))
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

    // === Method extraction ===
    [GeneratedRegex(@"(?:class|struct|record|interface)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"(?:public|private|protected|internal|static|virtual|override|abstract|async|sealed|partial|\s)+\s+\S+\s+(\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodDecl();

    [GeneratedRegex(@"^\s*(?:if|else|for|foreach|while|switch|lock|using|catch|finally|do)\s*[\(\{]?", RegexOptions.Compiled)]
    private static partial Regex ControlFlow();

    // === Dependency extraction ===
    [GeneratedRegex(@"(?:class|struct|record|interface)\s+(\w+)(?:\s*:\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex ClassWithBase();

    [GeneratedRegex(@"^\s*using\s+([\w\.]+)\s*;", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"\bnew\s+([A-Z]\w*)\s*(?:<|\()", RegexOptions.Compiled)]
    private static partial Regex NewObject();

    [GeneratedRegex(@"\b([A-Z]\w*(?:<[^>]+>)?)\s+\w+\s*(?:[;=,\)])", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}
