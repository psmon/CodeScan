using CodeScan.Services;

namespace CodeScan.Tests;

public class SourceAnalyzerTests
{
    // ========================================
    // C# patterns
    // ========================================

    [Fact]
    public void CSharp_ClassAndMethod()
    {
        var lines = new[]
        {
            "namespace MyApp;",
            "public class UserService {",
            "    public void Save(User user)",
            "    {",
            "        db.Save(user);",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal("UserService", methods[0].ClassName);
        Assert.Equal("Save", methods[0].MethodName);
    }

    [Fact]
    public void CSharp_MultipleClasses()
    {
        var lines = new[]
        {
            "public class Foo {",
            "    public int GetValue() { return 1; }",
            "}",
            "public class Bar {",
            "    public string GetName() { return \"bar\"; }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Equal(2, methods.Count);
        Assert.Equal("Foo", methods[0].ClassName);
        Assert.Equal("GetValue", methods[0].MethodName);
        Assert.Equal("Bar", methods[1].ClassName);
        Assert.Equal("GetName", methods[1].MethodName);
    }

    [Fact]
    public void CSharp_AsyncMethod()
    {
        var lines = new[]
        {
            "public class Service",
            "{",
            "    public async Task<string> FetchDataAsync()",
            "    {",
            "        return await client.GetAsync();",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal("FetchDataAsync", methods[0].MethodName);
    }

    [Fact]
    public void CSharp_StaticMethod()
    {
        var lines = new[]
        {
            "public static class Helper",
            "{",
            "    public static int Add(int a, int b)",
            "    {",
            "        return a + b;",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal("Add", methods[0].MethodName);
    }

    [Fact]
    public void CSharp_RecordAndInterface()
    {
        var lines = new[]
        {
            "public record UserDto {",
            "    public string GetFullName() { return Name; }",
            "}",
            "public interface IService {",
            "    void Execute();",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Contains(methods, m => m.ClassName == "UserDto" && m.MethodName == "GetFullName");
    }

    [Fact]
    public void CSharp_ControlFlowNotDetectedAsMethod()
    {
        var lines = new[]
        {
            "public class Logic",
            "{",
            "    public void Run()",
            "    {",
            "        if (true) { }",
            "        for (int i = 0; i < 10; i++) { }",
            "        foreach (var x in list) { }",
            "        while (true) { break; }",
            "        switch (val) { }",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal("Run", methods[0].MethodName);
    }

    [Fact]
    public void CSharp_NestedClass()
    {
        var lines = new[]
        {
            "public class Outer",
            "{",
            "    public void OuterMethod() { }",
            "    public class Inner",
            "    {",
            "        public void InnerMethod() { }",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.True(methods.Count >= 2);
        Assert.Contains(methods, m => m.MethodName == "OuterMethod");
        Assert.Contains(methods, m => m.MethodName == "InnerMethod");
    }

    [Fact]
    public void CSharp_ExpressionBodied()
    {
        var lines = new[]
        {
            "public class Calc",
            "{",
            "    public int Double(int x) => x * 2;",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal("Double", methods[0].MethodName);
    }

    // ========================================
    // Java patterns
    // ========================================

    [Fact]
    public void Java_ClassAndMethod()
    {
        var lines = new[]
        {
            "public class UserRepository {",
            "    public User findById(Long id) {",
            "        return db.find(id);",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".java");
        Assert.Single(methods);
        Assert.Equal("UserRepository", methods[0].ClassName);
        Assert.Equal("findById", methods[0].MethodName);
    }

    [Fact]
    public void Java_InterfaceAndEnum()
    {
        var lines = new[]
        {
            "public interface Processor {",
            "    void process();",
            "}",
            "public enum Status {",
            "    ACTIVE, INACTIVE;",
            "    public String label() { return name().toLowerCase(); }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".java");
        Assert.Contains(methods, m => m.ClassName == "Status" && m.MethodName == "label");
    }

    [Fact]
    public void Java_SynchronizedAndFinal()
    {
        var lines = new[]
        {
            "public class Cache {",
            "    public synchronized final String get(String key) {",
            "        return map.get(key);",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".java");
        Assert.Single(methods);
        Assert.Equal("get", methods[0].MethodName);
    }

    // ========================================
    // Kotlin patterns
    // ========================================

    [Fact]
    public void Kotlin_FunAndDataClass()
    {
        var lines = new[]
        {
            "data class User(val name: String)",
            "",
            "class UserService {",
            "    fun getUser(id: Int): User {",
            "        return repo.find(id)",
            "    }",
            "    suspend fun fetchRemote(): User {",
            "        return api.get()",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".kt");
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.MethodName == "getUser");
        Assert.Contains(methods, m => m.MethodName == "fetchRemote");
    }

    [Fact]
    public void Kotlin_ObjectAndSealedClass()
    {
        var lines = new[]
        {
            "object Singleton {",
            "    fun instance(): Singleton { return this }",
            "}",
            "sealed class Result {",
            "    fun isOk(): Boolean { return true }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".kt");
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.ClassName == "Singleton");
        Assert.Contains(methods, m => m.ClassName == "Result");
    }

    [Fact]
    public void Kotlin_OverrideAndPrivateFun()
    {
        var lines = new[]
        {
            "class MyAdapter {",
            "    override fun onBind(holder: ViewHolder) {",
            "        holder.bind()",
            "    }",
            "    private fun validate(input: String): Boolean {",
            "        return input.isNotEmpty()",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".kt");
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.MethodName == "onBind");
        Assert.Contains(methods, m => m.MethodName == "validate");
    }

    // ========================================
    // JavaScript patterns
    // ========================================

    [Fact]
    public void JS_FunctionDeclaration()
    {
        var lines = new[]
        {
            "function handleClick(event) {",
            "    console.log(event);",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".js");
        Assert.Single(methods);
        Assert.Equal("handleClick", methods[0].MethodName);
        Assert.Equal("Global", methods[0].ClassName);
    }

    [Fact]
    public void JS_ArrowFunction()
    {
        var lines = new[]
        {
            "const fetchData = async (url) => {",
            "    return await fetch(url);",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".js");
        Assert.Single(methods);
        Assert.Equal("fetchData", methods[0].MethodName);
    }

    [Fact]
    public void JS_ClassMethod()
    {
        var lines = new[]
        {
            "class ApiClient {",
            "    async getData(endpoint) {",
            "        return fetch(endpoint);",
            "    }",
            "    parse(response) {",
            "        return response.json();",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".js");
        Assert.True(methods.Count >= 2);
        Assert.Contains(methods, m => m.ClassName == "ApiClient" && m.MethodName == "getData");
        Assert.Contains(methods, m => m.ClassName == "ApiClient" && m.MethodName == "parse");
    }

    [Fact]
    public void JS_ExportFunction()
    {
        var lines = new[]
        {
            "export function calculate(a, b) {",
            "    return a + b;",
            "}",
            "export default function main() {",
            "    calculate(1, 2);",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".js");
        Assert.Contains(methods, m => m.MethodName == "calculate");
        Assert.Contains(methods, m => m.MethodName == "main");
    }

    [Fact]
    public void JS_ConstFunctionAssignment()
    {
        var lines = new[]
        {
            "const processItem = function(item) {",
            "    return item.value;",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".js");
        Assert.Single(methods);
        Assert.Equal("processItem", methods[0].MethodName);
    }

    // ========================================
    // TypeScript patterns
    // ========================================

    [Fact]
    public void TS_ClassWithTypes()
    {
        var lines = new[]
        {
            "class UserService {",
            "    async findUser(id: number): Promise<User> {",
            "        return await this.repo.find(id);",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".ts");
        Assert.Single(methods);
        Assert.Equal("UserService", methods[0].ClassName);
        Assert.Equal("findUser", methods[0].MethodName);
    }

    [Fact]
    public void TSX_Component()
    {
        var lines = new[]
        {
            "export function App() {",
            "    return <div>Hello</div>;",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".tsx");
        Assert.Single(methods);
        Assert.Equal("App", methods[0].MethodName);
    }

    // ========================================
    // PHP patterns
    // ========================================

    [Fact]
    public void PHP_ClassAndMethods()
    {
        var lines = new[]
        {
            "<?php",
            "class UserController {",
            "    public function index() {",
            "        return view('users');",
            "    }",
            "    private function validate($data) {",
            "        return true;",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".php");
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.MethodName == "index");
        Assert.Contains(methods, m => m.MethodName == "validate");
    }

    [Fact]
    public void PHP_InterfaceAndTrait()
    {
        var lines = new[]
        {
            "<?php",
            "interface Cacheable {",
            "    public function cacheKey();",
            "}",
            "trait HasSlug {",
            "    public function slug() {",
            "        return strtolower($this->name);",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".php");
        Assert.Contains(methods, m => m.ClassName == "HasSlug" && m.MethodName == "slug");
    }

    [Fact]
    public void PHP_StaticAbstractFunction()
    {
        var lines = new[]
        {
            "<?php",
            "abstract class BaseModel {",
            "    abstract protected function tableName();",
            "    public static function create($data) {",
            "        return new static($data);",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".php");
        Assert.Contains(methods, m => m.MethodName == "create");
    }

    // ========================================
    // Python patterns
    // ========================================

    [Fact]
    public void Python_ClassAndDef()
    {
        var lines = new[]
        {
            "class UserService:",
            "    def __init__(self, repo):",
            "        self.repo = repo",
            "",
            "    def get_user(self, user_id):",
            "        return self.repo.find(user_id)",
            "",
            "    async def fetch_remote(self, url):",
            "        return await aiohttp.get(url)"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".py");
        Assert.Equal(3, methods.Count);
        Assert.Contains(methods, m => m.MethodName == "__init__");
        Assert.Contains(methods, m => m.MethodName == "get_user");
        Assert.Contains(methods, m => m.MethodName == "fetch_remote");
        Assert.All(methods, m => Assert.Equal("UserService", m.ClassName));
    }

    [Fact]
    public void Python_TopLevelFunction()
    {
        var lines = new[]
        {
            "def main():",
            "    print('hello')",
            "",
            "def helper(x):",
            "    return x * 2"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".py");
        Assert.Equal(2, methods.Count);
        Assert.All(methods, m => Assert.Equal("Global", m.ClassName));
    }

    [Fact]
    public void Python_MultipleClasses()
    {
        var lines = new[]
        {
            "class Dog:",
            "    def bark(self):",
            "        print('woof')",
            "",
            "class Cat:",
            "    def meow(self):",
            "        print('meow')"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".py");
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.ClassName == "Dog" && m.MethodName == "bark");
        Assert.Contains(methods, m => m.ClassName == "Cat" && m.MethodName == "meow");
    }

    [Fact]
    public void Python_AsyncDef()
    {
        var lines = new[]
        {
            "async def handle_request(request):",
            "    data = await request.json()",
            "    return Response(data)"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".py");
        Assert.Single(methods);
        Assert.Equal("handle_request", methods[0].MethodName);
    }

    // ========================================
    // Edge cases
    // ========================================

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var methods = SourceAnalyzer.ExtractMethods(Array.Empty<string>(), ".cs");
        Assert.Empty(methods);
    }

    [Fact]
    public void UnsupportedExtension_ReturnsEmpty()
    {
        var lines = new[] { "some content" };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".txt");
        Assert.Empty(methods);
    }

    [Fact]
    public void IsSourceFile_SupportedExtensions()
    {
        Assert.True(SourceAnalyzer.IsSourceFile(".cs"));
        Assert.True(SourceAnalyzer.IsSourceFile(".java"));
        Assert.True(SourceAnalyzer.IsSourceFile(".kt"));
        Assert.True(SourceAnalyzer.IsSourceFile(".kts"));
        Assert.True(SourceAnalyzer.IsSourceFile(".js"));
        Assert.True(SourceAnalyzer.IsSourceFile(".ts"));
        Assert.True(SourceAnalyzer.IsSourceFile(".tsx"));
        Assert.True(SourceAnalyzer.IsSourceFile(".jsx"));
        Assert.True(SourceAnalyzer.IsSourceFile(".php"));
        Assert.True(SourceAnalyzer.IsSourceFile(".py"));
        Assert.False(SourceAnalyzer.IsSourceFile(".txt"));
        Assert.False(SourceAnalyzer.IsSourceFile(".md"));
    }

    [Fact]
    public void CSharp_CommentsAndBlankLinesSkipped()
    {
        var lines = new[]
        {
            "// This is a comment",
            "",
            "public class Svc",
            "{",
            "    // another comment",
            "    public void DoWork()",
            "    {",
            "    }",
            "}"
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal("DoWork", methods[0].MethodName);
    }

    [Fact]
    public void CSharp_LineNumbers()
    {
        var lines = new[]
        {
            "public class Svc",      // 1
            "{",                      // 2
            "    public void A()",    // 3
            "    {",                  // 4
            "        // body",        // 5
            "    }",                  // 6
            "}"                       // 7
        };
        var methods = SourceAnalyzer.ExtractMethods(lines, ".cs");
        Assert.Single(methods);
        Assert.Equal(3, methods[0].StartLine);  // 1-based
        Assert.Equal(6, methods[0].EndLine);
    }
}
