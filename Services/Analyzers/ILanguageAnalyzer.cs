using CodeScan.Models;

namespace CodeScan.Services.Analyzers;

/// <summary>
/// One analyzer per language. Owns its regex patterns, brace/indent tracking,
/// and edge-extraction rules. Dispatch happens at <see cref="LanguageAnalyzerRegistry"/>
/// — a single static switch on the file extension. Each implementation is responsible
/// only for its own language; cross-language helpers live in <see cref="AnalyzerHelpers"/>.
/// </summary>
public interface ILanguageAnalyzer
{
    string Language { get; }

    IReadOnlySet<string> SupportedExtensions { get; }

    List<MethodEntry> ExtractMethods(string[] lines);

    List<SourceDependency> ExtractDependencies(FileEntry file, string[] lines);
}
