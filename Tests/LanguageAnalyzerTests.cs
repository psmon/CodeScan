using CodeScan.Models;
using CodeScan.Services.Analyzers;
using CodeScan.Services.Analyzers.Languages;

namespace CodeScan.Tests;

/// <summary>
/// Regression tests for the per-language analyzer refactor (TestSample e2e
/// findings). Each case mirrors a specific bug we found while scanning the
/// TestSample multi-language samples.
/// </summary>
public class LanguageAnalyzerTests
{
    // -------- C# Allman style (class brace on next line) --------

    [Fact]
    public void CSharp_AllmanStyle_BindsMethodsToClass()
    {
        var lines = new[]
        {
            "namespace HelloWorld.Speakers;",
            "",
            "public sealed class EnSpeaker : Person",
            "{",
            "    public EnSpeaker(string name) : base(name, \"en\") { }",
            "",
            "    public override string Speak() => \"Hello, World!\";",
            "}"
        };
        var methods = new CSharpAnalyzer().ExtractMethods(lines);
        Assert.Contains(methods, m => m.MethodName == "Speak");
        Assert.All(methods, m => Assert.Equal("EnSpeaker", m.ClassName));
    }

    [Fact]
    public void CSharp_KandRStyle_StillWorks()
    {
        var lines = new[]
        {
            "public class Service {",
            "    public void Run() {}",
            "}"
        };
        var methods = new CSharpAnalyzer().ExtractMethods(lines);
        Assert.Single(methods);
        Assert.Equal("Service", methods[0].ClassName);
    }

    // -------- JS/TS — super() / reserved keywords are not methods --------

    [Fact]
    public void JsTs_SuperInsideConstructor_NotMethod()
    {
        var lines = new[]
        {
            "export class EnSpeaker extends Person {",
            "    constructor(name) {",
            "        super(name, \"en\");",
            "    }",
            "    speak() { return \"Hello\"; }",
            "}"
        };
        var methods = new JsTsAnalyzer().ExtractMethods(lines);
        Assert.DoesNotContain(methods, m => m.MethodName == "super");
        Assert.Contains(methods, m => m.MethodName == "speak");
        Assert.Contains(methods, m => m.MethodName == "constructor");
    }

    [Fact]
    public void JsTs_ReturnAndThrow_NotMethods()
    {
        var lines = new[]
        {
            "class Person {",
            "    speak() {",
            "        throw new Error(\"override me\");",
            "        return notUsed();",
            "    }",
            "}"
        };
        var methods = new JsTsAnalyzer().ExtractMethods(lines);
        Assert.DoesNotContain(methods, m => m.MethodName is "throw" or "return");
    }

    // -------- Kotlin — primary constructor + inheritance --------

    [Fact]
    public void Kotlin_PrimaryConstructor_DoesNotPolluteInheritance()
    {
        var file = StubFile("EnSpeaker.kt");
        var lines = new[]
        {
            "package helloworld.speakers",
            "import helloworld.Person",
            "class EnSpeaker(name: String) : Person(name, \"en\") {",
            "    override fun speak(): String = \"Hello\"",
            "}"
        };

        var deps = new KotlinAnalyzer().ExtractDependencies(file, lines);
        var inherits = deps.Where(d => d.EdgeKind == "inherits_or_implements")
                           .Select(d => d.ToName)
                           .ToList();

        Assert.Contains("Person", inherits);
        Assert.DoesNotContain("String", inherits);
        Assert.DoesNotContain("String)", inherits);
    }

    [Fact]
    public void Kotlin_CreatesCall_DetectedWithoutNewKeyword()
    {
        var file = StubFile("Main.kt");
        var lines = new[]
        {
            "fun main() {",
            "    val world = World()",
            "    world.add(EnSpeaker(\"Alice\"))",
            "}"
        };
        var deps = new KotlinAnalyzer().ExtractDependencies(file, lines);
        var creates = deps.Where(d => d.EdgeKind == "creates").Select(d => d.ToName).ToList();

        Assert.Contains("World", creates);
        Assert.Contains("EnSpeaker", creates);
    }

    // -------- Go — embedded struct as inheritance + no self-creates --------

    [Fact]
    public void Go_EmbeddedStruct_BecomesInheritsEdge()
    {
        var file = StubFile("en.go");
        var lines = new[]
        {
            "package speakers",
            "",
            "import \"helloworld/person\"",
            "",
            "type EnSpeaker struct {",
            "    person.Base",
            "}",
            "",
            "func NewEnSpeaker(name string) *EnSpeaker {",
            "    return &EnSpeaker{Base: person.NewBase(name, \"en\")}",
            "}"
        };

        var deps = new GoAnalyzer().ExtractDependencies(file, lines);
        var inherits = deps.Where(d => d.EdgeKind == "inherits_or_implements"
                                    && d.FromName == "EnSpeaker")
                           .Select(d => d.ToName)
                           .ToList();
        Assert.Contains("Base", inherits);
    }

    [Fact]
    public void Go_DoesNotEmitSelfReferenceOnCompositeLiteral()
    {
        var file = StubFile("en.go");
        var lines = new[]
        {
            "package speakers",
            "type EnSpeaker struct {",
            "    Base",
            "}",
            "func NewEnSpeaker() *EnSpeaker {",
            "    return &EnSpeaker{}",
            "}"
        };

        var deps = new GoAnalyzer().ExtractDependencies(file, lines);
        var selfCreates = deps.Where(d => d.EdgeKind == "creates"
                                       && d.FromName == "EnSpeaker"
                                       && d.ToName == "EnSpeaker")
                              .ToList();
        Assert.Empty(selfCreates);
    }

    // -------- Rust — impl Trait for Struct + group imports --------

    [Fact]
    public void Rust_ImplTraitForStruct_BecomesInheritsEdge()
    {
        var file = StubFile("en_speaker.rs");
        var lines = new[]
        {
            "use crate::person::{Person, PersonBase};",
            "pub struct EnSpeaker { base: PersonBase }",
            "impl Person for EnSpeaker {",
            "    fn speak(&self) -> String { String::from(\"Hello\") }",
            "}"
        };
        var deps = new RustAnalyzer().ExtractDependencies(file, lines);

        Assert.Contains(deps, d =>
            d.EdgeKind == "inherits_or_implements" &&
            d.FromName == "EnSpeaker" &&
            d.ToName == "Person");
    }

    [Fact]
    public void Rust_GroupImport_ExpandsToOneEdgePerItem()
    {
        var file = StubFile("world.rs");
        var lines = new[]
        {
            "use crate::person::{Person, PersonBase};",
            "pub struct World;"
        };

        var deps = new RustAnalyzer().ExtractDependencies(file, lines);
        var imports = deps.Where(d => d.EdgeKind == "imports").Select(d => d.ToName).ToList();

        Assert.Contains("crate::person::Person", imports);
        Assert.Contains("crate::person::PersonBase", imports);
        Assert.DoesNotContain(imports, name => name.EndsWith("::"));
    }

    // -------- C++ — modern smart pointer creation --------

    [Fact]
    public void Cpp_MakeUnique_CountsAsCreate()
    {
        var file = StubFile("main.cpp");
        var lines = new[]
        {
            "#include \"World.h\"",
            "#include \"speakers/EnSpeaker.h\"",
            "int main() {",
            "    helloworld::World world;",
            "    world.add(std::make_unique<helloworld::speakers::EnSpeaker>(\"Alice\"));",
            "    auto shared = std::make_shared<KoSpeaker>(\"진수\");",
            "    auto raw = new JaSpeaker(\"ハナコ\");",
            "    return 0;",
            "}"
        };
        var deps = new CppAnalyzer().ExtractDependencies(file, lines);
        var creates = deps.Where(d => d.EdgeKind == "creates").Select(d => d.ToName).ToList();

        Assert.Contains("EnSpeaker", creates);
        Assert.Contains("KoSpeaker", creates);
        Assert.Contains("JaSpeaker", creates);
    }

    // -------- Registry — every supported extension resolves --------

    [Theory]
    [InlineData(".cs", "csharp")]
    [InlineData(".java", "java")]
    [InlineData(".kt", "kotlin")]
    [InlineData(".kts", "kotlin")]
    [InlineData(".js", "javascript")]
    [InlineData(".ts", "javascript")]
    [InlineData(".php", "php")]
    [InlineData(".py", "python")]
    [InlineData(".go", "go")]
    [InlineData(".rs", "rust")]
    [InlineData(".cpp", "cpp")]
    [InlineData(".h", "cpp")]
    public void Registry_ResolvesExtensionToLanguage(string ext, string language)
    {
        var analyzer = LanguageAnalyzerRegistry.Find(ext);
        Assert.NotNull(analyzer);
        Assert.Equal(language, analyzer!.Language);
    }

    private static FileEntry StubFile(string name) => new()
    {
        FullPath = name,
        RelativePath = name,
        Name = name,
        Size = 0,
        IsDirectory = false,
        Depth = 0
    };
}
