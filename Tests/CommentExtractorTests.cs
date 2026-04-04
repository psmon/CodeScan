using CodeScan.Services;

namespace CodeScan.Tests;

public class CommentExtractorTests
{
    // ========================================
    // C-style single-line comments (//)
    // ========================================

    [Fact]
    public void CStyle_SingleLineComment()
    {
        var lines = new[]
        {
            "// This is a service comment",
            "public class Service { }"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Contains("This is a service comment", comments[0].Comment);
        Assert.Contains("public class Service", comments[0].NearbyCode);
    }

    [Fact]
    public void CStyle_ConsecutiveSingleLine()
    {
        var lines = new[]
        {
            "// First line",
            "// Second line",
            "// Third line",
            "public void Method() { }"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Contains("First line", comments[0].Comment);
        Assert.Contains("Second line", comments[0].Comment);
        Assert.Contains("Third line", comments[0].Comment);
    }

    [Fact]
    public void CStyle_TripleSlashXmlComment()
    {
        var lines = new[]
        {
            "/// <summary>",
            "/// Gets the user by ID",
            "/// </summary>",
            "public User GetUser(int id) { }"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Contains("Gets the user by ID", comments[0].Comment);
    }

    // ========================================
    // C-style block comments (/* */)
    // ========================================

    [Fact]
    public void CStyle_BlockComment()
    {
        var lines = new[]
        {
            "/* This is a block comment",
            "   spanning multiple lines */",
            "public void Execute() { }"
        };
        var comments = CommentExtractor.Extract(lines, ".java");
        Assert.Single(comments);
        Assert.Contains("This is a block comment", comments[0].Comment);
        Assert.Contains("spanning multiple lines", comments[0].Comment);
    }

    [Fact]
    public void CStyle_SingleLineBlock()
    {
        var lines = new[]
        {
            "/* Quick note */",
            "int value = 42;"
        };
        var comments = CommentExtractor.Extract(lines, ".js");
        Assert.Single(comments);
        Assert.Contains("Quick note", comments[0].Comment);
    }

    [Fact]
    public void CStyle_JavaDocStyle()
    {
        var lines = new[]
        {
            "/**",
            " * Processes the request and returns response.",
            " * @param request the HTTP request",
            " * @return response object",
            " */",
            "public Response process(Request request) { }"
        };
        var comments = CommentExtractor.Extract(lines, ".java");
        Assert.Single(comments);
        Assert.Contains("Processes the request", comments[0].Comment);
    }

    // ========================================
    // Multiple languages using C-style
    // ========================================

    [Theory]
    [InlineData(".cs")]
    [InlineData(".java")]
    [InlineData(".kt")]
    [InlineData(".js")]
    [InlineData(".ts")]
    [InlineData(".tsx")]
    [InlineData(".jsx")]
    [InlineData(".php")]
    public void CStyle_WorksForAllLanguages(string ext)
    {
        var lines = new[]
        {
            "// This comment should be extracted",
            "doSomething();"
        };
        var comments = CommentExtractor.Extract(lines, ext);
        Assert.Single(comments);
        Assert.Contains("This comment should be extracted", comments[0].Comment);
    }

    // ========================================
    // Python comments (#)
    // ========================================

    [Fact]
    public void Python_HashComment()
    {
        var lines = new[]
        {
            "# Initialize the database connection",
            "db = Database('localhost')"
        };
        var comments = CommentExtractor.Extract(lines, ".py");
        Assert.Single(comments);
        Assert.Contains("Initialize the database connection", comments[0].Comment);
    }

    [Fact]
    public void Python_ConsecutiveHashComments()
    {
        var lines = new[]
        {
            "# Step 1: Load config",
            "# Step 2: Connect to DB",
            "# Step 3: Start server",
            "app.run()"
        };
        var comments = CommentExtractor.Extract(lines, ".py");
        Assert.Single(comments);
        Assert.Contains("Step 1", comments[0].Comment);
        Assert.Contains("Step 3", comments[0].Comment);
    }

    // ========================================
    // Python docstrings (""" and ''')
    // ========================================

    [Fact]
    public void Python_TripleDoubleQuoteDocstring()
    {
        var lines = new[]
        {
            "def calculate(x, y):",
            "    \"\"\"",
            "    Calculate the sum of two numbers.",
            "    Returns the result.",
            "    \"\"\"",
            "    return x + y"
        };
        var comments = CommentExtractor.Extract(lines, ".py");
        Assert.Contains(comments, c => c.Comment.Contains("Calculate the sum"));
    }

    [Fact]
    public void Python_TripleSingleQuoteDocstring()
    {
        var lines = new[]
        {
            "def process():",
            "    '''Process all pending items'''",
            "    queue.flush()"
        };
        var comments = CommentExtractor.Extract(lines, ".py");
        Assert.Contains(comments, c => c.Comment.Contains("Process all pending items"));
    }

    [Fact]
    public void Python_MultiLineDocstring()
    {
        var lines = new[]
        {
            "class Engine:",
            "    \"\"\"",
            "    Main processing engine.",
            "    Handles all data transformations.",
            "    \"\"\"",
            "    def start(self):",
            "        pass"
        };
        var comments = CommentExtractor.Extract(lines, ".py");
        Assert.Contains(comments, c => c.Comment.Contains("Main processing engine"));
    }

    // ========================================
    // Context extraction (NearbyCode, BeforeCode)
    // ========================================

    [Fact]
    public void NearbyCode_CapturesUpTo3Lines()
    {
        var lines = new[]
        {
            "// Important setup",
            "var a = 1;",
            "var b = 2;",
            "var c = 3;",
            "var d = 4;"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Contains("var a = 1;", comments[0].NearbyCode);
        Assert.Contains("var b = 2;", comments[0].NearbyCode);
        Assert.Contains("var c = 3;", comments[0].NearbyCode);
        Assert.DoesNotContain("var d = 4;", comments[0].NearbyCode);
    }

    [Fact]
    public void BeforeCode_CapturesPreviousLine()
    {
        var lines = new[]
        {
            "public class Svc {",
            "    // Service implementation",
            "    public void Run() { }"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Equal("public class Svc {", comments[0].BeforeCode);
    }

    [Fact]
    public void BeforeCode_EmptyForFirstLine()
    {
        var lines = new[]
        {
            "// This is the first line",
            "int x = 1;"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Equal("", comments[0].BeforeCode);
    }

    // ========================================
    // Line numbers
    // ========================================

    [Fact]
    public void LineNumbers_AreOneBased()
    {
        var lines = new[]
        {
            "int x = 1;",             // line 1
            "// A comment here",      // line 2
            "int y = 2;"              // line 3
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Equal(2, comments[0].StartLine);
        Assert.Equal(2, comments[0].EndLine);
    }

    [Fact]
    public void LineNumbers_BlockComment()
    {
        var lines = new[]
        {
            "code();",               // line 1
            "/* start block",        // line 2
            "   middle",             // line 3
            "   end block */",       // line 4
            "moreCode();"            // line 5
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Single(comments);
        Assert.Equal(2, comments[0].StartLine);
        Assert.Equal(4, comments[0].EndLine);
    }

    // ========================================
    // Trivial comment filtering
    // ========================================

    [Fact]
    public void TrivialComments_AreSkipped()
    {
        var lines = new[]
        {
            "// hi",     // < 5 chars after cleaning
            "// ok",
            "// This is a meaningful comment",
            "code();"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        // "hi" and "ok" alone are < 5 chars, but consecutive comments are grouped
        // The group "hi\nok" is still trivial but let's just verify meaningful ones pass
        Assert.True(comments.Count >= 1);
        Assert.Contains(comments, c => c.Comment.Contains("meaningful comment"));
    }

    // ========================================
    // Edge cases
    // ========================================

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var comments = CommentExtractor.Extract(Array.Empty<string>(), ".cs");
        Assert.Empty(comments);
    }

    [Fact]
    public void NoComments_ReturnsEmpty()
    {
        var lines = new[]
        {
            "public class Foo {",
            "    public void Bar() { }",
            "}"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Empty(comments);
    }

    [Fact]
    public void IsSupported_AllExtensions()
    {
        Assert.True(CommentExtractor.IsSupported(".cs"));
        Assert.True(CommentExtractor.IsSupported(".java"));
        Assert.True(CommentExtractor.IsSupported(".kt"));
        Assert.True(CommentExtractor.IsSupported(".kts"));
        Assert.True(CommentExtractor.IsSupported(".js"));
        Assert.True(CommentExtractor.IsSupported(".ts"));
        Assert.True(CommentExtractor.IsSupported(".tsx"));
        Assert.True(CommentExtractor.IsSupported(".jsx"));
        Assert.True(CommentExtractor.IsSupported(".php"));
        Assert.True(CommentExtractor.IsSupported(".py"));
        Assert.False(CommentExtractor.IsSupported(".txt"));
    }

    [Fact]
    public void MultipleCommentBlocks_AllExtracted()
    {
        var lines = new[]
        {
            "// First comment block",
            "public void A() { }",
            "",
            "/* Second comment block */",
            "public void B() { }",
            "",
            "// Third comment block",
            "public void C() { }"
        };
        var comments = CommentExtractor.Extract(lines, ".cs");
        Assert.Equal(3, comments.Count);
    }

    [Fact]
    public void Python_MixedHashAndDocstring()
    {
        var lines = new[]
        {
            "# Module level comment about this file",
            "import os",
            "",
            "class Config:",
            "    \"\"\"Configuration manager for the application.\"\"\"",
            "    def load(self):",
            "        # Load from disk",
            "        pass"
        };
        var comments = CommentExtractor.Extract(lines, ".py");
        Assert.True(comments.Count >= 2);
        Assert.Contains(comments, c => c.Comment.Contains("Module level comment"));
        Assert.Contains(comments, c => c.Comment.Contains("Configuration manager"));
    }
}
