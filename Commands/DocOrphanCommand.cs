using System.Text;
using CodeScan.Services;

namespace CodeScan.Commands;

/// <summary>
/// Finds "orphan" markdown docs — docs the graph can't connect to any code
/// (no heading carries a `mentions` edge to a class). It separates:
///   • intentional orphans   (frontmatter `anchor: none`)  — design/methodology, OK
///   • declared anchors       (frontmatter `governs: [...]`) — already anchored
///   • neglected orphans      (neither)                     — needs re-linking
/// For neglected orphans it lists candidate code classes named in the doc body,
/// so an AI can propose `governs:` anchors (or graph-edit links) to re-connect
/// the doc to code. See harness/knowledge/doc-code-linkage.md.
/// </summary>
public sealed class DocOrphanCommand
{
    private readonly SqliteStore _db;

    public DocOrphanCommand(SqliteStore db) => _db = db;

    public int Execute(string[] args)
    {
        long? projectId = null;
        var showAll = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": PrintHelp(); return 0;
                case "-p" or "--project" when i + 1 < args.Length:
                    if (long.TryParse(args[++i], out var pid)) projectId = pid;
                    break;
                case "--all": showAll = true; break;
            }
        }

        var result = Analyze(projectId);
        if (result.ProjectMissing)
        {
            Console.Error.WriteLine("No projects found. Run `codescan scan <path>` first.");
            return 1;
        }

        var neglected = result.Neglected;
        var declared = result.Declared;
        var intentional = result.Intentional;

        Console.WriteLine($"=== Orphan doc scan — project #{result.ProjectId} ({result.TotalUnlinked} md files with no code link) ===\n");

        Console.WriteLine($"NEGLECTED orphans ({neglected.Count}) — no `mentions`, no `governs`, no `anchor: none`:");
        if (neglected.Count == 0) Console.WriteLine("  (none)");
        foreach (var (path, candidates, hasFrontmatter) in neglected)
        {
            Console.WriteLine($"  ✗ {path}");
            if (candidates.Count > 0)
            {
                Console.WriteLine($"      candidate classes in body → {string.Join(", ", candidates)}");
                // Ready-to-apply proposal following the frontmatter schema.
                var governs = "governs: [" + string.Join(", ", candidates.Select(c => $"\"{c}\"")) + "]";
                if (hasFrontmatter)
                    Console.WriteLine($"      FIX (add to existing frontmatter):  {governs}");
                else
                    Console.WriteLine($"      FIX (prepend frontmatter):  ---\\n{governs}\\nanchor: auto\\n---");
            }
            else
            {
                Console.WriteLine("      no code-class names in body (pure prose)");
                Console.WriteLine(hasFrontmatter
                    ? "      FIX (add to existing frontmatter):  anchor: none"
                    : "      FIX (prepend frontmatter):  ---\\nanchor: none\\n---");
            }
        }

        if (showAll)
        {
            Console.WriteLine($"\nDECLARED anchors ({declared.Count}) — frontmatter `governs:` set:");
            foreach (var p in declared) Console.WriteLine($"  • {p}");
            Console.WriteLine($"\nINTENTIONAL orphans ({intentional.Count}) — frontmatter `anchor: none`:");
            foreach (var p in intentional) Console.WriteLine($"  · {p}");
        }
        else if (declared.Count + intentional.Count > 0)
        {
            Console.WriteLine($"\n({declared.Count} declared-anchor + {intentional.Count} intentional-orphan docs hidden — pass --all)");
        }

        Console.WriteLine($"""

            Re-connecting an orphan (AI + user approval):
              • The graph is DERIVED — a DB-only edge is lost on the next scan. The
                durable fix is the MD FRONTMATTER (source of truth); CodeScan then
                parses `governs:` back into the graph on scan.
              • Apply flow (see FIX lines above):
                  frontmatter exists → add the `governs:` / `anchor:` field
                  no frontmatter     → prepend a `--- ... ---` block, then add it
              • Follow the frontmatter schema so edits stay consistent:
                harness/knowledge/doc-code-linkage.md  (§ Frontmatter 템플릿)
              • DB-only curation (codescan graph-edit) is for links NOT sourced from
                a doc; prefer the frontmatter route for orphan docs.
            """);
        return 0;
    }

    /// <summary>
    /// Pure analysis (no console I/O) shared by the CLI and the TUI. Resolves the
    /// project (defaults to latest scanned), then classifies every markdown doc
    /// the graph can't link to code into neglected / declared / intentional.
    /// </summary>
    public DocOrphanResult Analyze(long? projectId)
    {
        projectId ??= _db.GetLatestProjectId();
        if (projectId is null)
            return new DocOrphanResult { ProjectMissing = true };

        var project = _db.GetProject(projectId.Value);
        var root = project?.RootPath;
        var unlinked = _db.FindUnlinkedMarkdownDocs(projectId);
        var classLabels = _db.GetClassLabels(projectId);

        var result = new DocOrphanResult
        {
            ProjectId = projectId.Value,
            TotalUnlinked = unlinked.Count
        };

        foreach (var rel in unlinked)
        {
            var (anchor, hasGoverns, hasFrontmatter, body) = ReadFrontmatterAndBody(root, rel);
            if (string.Equals(anchor, "none", StringComparison.OrdinalIgnoreCase)) { result.Intentional.Add(rel); continue; }
            if (hasGoverns) { result.Declared.Add(rel); continue; }
            result.Neglected.Add(new OrphanDoc(rel, CandidateClasses(body, classLabels), hasFrontmatter));
        }

        return result;
    }

    // Read the YAML-ish frontmatter (anchor / governs) and the body after it.
    private static (string? Anchor, bool HasGoverns, bool HasFrontmatter, string Body) ReadFrontmatterAndBody(string? root, string relativePath)
    {
        if (string.IsNullOrEmpty(root)) return (null, false, false, "");
        string text;
        try { text = File.ReadAllText(Path.Combine(root, relativePath)); }
        catch { return (null, false, false, ""); }

        string? anchor = null;
        var hasGoverns = false;
        var hasFrontmatter = false;
        var body = text;

        if (text.StartsWith("---"))
        {
            var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                hasFrontmatter = true;
                var fm = text[3..end];
                body = text[(end + 4)..];
                foreach (var raw in fm.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("anchor:", StringComparison.OrdinalIgnoreCase))
                        anchor = StripComment(line[7..]);
                    else if (line.StartsWith("governs:", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = StripComment(line[8..]);
                        // Non-empty governs = declared. "[]" / empty = not declared.
                        hasGoverns = v.Length > 0 && v != "[]" && v != "[ ]";
                    }
                }
            }
        }
        return (anchor, hasGoverns, hasFrontmatter, body);
    }

    // Drop an inline YAML comment (`value  # note`) and trim.
    private static string StripComment(string value)
    {
        var hash = value.IndexOf('#');
        if (hash >= 0) value = value[..hash];
        return value.Trim();
    }

    // Identifier-like body tokens (len ≥ 4) that exactly (case-sensitively) equal
    // a project class name — candidates the doc could `governs`/`mentions`.
    private static List<string> CandidateClasses(string body, HashSet<string> classLabels)
    {
        if (classLabels.Count == 0 || body.Length == 0) return [];
        var found = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length >= 4 && classLabels.Contains(sb.ToString())) found.Add(sb.ToString());
            sb.Clear();
        }
        foreach (var ch in body)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else Flush();
        }
        Flush();
        return found.OrderBy(s => s, StringComparer.Ordinal).Take(12).ToList();
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            codescan doc-orphan — find docs with no code linkage (orphans)

            Usage: codescan doc-orphan [--project <id>] [--all]

            A markdown doc is an "orphan" when the graph can't connect it to any
            code: none of its headings link (`mentions`) to a class. Orphans are
            classified by frontmatter:
              • NEGLECTED    — no anchor at all → likely needs re-linking (shown)
              • DECLARED     — `governs: [...]` present → already anchored
              • INTENTIONAL  — `anchor: none` → pure methodology, OK

            For each neglected orphan, candidate class names found in the doc body
            are listed so an AI can propose `governs:` anchors or graph-edit links.

            Options:
              -p, --project <id>   Scope to one project (default: latest scanned)
              --all                Also list declared-anchor and intentional docs
              -h, --help           Show help

            See: harness/knowledge/doc-code-linkage.md
            """);
    }
}

/// <summary>A neglected orphan doc plus the class names its body mentions.</summary>
public sealed record OrphanDoc(string Path, List<string> Candidates, bool HasFrontmatter);

/// <summary>Structured orphan-doc analysis shared by CLI and TUI renderers.</summary>
public sealed class DocOrphanResult
{
    public long ProjectId { get; set; }
    public int TotalUnlinked { get; set; }
    public bool ProjectMissing { get; set; }
    public List<OrphanDoc> Neglected { get; } = [];
    public List<string> Declared { get; } = [];
    public List<string> Intentional { get; } = [];
}
