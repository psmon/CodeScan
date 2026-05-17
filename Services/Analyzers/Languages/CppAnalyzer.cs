using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

internal sealed partial class CppAnalyzer : ILanguageAnalyzer
{
    public string Language => "cpp";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cxx",
        ".h", ".hpp", ".hh", ".hxx"
    };

    public List<MethodEntry> ExtractMethods(string[] _) => [];

    public List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();

        WalkBraceLanguage(
            lines,
            classPattern: ClassDecl(),
            isCommentLine: line => line.StartsWith("//"),
            onClassEnter: (className, lineNumber, line) =>
            {
                var m = ClassWithBase().Match(line);
                if (!m.Success || !m.Groups[2].Success) return;
                foreach (var target in SplitTypeList(m.Groups[2].Value))
                    list.Add(Dep(Language, "class", className, "inherits_or_implements", "type", target, lineNumber, "class declaration"));
            },
            onLine: (currentClass, lineNumber, line) =>
            {
                if (ImportPattern().Match(line) is { Success: true } im)
                    list.Add(Dep(Language, "file", file.Name, "imports", "module", im.Groups[1].Value, lineNumber, "import"));

                if (ClassWithBase().IsMatch(line)) return;

                foreach (Match c in CreateCall().Matches(line))
                {
                    var target = NormalizeTypeName(FirstSuccessfulGroup(c, 1, 2, 3));
                    if (!IsLikelyType(target)) continue;
                    if (string.Equals(target, currentClass, StringComparison.Ordinal)) continue;
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

    [GeneratedRegex(@"(?:class|struct)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"(?:class|struct)\s+(\w+)(?:\s*:\s*(?:public|private|protected)?\s*([^{]+))?", RegexOptions.Compiled)]
    private static partial Regex ClassWithBase();

    [GeneratedRegex(@"^\s*#include\s+[<""]([^>""]+)[>""]", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    // Three modern-C++ creation shapes:
    //   1. `new TypeName(...)` / `new TypeName<...>(...)`
    //   2. `std::make_unique<...::TypeName>(...)` (also `make_shared`)
    //   3. `std::make_unique<TypeName>(...)` (unqualified case for groups[3] alt)
    [GeneratedRegex(@"\bnew\s+([A-Z]\w*)\s*(?:<|\()|\b(?:std::)?make_(?:unique|shared)\s*<\s*(?:[\w:]+::)?([A-Z]\w*)\s*>|\b(?:std::)?make_(?:unique|shared)\s*<\s*([A-Z]\w*)\s*>", RegexOptions.Compiled)]
    private static partial Regex CreateCall();

    [GeneratedRegex(@"\b([A-Z]\w*(?:::\w+)?(?:<[^>]+>)?)\s+[\*&]?\w+\s*(?:[;=,\)])", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}
