using CodeScan.Services;

namespace CodeScan.Tests;

public class MarkdownAnalyzerTests
{
    // ========================================
    // Headings
    // ========================================

    [Fact]
    public void Headings_ExtractsAllLevels()
    {
        var md = """
            # Title
            ## Section
            ### Subsection
            text
            """;
        var doc = MarkdownAnalyzer.AnalyzeContent(md);

        Assert.Equal(3, doc.Headings.Count);
        Assert.Equal(1, doc.Headings[0].Level);
        Assert.Equal("Title", doc.Headings[0].Text);
        Assert.Equal(2, doc.Headings[1].Level);
        Assert.Equal("Section", doc.Headings[1].Text);
        Assert.Equal(3, doc.Headings[2].Level);
        Assert.Equal("Subsection", doc.Headings[2].Text);
    }

    [Fact]
    public void Headings_StripsEmphasisMarkers()
    {
        var doc = MarkdownAnalyzer.AnalyzeContent("# **1. 핵심 개념**");
        Assert.Single(doc.Headings);
        Assert.Equal("1. 핵심 개념", doc.Headings[0].Text);
    }

    [Fact]
    public void Headings_KoreanText()
    {
        var doc = MarkdownAnalyzer.AnalyzeContent("## 기간별 상담 통계란?");
        Assert.Single(doc.Headings);
        Assert.Equal("기간별 상담 통계란?", doc.Headings[0].Text);
    }

    [Fact]
    public void Headings_IgnoresHashWithoutSpace()
    {
        // "#tag" is not an ATX heading.
        var doc = MarkdownAnalyzer.AnalyzeContent("#nospace\n# real heading");
        Assert.Single(doc.Headings);
        Assert.Equal("real heading", doc.Headings[0].Text);
    }

    // ========================================
    // Frontmatter (YAML metadata)
    // ========================================

    [Fact]
    public void FrontMatter_ExtractsKeyValues()
    {
        var md = """
            ---
            project: BlumAI
            code: 'HUB-241'
            doc_type: manual
            ---

            # Body
            """;
        var doc = MarkdownAnalyzer.AnalyzeContent(md);

        Assert.Contains("project: BlumAI", doc.FrontMatter);
        Assert.Contains("code: HUB-241", doc.FrontMatter);
        Assert.Contains("doc_type: manual", doc.FrontMatter);
    }

    [Fact]
    public void FrontMatter_FlattensListsAndWikiLinks()
    {
        var md = """
            ---
            tags:
              - 상담통계
              - manual
            related:
              - [[2026-03-통계2.0-정책서]]
            ---
            body
            """;
        var doc = MarkdownAnalyzer.AnalyzeContent(md);

        Assert.Contains("상담통계", doc.FrontMatter);
        Assert.Contains("manual", doc.FrontMatter);
        // wiki-link brackets unwrapped
        Assert.Contains("2026-03-통계2.0-정책서", doc.FrontMatter);
        Assert.DoesNotContain("[[", doc.FrontMatter);
    }

    [Fact]
    public void FrontMatter_RemovedFromBody()
    {
        var md = """
            ---
            title: X
            ---
            # Real Heading
            """;
        var doc = MarkdownAnalyzer.AnalyzeContent(md);

        // The 'title' key must not be parsed as a heading, and body holds the heading.
        Assert.Single(doc.Headings);
        Assert.Equal("Real Heading", doc.Headings[0].Text);
        Assert.DoesNotContain("title: X", doc.Body);
    }

    [Fact]
    public void NoFrontMatter_BodyIsWholeContent()
    {
        var md = "# Just a heading\nsome text";
        var doc = MarkdownAnalyzer.AnalyzeContent(md);

        Assert.Equal("", doc.FrontMatter);
        Assert.Contains("some text", doc.Body);
        Assert.Single(doc.Headings);
    }

    [Fact]
    public void DashRuleIsNotFrontMatter()
    {
        // A horizontal rule mid-document is not frontmatter (must be at the top).
        var md = "intro\n---\n# Heading";
        var doc = MarkdownAnalyzer.AnalyzeContent(md);

        Assert.Equal("", doc.FrontMatter);
        Assert.Contains("intro", doc.Body);
    }
}
