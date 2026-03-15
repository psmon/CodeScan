using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ProjectCommand
{
    private readonly SqliteStore _db;

    public ProjectCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(long projectId)
    {
        var project = _db.GetProject(projectId);
        if (project == null)
        {
            Console.Error.WriteLine($"Error: project #{projectId} not found.");
            Console.Error.WriteLine("Run 'codescan projects' to see available projects.");
            return 1;
        }

        // === Summary ===
        Console.WriteLine($"=== Project #{project.Id} ===\n");
        Console.WriteLine($"  Path:       {project.RootPath}");
        Console.WriteLine($"  Files:      {project.FileCount}");
        Console.WriteLine($"  Dirs:       {project.DirCount}");
        Console.WriteLine($"  Size:       {FormatSize(project.TotalSize)}");
        Console.WriteLine($"  Last Scan:  {project.LastScannedAt ?? "(never)"}");

        var methodCount = _db.GetProjectMethodCount(projectId);
        var commentCount = _db.GetProjectCommentCount(projectId);
        Console.WriteLine($"  Methods:    {methodCount}");
        Console.WriteLine($"  Comments:   {commentCount}");

        // AddInfo
        if (!string.IsNullOrWhiteSpace(project.AddInfo))
            Console.WriteLine($"  AddInfo:    {project.AddInfo}");
        else
            Console.WriteLine("  AddInfo:    (none)");

        // === Extensions breakdown ===
        var extensions = _db.GetProjectExtensions(projectId);
        if (extensions.Count > 0)
        {
            Console.WriteLine($"\n--- Extensions ---");
            Console.WriteLine($"  {string.Join(", ", extensions)}");
        }

        // === Authors ===
        var authors = _db.GetProjectAuthors(projectId);
        if (authors.Count > 0)
        {
            Console.WriteLine($"\n--- Authors ---");
            foreach (var a in authors)
                Console.WriteLine($"  {a}");
        }

        // === Project Docs ===
        var docs = _db.GetProjectDocContents(projectId);
        if (docs.Count > 0)
        {
            Console.WriteLine($"\n--- Project Docs ({docs.Count}) ---");
            foreach (var doc in docs)
            {
                Console.WriteLine($"\n  [{doc.DocPath}]");
                // Show first 30 lines of doc content
                var lines = doc.Content.Split('\n');
                var showLines = Math.Min(lines.Length, 30);
                for (int i = 0; i < showLines; i++)
                    Console.WriteLine($"    {lines[i].TrimEnd('\r')}");
                if (lines.Length > 30)
                    Console.WriteLine($"    ... ({lines.Length - 30} more lines)");
            }
        }

        // === Files ===
        var files = _db.GetProjectFiles(projectId);
        if (files.Count > 0)
        {
            Console.WriteLine($"\n--- Files ({files.Count}) ---");
            foreach (var f in files)
                Console.WriteLine($"  {f.RelativePath}  ({FormatSize(f.Size)})");
        }

        // === Methods (grouped by file) ===
        var methods = _db.GetProjectMethods(projectId);
        if (methods.Count > 0)
        {
            Console.WriteLine($"\n--- Methods ({methods.Count}) ---");
            string? currentFile = null;
            foreach (var m in methods)
            {
                if (m.FilePath != currentFile)
                {
                    currentFile = m.FilePath;
                    Console.WriteLine($"\n  [{currentFile}]");
                }

                var className = string.IsNullOrEmpty(m.ClassName) ? "" : $"{m.ClassName}.";
                var lineRange = m.EndLine > 0 ? $"L{m.StartLine}-{m.EndLine}" : $"L{m.StartLine}";
                var blame = "";
                if (m.LastDate != null || m.LastAuthor != null)
                {
                    var date = m.LastDate != null ? m.LastDate[..Math.Min(10, m.LastDate.Length)] : "";
                    blame = $"  [{date} {m.LastAuthor}]";
                }
                var commit = !string.IsNullOrEmpty(m.CommitSummary) ? $" {m.CommitSummary}" : "";
                Console.WriteLine($"    {className}{m.MethodName}()  {lineRange}{blame}{commit}");
            }
        }

        // === Comments (grouped by file) ===
        var comments = _db.GetProjectComments(projectId);
        if (comments.Count > 0)
        {
            Console.WriteLine($"\n--- Comments ({comments.Count}) ---");
            string? currentFile = null;
            foreach (var c in comments)
            {
                if (c.FilePath != currentFile)
                {
                    currentFile = c.FilePath;
                    Console.WriteLine($"\n  [{currentFile}]");
                }

                var lineInfo = c.EndLine > c.StartLine ? $"L{c.StartLine}-{c.EndLine}" : $"L{c.StartLine}";
                var commentText = c.Comment.Length > 100 ? c.Comment[..100] + "..." : c.Comment;
                commentText = commentText.Replace("\n", " ").Replace("\r", "");
                Console.WriteLine($"    {lineInfo}: {commentText}");
            }
        }

        // === Scan History ===
        var scans = _db.GetProjectScans(projectId);
        if (scans.Count > 0)
        {
            Console.WriteLine($"\n--- Scan History ({scans.Count}) ---");
            Console.WriteLine($"  {"ID",-6} {"Files",-7} {"Dirs",-6} {"Size",-10} Scanned At");
            Console.WriteLine($"  {"--",-6} {"-----",-7} {"----",-6} {"----",-10} ----------");
            foreach (var s in scans)
                Console.WriteLine($"  {s.Id,-6} {s.FileCount,-7} {s.DirCount,-6} {FormatSize(s.TotalSize),-10} {s.ScannedAt}");
        }

        // === AddInfo prompt ===
        if (string.IsNullOrWhiteSpace(project.AddInfo))
        {
            Console.WriteLine();
            Console.WriteLine("Tip: No description set. Add one to help understand this project.");
            Console.WriteLine($"  codescan project-addinfo {projectId} \"Project description here\"");
            Console.WriteLine();
            Console.WriteLine("LLM-assisted (LLM analyzes the project and saves its understanding):");
            Console.WriteLine($"  1. codescan project {projectId}                        # Review project info");
            Console.WriteLine($"  2. codescan search \"\" --project {projectId}             # Browse indexed data");
            Console.WriteLine($"  3. codescan project-addinfo {projectId} \"analysis result\"  # Save understanding");
        }

        Console.WriteLine();
        return 0;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
