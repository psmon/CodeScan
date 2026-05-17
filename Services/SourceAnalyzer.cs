using CodeScan.Models;
using CodeScan.Services.Analyzers;

namespace CodeScan.Services;

/// <summary>
/// Thin facade over <see cref="LanguageAnalyzerRegistry"/>. Older call sites and
/// the test suite reach through this class — keep its public surface stable.
/// </summary>
public static class SourceAnalyzer
{
    public static bool IsSourceFile(string extension) =>
        LanguageAnalyzerRegistry.IsSourceExtension(extension);

    public static List<MethodEntry> ExtractMethods(string filePath)
    {
        string[] lines;
        try { lines = File.ReadAllLines(filePath); }
        catch { return []; }
        return ExtractMethods(lines, Path.GetExtension(filePath));
    }

    public static List<MethodEntry> ExtractMethods(string[] lines, string extension) =>
        LanguageAnalyzerRegistry.Find(extension)?.ExtractMethods(lines) ?? [];
}
