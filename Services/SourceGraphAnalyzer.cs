using CodeScan.Models;
using CodeScan.Services.Analyzers;

namespace CodeScan.Services;

public sealed class HybridSourceGraphAnalyzer
{
    private readonly List<ISourceDependencyStrategy> _strategies;

    public HybridSourceGraphAnalyzer()
    {
        _strategies =
        [
            new SemanticProbeStrategy(),
            new RegexSourceDependencyStrategy()
        ];
    }

    public void Enrich(string rootPath, IEnumerable<FileEntry> sourceFiles)
    {
        var context = new SourceDependencyContext(rootPath, SemanticCapabilityDetector.Detect(rootPath));

        foreach (var file in sourceFiles)
        {
            var strategy = _strategies.FirstOrDefault(s => s.CanAnalyze(file, context));
            if (strategy is null) continue;

            file.Dependencies = strategy.Analyze(file, context)
                .Where(d => !string.IsNullOrWhiteSpace(d.ToName))
                .GroupBy(d => d.StableKey)
                .Select(g => g.First())
                .ToList();
        }
    }
}

public sealed class SourceDependencyContext
{
    public SourceDependencyContext(string rootPath, IReadOnlyDictionary<string, SemanticCapability> semanticCapabilities)
    {
        RootPath = rootPath;
        SemanticCapabilities = semanticCapabilities;
    }

    public string RootPath { get; }
    public IReadOnlyDictionary<string, SemanticCapability> SemanticCapabilities { get; }
}

public sealed class SemanticCapability
{
    public required string Language { get; init; }
    public required bool ProjectModelFound { get; init; }
    public required string StrategyName { get; init; }
    public string Reason { get; init; } = "";
}

public interface ISourceDependencyStrategy
{
    string Name { get; }
    bool CanAnalyze(FileEntry file, SourceDependencyContext context);
    List<SourceDependency> Analyze(FileEntry file, SourceDependencyContext context);
}

public static class SemanticCapabilityDetector
{
    public static IReadOnlyDictionary<string, SemanticCapability> Detect(string rootPath) => new Dictionary<string, SemanticCapability>(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = Capability("csharp", "Roslyn", HasAny(rootPath, "*.sln", "*.csproj"),
            "Requires a solution/project model for exact symbols."),
        ["java"] = Capability("java", "JDT/Spoon", HasAny(rootPath, "pom.xml", "build.gradle", "build.gradle.kts"),
            "Requires Maven/Gradle or a JDT project model."),
        ["typescript"] = Capability("typescript", "TypeScript Compiler API", HasAny(rootPath, "tsconfig.json", "jsconfig.json"),
            "Requires tsconfig/jsconfig for type checker context."),
        ["go"] = Capability("go", "go/packages", HasAny(rootPath, "go.mod", "go.work"),
            "Requires Go module/workspace metadata."),
        ["rust"] = Capability("rust", "rust-analyzer", HasAny(rootPath, "Cargo.toml"),
            "Requires Cargo metadata."),
        ["cpp"] = Capability("cpp", "Clang LibTooling", HasAny(rootPath, "compile_commands.json"),
            "Requires compile_commands.json for include/define flags.")
    };

    private static SemanticCapability Capability(string language, string strategy, bool found, string reason) => new()
    {
        Language = language,
        StrategyName = strategy,
        ProjectModelFound = found,
        Reason = reason
    };

    private static bool HasAny(string rootPath, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                if (Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories)
                    .Any(p => !IsUnderIgnoredDirectory(rootPath, p)))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static bool IsUnderIgnoredDirectory(string rootPath, string path)
    {
        var relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
        return relative.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "dist" or "build");
    }
}

/// <summary>
/// Placeholder selector for future compiler-backed analyzers (Roslyn, JDT/Spoon,
/// tsc, go/packages, rust-analyzer, Clang LibTooling). For now it always yields to
/// the regex strategy — replacing this stub is how language-aware semantic analysis
/// plugs into the pipeline.
/// </summary>
internal sealed class SemanticProbeStrategy : ISourceDependencyStrategy
{
    public string Name => "semantic-probe";

    public bool CanAnalyze(FileEntry file, SourceDependencyContext context)
    {
        var language = GetLanguage(file.Extension);
        if (language is null) return false;
        if (!context.SemanticCapabilities.TryGetValue(language, out var capability)) return false;
        if (!capability.ProjectModelFound) return false;
        return false;
    }

    public List<SourceDependency> Analyze(FileEntry file, SourceDependencyContext context) => [];

    private static string? GetLanguage(string extension) => extension switch
    {
        ".cs" => "csharp",
        ".java" => "java",
        ".ts" or ".tsx" or ".js" or ".jsx" => "typescript",
        ".go" => "go",
        ".rs" => "rust",
        ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hpp" => "cpp",
        _ => null
    };
}

/// <summary>
/// Delegates per file to the matching <see cref="ILanguageAnalyzer"/>. Each language's
/// rules live in <c>Services/Analyzers/Languages/</c>; this strategy is just plumbing.
/// </summary>
internal sealed class RegexSourceDependencyStrategy : ISourceDependencyStrategy
{
    public string Name => "regex";

    public bool CanAnalyze(FileEntry file, SourceDependencyContext context) =>
        LanguageAnalyzerRegistry.IsSourceExtension(file.Extension);

    public List<SourceDependency> Analyze(FileEntry file, SourceDependencyContext context)
    {
        var analyzer = LanguageAnalyzerRegistry.Find(file.Extension);
        if (analyzer is null) return [];

        string[] lines;
        try { lines = File.ReadAllLines(file.FullPath); }
        catch { return []; }

        return analyzer.ExtractDependencies(file, lines);
    }
}
