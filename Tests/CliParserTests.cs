using CodeScan.Cli;

namespace CodeScan.Tests;

public class CliParserTests
{
    [Fact]
    public void GlobalOptions_StopAtCommand()
    {
        var parsed = CliParser.ParseGlobal(["--verbose", "search", "--help"]);

        Assert.True(parsed.Options.Verbose);
        Assert.False(parsed.Options.ShowHelp);
        Assert.Equal(["search", "--help"], parsed.Remaining);
    }

    [Fact]
    public void Scan_DefaultsPathAndDetailOutput()
    {
        var parsed = CliParser.ParseScan([]);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(".", parsed.Value!.ForwardedArguments[0]);
        Assert.Contains("--detail", parsed.Value.ForwardedArguments);
        Assert.Contains("--tree", parsed.Value.ForwardedArguments);
        Assert.Contains("--stats", parsed.Value.ForwardedArguments);
    }

    [Fact]
    public void List_ParsesAllCoreOptions()
    {
        var parsed = CliParser.ParseList(["src", "--include", ".cs,.md", "--exclude", "bin,obj", "--depth", "3", "--detail", "--full"]);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("src", parsed.Value!.Path);
        Assert.Equal([".cs", ".md"], parsed.Value.Options.Include);
        Assert.Equal(["bin", "obj"], parsed.Value.Options.Exclude);
        Assert.Equal(3, parsed.Value.Options.Depth);
        Assert.True(parsed.Value.Options.Detail);
        Assert.True(parsed.Value.Options.FullRebuild);
    }

    [Theory]
    [InlineData("--depth")]
    [InlineData("--include")]
    [InlineData("--mode")]
    public void List_RejectsMissingOptionValue(string option)
    {
        var parsed = CliParser.ParseList([".", option]);

        Assert.False(parsed.IsSuccess);
        Assert.Contains("requires a value", parsed.Error);
    }

    [Fact]
    public void Search_ParsesGraphQueryOptions()
    {
        var parsed = CliParser.ParseSearch(["MATCH (n:class)", "--query", "--project", "7", "--depth", "2"]);

        Assert.True(parsed.IsSuccess);
        Assert.Equal("MATCH (n:class)", parsed.Value!.Query);
        Assert.True(parsed.Value.Options.Graph);
        Assert.True(parsed.Value.Options.GraphQuery);
        Assert.Equal(7, parsed.Value.Options.ProjectId);
        Assert.Equal(2, parsed.Value.Options.GraphDepth);
    }

    [Fact]
    public void Search_RejectsUnknownOption()
    {
        var parsed = CliParser.ParseSearch(["term", "--limt", "2"]);

        Assert.False(parsed.IsSuccess);
        Assert.Contains("unknown option", parsed.Error);
    }

    [Fact]
    public void Query_RequiresQueryText()
    {
        var parsed = CliParser.ParseGraph(["--limit", "10"], queryRequired: true);

        Assert.False(parsed.IsSuccess);
        Assert.Equal("graph query is required.", parsed.Error);
    }
}
