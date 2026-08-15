using System.Text.RegularExpressions;

namespace CodeScan.Services;

public sealed class GraphQuerySpec
{
    public string LeftAlias { get; init; } = "n";
    public string? LeftKind { get; init; }
    public bool HasEdge { get; init; }
    public string EdgeAlias { get; init; } = "r";
    public string? EdgeKind { get; init; }
    public string RightAlias { get; init; } = "m";
    public string? RightKind { get; init; }
    public List<GraphQueryCondition> Conditions { get; init; } = [];
    public int? Limit { get; init; }

    // Variable-length path: `-[r:kind*1..3]->`. When true the edge is traversed
    // repeatedly (MinHops..MaxHops) via a recursive CTE instead of a single join.
    public bool HasVariableHops { get; init; }
    public int MinHops { get; init; } = 1;
    public int MaxHops { get; init; } = 1;
}

public sealed class GraphQueryCondition
{
    public required string Alias { get; init; }
    public required string Field { get; init; }
    public required GraphQueryOperator Operator { get; init; }
    public required string Value { get; init; }
}

public enum GraphQueryOperator
{
    Equals,
    Contains,
    StartsWith,
    EndsWith
}

public sealed class GraphQueryParseException : Exception
{
    public GraphQueryParseException(string message) : base(message) { }
}

public static partial class GraphQueryParser
{
    private static readonly HashSet<string> NodeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "label", "path", "detail"
    };

    private static readonly HashSet<string> EdgeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "label"
    };

    public static bool LooksLikeQuery(string query)
        => query.TrimStart().StartsWith("MATCH ", StringComparison.OrdinalIgnoreCase);

    public static GraphQuerySpec Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new GraphQueryParseException("Graph query is empty.");

        // Structural pre-check: catch constructs the single-pattern regex would
        // otherwise partially match (leaving the unsupported tail silently
        // ignored), so the caller gets an actionable error instead of a wrong
        // result.
        RejectUnsupportedConstructs(query);

        var match = MatchPattern().Match(query);
        if (!match.Success)
            ThrowUnsupportedPattern(query);   // always throws with actionable guidance

        var leftAlias = ValueOr(match, "leftAlias", "n");
        var edgeAlias = ValueOr(match, "edgeAlias", "r");
        var rightAlias = ValueOr(match, "rightAlias", "m");
        var hasEdge = match.Groups["edge"].Success;
        var tail = match.Groups["tail"].Value;
        var limit = ParseLimit(tail);
        var conditions = ParseConditions(tail);

        ValidateConditions(conditions, leftAlias, hasEdge ? edgeAlias : null, hasEdge ? rightAlias : null);

        var (hasHops, minHops, maxHops) = ParseHops(match);

        return new GraphQuerySpec
        {
            LeftAlias = leftAlias,
            LeftKind = EmptyToNull(match.Groups["leftKind"].Value),
            HasEdge = hasEdge,
            EdgeAlias = edgeAlias,
            EdgeKind = EmptyToNull(match.Groups["edgeKind"].Value),
            RightAlias = rightAlias,
            RightKind = EmptyToNull(match.Groups["rightKind"].Value),
            Conditions = conditions,
            Limit = limit,
            HasVariableHops = hasHops,
            MinHops = minHops,
            MaxHops = maxHops
        };
    }

    // Upper bound on variable-length traversal — keeps recursive CTEs cheap and
    // an unbounded `*` from walking the whole graph.
    public const int HopCap = 6;

    // Interpret the `*`, `*N`, `*min..max`, `*..max`, `*min..` quantifier.
    private static (bool has, int min, int max) ParseHops(Match match)
    {
        if (!match.Groups["hops"].Success) return (false, 1, 1);

        var minStr = match.Groups["hopMin"].Value;
        var maxStr = match.Groups["hopMax"].Value;
        var hasRange = match.Groups["hopRange"].Success;
        int? min = minStr.Length > 0 ? int.Parse(minStr) : null;
        int? max = maxStr.Length > 0 ? int.Parse(maxStr) : null;

        int lo, hi;
        if (hasRange)          { lo = min ?? 1; hi = max ?? HopCap; }   // *min..max / *..max / *min..
        else if (min.HasValue) { lo = min.Value; hi = min.Value; }      // *N  → exactly N
        else                   { lo = 1; hi = HopCap; }                 // *   → 1..cap

        lo = Math.Max(1, lo);
        hi = Math.Min(HopCap, Math.Max(lo, hi));
        return (true, lo, hi);
    }

    // A compact capability reference appended to every parse error, so an AI
    // caller can see exactly what the CodeScan query subset supports and rewrite
    // its query without guessing.
    private static string SupportedGrammarHelp() =>
        """
        --- CodeScan graph query capabilities (a Cypher subset over the scanned graph) ---
        Patterns:
          MATCH (n:kind)
          MATCH (a:kind)-[r:edge_kind]->(b:kind)
          MATCH (a:kind)-[r:edge_kind*1..3]->(b:kind)   variable-length path, max 6 hops
        Clauses:  [WHERE <cond> AND <cond> ...] [RETURN ...] [LIMIT n]
        WHERE fields:     node = kind|label|path|detail ; edge = kind|label
        WHERE operators:  =, CONTAINS, STARTS WITH, ENDS WITH   (join with AND only)
        Node kinds: project directory file class method comment doc doc-meta heading author type module
        Edge kinds: contains defines authored has_comment documents imports
                    inherits_or_implements creates uses_type has_heading has_meta
        Not supported — rewrite instead:
          backward edges (<-[]-)        -> point the arrow forward, swap a/b
          multi-segment ((a)->(b)->(c)) -> use *min..max, or query each segment
          OR / NOT in WHERE             -> AND only, or run separate queries
          numeric comparisons (>, <)    -> not available (e.g. weight can't be filtered)
          property maps ({k:'v'})       -> move to WHERE (n.label = 'v')
        """;

    // Red-flag constructs that the single-relationship regex might partially
    // match. Detected up front so the error is specific and the unsupported tail
    // is never silently dropped.
    private static void RejectUnsupportedConstructs(string query)
    {
        if (Regex.IsMatch(query, @"<\s*-"))
            throw new GraphQueryParseException(
                "Backward relationships (<-[...]-) are not supported. Point the arrow forward and swap the endpoints: rewrite (b)<-[r:kind]-(a) as (a)-[r:kind]->(b).\n\n"
                + SupportedGrammarHelp());

        if (Regex.Matches(query, @"-\s*\[").Count > 1)
            throw new GraphQueryParseException(
                "Multi-segment paths ((a)-[]->(b)-[]->(c)) are not supported. Use one variable-length hop — (a)-[r:kind*1..3]->(c) — or run each segment as its own query.\n\n"
                + SupportedGrammarHelp());

        if (query.Contains('{'))
            throw new GraphQueryParseException(
                "Inline property maps ((n {label:'x'})) are not supported. Move properties into a WHERE clause: MATCH (n:kind) WHERE n.label = 'x'.\n\n"
                + SupportedGrammarHelp());
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowUnsupportedPattern(string query)
    {
        throw new GraphQueryParseException(
            "Could not parse the MATCH pattern. Start from one of the supported shapes below and adjust.\n\n"
            + SupportedGrammarHelp());
    }

    private static List<GraphQueryCondition> ParseConditions(string tail)
    {
        var where = ExtractClause(tail, "WHERE", ["RETURN", "LIMIT"]);
        if (string.IsNullOrWhiteSpace(where)) return [];

        var list = new List<GraphQueryCondition>();
        var parts = Regex.Split(where, @"\s+AND\s+", RegexOptions.IgnoreCase);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            var match = ConditionPattern().Match(part);
            if (!match.Success)
            {
                if (Regex.IsMatch(part, @"\bOR\b|\bNOT\b", RegexOptions.IgnoreCase))
                    throw new GraphQueryParseException(
                        $"WHERE condition '{part}' uses OR/NOT, which are not supported. Combine conditions with AND only, or run separate queries and merge the results.\n\n"
                        + SupportedGrammarHelp());

                if (Regex.IsMatch(part, @"(>=|<=|<>|!=|>|<)"))
                    throw new GraphQueryParseException(
                        $"WHERE condition '{part}' uses a comparison operator, which is not supported. Only =, CONTAINS, STARTS WITH, ENDS WITH are available (numeric fields such as edge weight cannot be filtered).\n\n"
                        + SupportedGrammarHelp());

                throw new GraphQueryParseException(
                    $"Unsupported WHERE condition: {part}.\n\n" + SupportedGrammarHelp());
            }

            list.Add(new GraphQueryCondition
            {
                Alias = match.Groups["alias"].Value,
                Field = match.Groups["field"].Value,
                Operator = ParseOperator(match.Groups["op"].Value),
                Value = Unquote(match.Groups["value"].Value)
            });
        }
        return list;
    }

    private static void ValidateConditions(List<GraphQueryCondition> conditions, string leftAlias, string? edgeAlias, string? rightAlias)
    {
        foreach (var condition in conditions)
        {
            var isLeft = condition.Alias.Equals(leftAlias, StringComparison.OrdinalIgnoreCase);
            var isRight = rightAlias != null && condition.Alias.Equals(rightAlias, StringComparison.OrdinalIgnoreCase);
            var isEdge = edgeAlias != null && condition.Alias.Equals(edgeAlias, StringComparison.OrdinalIgnoreCase);

            if (!isLeft && !isRight && !isEdge)
                throw new GraphQueryParseException(
                    $"Unknown alias '{condition.Alias}' in WHERE — it must match an alias declared in MATCH.\n\n" + SupportedGrammarHelp());

            if ((isLeft || isRight) && !NodeFields.Contains(condition.Field))
                throw new GraphQueryParseException(
                    $"Unsupported node field '{condition.Field}'. Use kind, label, path, or detail.\n\n" + SupportedGrammarHelp());

            if (isEdge && !EdgeFields.Contains(condition.Field))
                throw new GraphQueryParseException(
                    $"Unsupported edge field '{condition.Field}'. Use kind or label.\n\n" + SupportedGrammarHelp());
        }
    }

    private static int? ParseLimit(string tail)
    {
        var match = LimitPattern().Match(tail);
        if (!match.Success) return null;
        return int.TryParse(match.Groups["limit"].Value, out var limit) ? limit : null;
    }

    private static string ExtractClause(string text, string startKeyword, string[] endKeywords)
    {
        var start = IndexOfKeyword(text, startKeyword);
        if (start < 0) return "";

        start += startKeyword.Length;
        var end = text.Length;
        foreach (var keyword in endKeywords)
        {
            var idx = IndexOfKeyword(text[start..], keyword);
            if (idx >= 0)
                end = Math.Min(end, start + idx);
        }
        return text[start..end].Trim();
    }

    private static int IndexOfKeyword(string text, string keyword)
        => Regex.Match(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase).Success
            ? Regex.Match(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase).Index
            : -1;

    private static GraphQueryOperator ParseOperator(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
        return normalized switch
        {
            "=" => GraphQueryOperator.Equals,
            "CONTAINS" => GraphQueryOperator.Contains,
            "STARTS WITH" => GraphQueryOperator.StartsWith,
            "ENDS WITH" => GraphQueryOperator.EndsWith,
            _ => throw new GraphQueryParseException($"Unsupported operator: {value}")
        };
    }

    private static string ValueOr(Match match, string groupName, string fallback)
        => string.IsNullOrWhiteSpace(match.Groups[groupName].Value) ? fallback : match.Groups[groupName].Value;

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    [GeneratedRegex(@"^\s*MATCH\s*\(\s*(?<leftAlias>[A-Za-z_]\w*)?\s*(?::\s*(?<leftKind>[\w-]+))?\s*\)\s*(?<edge>-\s*\[\s*(?<edgeAlias>[A-Za-z_]\w*)?\s*(?::\s*(?<edgeKind>[\w-]+))?\s*(?<hops>\*\s*(?<hopMin>\d*)\s*(?:(?<hopRange>\.\.)\s*(?<hopMax>\d*))?)?\s*\]\s*(?:->|--)\s*\(\s*(?<rightAlias>[A-Za-z_]\w*)?\s*(?::\s*(?<rightKind>[\w-]+))?\s*\))?\s*(?<tail>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MatchPattern();

    [GeneratedRegex(@"\bLIMIT\s+(?<limit>\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LimitPattern();

    [GeneratedRegex(@"^(?<alias>[A-Za-z_]\w*)\.(?<field>[A-Za-z_]\w*)\s*(?<op>CONTAINS|STARTS\s+WITH|ENDS\s+WITH|=)\s*(?<value>""[^""]*""|'[^']*'|[^\s]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionPattern();
}
