using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

/// <summary>
/// Go is structurally different from the C-family languages: there is no inheritance
/// keyword, but a struct may *embed* another type, which Go treats as composition
/// with method promotion. We model that as <c>inherits_or_implements</c>. The
/// analyzer also has to suppress the obvious false positive where a constructor
/// returns its own type via composite-literal syntax (<c>return &amp;Foo{...}</c>).
/// </summary>
internal sealed partial class GoAnalyzer : ILanguageAnalyzer
{
    public string Language => "go";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".go"
    };

    public List<MethodEntry> ExtractMethods(string[] _) => [];

    public List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();
        var currentType = "Global";
        var structBodyOwner = (string?)null;
        var structBodyDepth = 0;
        var braceDepth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripLineComment(lines[i]).Trim();
            if (line.Length == 0) continue;

            var opens = CountChar(line, '{');
            var closes = CountChar(line, '}');

            if (ImportPattern().Match(line) is { Success: true } im)
                list.Add(Dep(Language, "file", file.Name, "imports", "module", im.Groups[1].Value, i + 1, "import"));

            // Inside a struct body: each bare-type line is an embedded field.
            if (structBodyOwner is not null)
            {
                if (EmbeddedField().Match(line) is { Success: true } em)
                {
                    var type = em.Groups[1].Value.Split('.').Last();
                    if (IsLikelyType(type) && !string.Equals(type, structBodyOwner, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", structBodyOwner, "inherits_or_implements", "type", type, i + 1, "embedded field"));
                }
            }
            else if (TypeDecl().Match(line) is { Success: true } td)
            {
                currentType = td.Groups[1].Value;
                if (td.Groups[2].Value == "struct" && opens > 0)
                {
                    structBodyOwner = currentType;
                    structBodyDepth = braceDepth + 1;
                }
            }
            else
            {
                foreach (Match created in NewObject().Matches(line))
                {
                    var target = NormalizeTypeName(FirstSuccessfulGroup(created, 1, 2));
                    if (!IsLikelyType(target)) continue;
                    if (string.Equals(target, currentType, StringComparison.Ordinal)) continue;
                    list.Add(Dep(Language, "class", currentType, "creates", "type", target, i + 1, "object creation"));
                }

                foreach (Match t in TypeUse().Matches(line))
                {
                    var typeName = NormalizeTypeName(t.Groups[1].Value);
                    if (IsLikelyType(typeName) && !string.Equals(typeName, currentType, StringComparison.Ordinal))
                        list.Add(Dep(Language, "class", currentType, "uses_type", "type", typeName, i + 1, "type reference"));
                }
            }

            braceDepth += opens - closes;

            if (structBodyOwner is not null && braceDepth < structBodyDepth)
                structBodyOwner = null;
        }

        return list;
    }

    [GeneratedRegex(@"type\s+(\w+)\s+(struct|interface)\b", RegexOptions.Compiled)]
    private static partial Regex TypeDecl();

    // Embedded field: just `Type` or `pkg.Type` on its own line, no field-name prefix.
    [GeneratedRegex(@"^\*?(\w+(?:\.\w+)?)\s*$", RegexOptions.Compiled)]
    private static partial Regex EmbeddedField();

    [GeneratedRegex(@"^\s*import\s+(?:\(\s*)?""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    // Capture: `new(Type)`, `make(Type)`, `&Type{`, but NOT inside struct body.
    [GeneratedRegex(@"\b(?:new|make)\s*\(\s*([A-Z]\w*)|&([A-Z]\w*)\s*\{", RegexOptions.Compiled)]
    private static partial Regex NewObject();

    [GeneratedRegex(@"\b\w+\s+[\*\[\]]*([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}
