using CodeScan.Services.Analyzers.Languages;

namespace CodeScan.Services.Analyzers;

/// <summary>
/// Resolves file extensions to language analyzers. Initialization is a single
/// switch expression so every language lives in exactly one place. Adding a new
/// language means writing an <see cref="ILanguageAnalyzer"/> and adding a case
/// here — no other file changes are required.
/// </summary>
public static class LanguageAnalyzerRegistry
{
    private static readonly ILanguageAnalyzer[] _all =
    [
        new CSharpAnalyzer(),
        new JavaAnalyzer(),
        new KotlinAnalyzer(),
        new JsTsAnalyzer(),
        new PhpAnalyzer(),
        new PythonAnalyzer(),
        new GoAnalyzer(),
        new RustAnalyzer(),
        new CppAnalyzer()
    ];

    private static readonly Dictionary<string, ILanguageAnalyzer> _byExtension =
        BuildExtensionIndex(_all);

    public static IReadOnlyList<ILanguageAnalyzer> All => _all;

    public static ILanguageAnalyzer? Find(string extension) =>
        _byExtension.GetValueOrDefault(extension.ToLowerInvariant());

    public static bool IsSourceExtension(string extension) =>
        _byExtension.ContainsKey(extension.ToLowerInvariant());

    private static Dictionary<string, ILanguageAnalyzer> BuildExtensionIndex(ILanguageAnalyzer[] analyzers)
    {
        var map = new Dictionary<string, ILanguageAnalyzer>(StringComparer.OrdinalIgnoreCase);
        foreach (var analyzer in analyzers)
        {
            foreach (var ext in analyzer.SupportedExtensions)
                map[ext] = analyzer;
        }
        return map;
    }
}
