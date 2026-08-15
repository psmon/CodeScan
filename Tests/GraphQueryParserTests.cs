using CodeScan.Models;
using CodeScan.Services;

namespace CodeScan.Tests;

public class GraphQueryParserTests
{
    [Fact]
    public void Parse_NodeQuery_WithKindWhereAndLimit()
    {
        var query = GraphQueryParser.Parse("MATCH (c:class) WHERE c.label CONTAINS 'HttpClient' LIMIT 20");

        Assert.Equal("c", query.LeftAlias);
        Assert.Equal("class", query.LeftKind);
        Assert.False(query.HasEdge);
        Assert.Equal(20, query.Limit);
        Assert.Single(query.Conditions);
        Assert.Equal("label", query.Conditions[0].Field);
        Assert.Equal(GraphQueryOperator.Contains, query.Conditions[0].Operator);
        Assert.Equal("HttpClient", query.Conditions[0].Value);
    }

    [Fact]
    public void Parse_EdgeQuery_WithAliasesAndKinds()
    {
        var query = GraphQueryParser.Parse("MATCH (f:file)-[r:imports]->(m:module) WHERE m.label STARTS WITH 'System' RETURN f,r,m LIMIT 10");

        Assert.True(query.HasEdge);
        Assert.Equal("f", query.LeftAlias);
        Assert.Equal("file", query.LeftKind);
        Assert.Equal("r", query.EdgeAlias);
        Assert.Equal("imports", query.EdgeKind);
        Assert.Equal("m", query.RightAlias);
        Assert.Equal("module", query.RightKind);
        Assert.Equal(10, query.Limit);
        Assert.Single(query.Conditions);
        Assert.Equal(GraphQueryOperator.StartsWith, query.Conditions[0].Operator);
    }

    [Fact]
    public void Parse_RejectsUnsupportedEdgeField()
    {
        Assert.Throws<GraphQueryParseException>(() =>
            GraphQueryParser.Parse("MATCH (a:class)-[r:uses_type]->(b:type) WHERE r.path CONTAINS 'x'"));
    }

    [Theory]
    [InlineData("MATCH (a:class)-[r:uses_type*1..3]->(b:type)", 1, 3)]
    [InlineData("MATCH (a:class)-[r:uses_type*2]->(b:type)", 2, 2)]      // exactly N
    [InlineData("MATCH (a:class)-[r:uses_type*]->(b:type)", 1, 6)]        // unbounded → cap
    [InlineData("MATCH (a:class)-[r:uses_type*..4]->(b:type)", 1, 4)]     // open lower
    [InlineData("MATCH (a:class)-[r:uses_type*2..]->(b:type)", 2, 6)]     // open upper → cap
    [InlineData("MATCH (a:class)-[r:uses_type*3..99]->(b:type)", 3, 6)]   // clamp to cap
    public void Parse_VariableHops(string query, int expectedMin, int expectedMax)
    {
        var spec = GraphQueryParser.Parse(query);
        Assert.True(spec.HasVariableHops);
        Assert.True(spec.HasEdge);
        Assert.Equal(expectedMin, spec.MinHops);
        Assert.Equal(expectedMax, spec.MaxHops);
    }

    [Fact]
    public void Parse_SingleHop_HasNoVariableHops()
    {
        var spec = GraphQueryParser.Parse("MATCH (a:class)-[r:uses_type]->(b:type)");
        Assert.False(spec.HasVariableHops);
    }

    [Theory]
    [InlineData("MATCH (b:type)<-[r:uses_type]-(a:class)", "Backward")]
    [InlineData("MATCH (a:class)-[r:contains]->(b:file)-[s:defines]->(c:method)", "Multi-segment")]
    [InlineData("MATCH (n {label:'x'})", "property maps")]
    [InlineData("MATCH (c:class) WHERE c.label = 'A' OR c.label = 'B'", "OR/NOT")]
    [InlineData("MATCH (a:class)-[r:uses_type]->(b:type) WHERE b.weight > 3", "comparison operator")]
    public void Parse_UnsupportedConstructs_GiveActionableErrors(string query, string expectedHint)
    {
        var ex = Assert.Throws<GraphQueryParseException>(() => GraphQueryParser.Parse(query));
        Assert.Contains(expectedHint, ex.Message);
        // Every parse error carries the capability reference so an AI can self-correct.
        Assert.Contains("CodeScan graph query capabilities", ex.Message);
    }

    // Actor-model edges (T2): free-form EdgeKind means the parser accepts them
    // without code change. The canonical names live in EdgeKinds.
    [Theory]
    [InlineData(EdgeKinds.SpawnsChild)]
    [InlineData(EdgeKinds.ReceivesMessage)]
    [InlineData(EdgeKinds.SendsMessageTo)]
    [InlineData(EdgeKinds.SupervisesWith)]
    [InlineData(EdgeKinds.ActorNamed)]
    [InlineData(EdgeKinds.Activates)]
    public void Parse_AcceptsActorModelEdgeKinds(string edge)
    {
        var query = GraphQueryParser.Parse($"MATCH (a:class)-[r:{edge}]->(b:type) LIMIT 5");

        Assert.True(query.HasEdge);
        Assert.Equal(edge, query.EdgeKind);
        Assert.Equal(5, query.Limit);
    }
}
