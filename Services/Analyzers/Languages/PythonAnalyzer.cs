using System.Text.RegularExpressions;
using CodeScan.Models;
using static CodeScan.Services.Analyzers.AnalyzerHelpers;

namespace CodeScan.Services.Analyzers.Languages;

internal sealed partial class PythonAnalyzer : ILanguageAnalyzer
{
    public string Language => "python";

    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".py"
    };

    public List<MethodEntry> ExtractMethods(string[] lines)
    {
        var methods = new List<MethodEntry>();
        var currentClass = "Global";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (ClassDecl().Match(trimmed) is { Success: true } cm)
            {
                currentClass = cm.Groups[1].Value;
                continue;
            }

            if (MethodDecl().Match(trimmed) is { Success: true } mm)
            {
                var indent = line.Length - trimmed.Length;
                methods.Add(new MethodEntry
                {
                    ClassName = currentClass,
                    MethodName = mm.Groups[1].Value,
                    StartLine = i + 1,
                    EndLine = FindIndentBlockEnd(lines, i, indent)
                });
                continue;
            }

            // Reset class when we hit a non-indented, non-class, non-decorator line.
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

    public List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines)
    {
        var list = new List<SourceDependency>();
        var currentClass = "Global";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripPythonComment(lines[i]).Trim();
            if (line.Length == 0) continue;

            if (ImportPattern().Match(line) is { Success: true } im)
            {
                var module = im.Groups[1].Success ? im.Groups[1].Value : im.Groups[2].Value;
                list.Add(Dep(Language, "file", file.Name, "imports", "module", module, i + 1, "import"));
            }

            if (ClassWithBase().Match(line) is { Success: true } cm)
            {
                currentClass = cm.Groups[1].Value;
                foreach (var target in SplitTypeList(cm.Groups[2].Value))
                    list.Add(Dep(Language, "class", currentClass, "inherits_or_implements", "type", target, i + 1, "class declaration"));
                continue;
            }

            foreach (Match created in CallLikeType().Matches(line))
            {
                var target = created.Groups[1].Value;
                if (!string.Equals(target, currentClass, StringComparison.Ordinal))
                    list.Add(Dep(Language, "class", currentClass, "creates", "type", target, i + 1, "constructor-like call"));
            }
        }

        return list;
    }

    [GeneratedRegex(@"^class\s+(\w+)", RegexOptions.Compiled)]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"^(?:async\s+)?def\s+(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodDecl();

    [GeneratedRegex(@"^class\s+(\w+)(?:\(([^)]*)\))?", RegexOptions.Compiled)]
    private static partial Regex ClassWithBase();

    [GeneratedRegex(@"^\s*(?:from\s+([\w\.]+)\s+import|import\s+([\w\.]+))", RegexOptions.Compiled)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"\b([A-Z]\w*)\s*\(", RegexOptions.Compiled)]
    private static partial Regex CallLikeType();
}
