using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

/// <summary>
/// Rust's relevant edges live in three places we don't see in the C-family
/// languages: <c>impl Trait for Struct</c> (the only explicit trait link),
/// grouped <c>use crate::x::{A, B}</c> imports, and the strong tendency to
/// instantiate stdlib wrappers (<c>Box::new</c>, <c>Vec::new</c>) that would
/// otherwise pollute the <c>creates</c> edges.
/// </summary>
internal sealed partial class RustAnalyzer : ILanguageAnalyzer
{
    public string Language => "rust";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".rs"
    };

    private static readonly HashSet<string> StdLibWrappers = new(StringComparer.Ordinal)
    {
        "Box", "Vec", "Option", "Result", "String", "HashMap", "BTreeMap", "Arc", "Rc", "Cell", "RefCell",
        // `Self` is the implicit self-type alias inside impl blocks — not a real edge target.
        "Self"
    };

    public List<MethodEntry> ExtractMethods(string[] _) => [];

    public List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();
        var currentType = "Global";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripLineComment(lines[i]).Trim();
            if (line.Length == 0) continue;

            // `use crate::x::{A, B};` and `use crate::x::A;` go through one regex
            // with two alternatives; the grouped form expands into one edge per item.
            if (ImportPattern().Match(line) is { Success: true } im)
            {
                if (im.Groups[1].Success && im.Groups[2].Success)
                {
                    var prefix = im.Groups[1].Value;
                    foreach (var part in im.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = part.Trim();
                        if (name.Length == 0 || name == "self") continue;
                        list.Add(Dep(Language, "file", file.Name, "imports", "module", $"{prefix}::{name}", i + 1, "import"));
                    }
                }
                else if (im.Groups[3].Success)
                {
                    list.Add(Dep(Language, "file", file.Name, "imports", "module", im.Groups[3].Value, i + 1, "import"));
                }
                continue;
            }

            if (ImplFor().Match(line) is { Success: true } iff)
            {
                var trait = iff.Groups[1].Value;
                currentType = iff.Groups[2].Value;
                list.Add(Dep(Language, "class", currentType, "inherits_or_implements", "type", trait, i + 1, "impl trait for type"));
                continue;
            }

            if (ImplOnly().Match(line) is { Success: true } impl)
            {
                currentType = impl.Groups[1].Value;
                continue;
            }

            if (TypeDecl().Match(line) is { Success: true } td)
            {
                currentType = td.Groups[1].Value;
                continue;
            }

            foreach (Match created in NewObject().Matches(line))
            {
                var target = NormalizeTypeName(FirstSuccessfulGroup(created, 1, 2, 3));
                if (!IsLikelyType(target)) continue;
                if (StdLibWrappers.Contains(target)) continue;
                if (string.Equals(target, currentType, StringComparison.Ordinal)) continue;
                list.Add(Dep(Language, "class", currentType, "creates", "type", target, i + 1, "object creation"));
            }

            foreach (Match t in TypeUse().Matches(line))
            {
                var typeName = NormalizeTypeName(t.Groups[1].Value);
                if (!IsLikelyType(typeName)) continue;
                if (StdLibWrappers.Contains(typeName)) continue;
                if (string.Equals(typeName, currentType, StringComparison.Ordinal)) continue;
                list.Add(Dep(Language, "class", currentType, "uses_type", "type", typeName, i + 1, "type reference"));
            }
        }

        return list;
    }

    [GeneratedRegex(@"(?:struct|enum|trait)\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex TypeDecl();

    // Match either grouped (`prefix::{A, B}`) or single (`path::Name`) imports.
    [GeneratedRegex(@"^\s*use\s+(?:([\w:]+?)::\{([^}]+)\}|((?:\w+::)*\w+))\s*;?", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"^\s*impl(?:\s*<[^>]+>)?\s+(\w+)(?:<[^>]+>)?\s+for\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ImplFor();

    [GeneratedRegex(@"^\s*impl(?:\s*<[^>]+>)?\s+(\w+)(?:<[^>]+>)?\s*\{", RegexOptions.Compiled)]
    private static partial Regex ImplOnly();

    // `Foo::new(`, `Foo::default(`, or `Foo { ... }` literal.
    [GeneratedRegex(@"\b([A-Z]\w*)::(?:new|default)\s*\(|\b([A-Z]\w*)\s*\{|^\s*([A-Z]\w*)::new\b", RegexOptions.Compiled)]
    private static partial Regex NewObject();

    [GeneratedRegex(@"\b(?:let\s+\w+\s*:\s*|->\s*|Box<|Vec<|impl\s+)([A-Z]\w*)", RegexOptions.Compiled)]
    private static partial Regex TypeUse();
}
