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

        // Project info
        Console.WriteLine($"=== Project #{project.Id} ===\n");
        Console.WriteLine($"  Path:       {project.RootPath}");
        Console.WriteLine($"  Files:      {project.FileCount}");
        Console.WriteLine($"  Dirs:       {project.DirCount}");
        Console.WriteLine($"  Size:       {FormatSize(project.TotalSize)}");
        Console.WriteLine($"  Last Scan:  {project.LastScannedAt ?? "(never)"}");

        // Methods & comments count
        var methodCount = _db.GetProjectMethodCount(projectId);
        var commentCount = _db.GetProjectCommentCount(projectId);
        Console.WriteLine($"  Methods:    {methodCount}");
        Console.WriteLine($"  Comments:   {commentCount}");

        // Project docs
        var docs = _db.GetProjectDocs(projectId);
        if (docs.Count > 0)
        {
            Console.WriteLine($"  Docs:       {string.Join(", ", docs)}");
        }

        // AddInfo
        Console.WriteLine();
        if (!string.IsNullOrWhiteSpace(project.AddInfo))
        {
            Console.WriteLine($"  AddInfo:    {project.AddInfo}");
        }
        else
        {
            Console.WriteLine("  AddInfo:    (none)");
            Console.WriteLine();
            Console.WriteLine("  Tip: No additional description set for this project.");
            Console.WriteLine("  You can add one manually:");
            Console.WriteLine($"    codescan project-addinfo {projectId} \"Project description here\"");
            Console.WriteLine();
            Console.WriteLine("  Or ask an LLM to analyze and generate a description:");
            Console.WriteLine($"    Use 'codescan search \"\" --project {projectId}' to review indexed data,");
            Console.WriteLine("    then set a description with project-addinfo.");
        }

        // Scan history
        var scans = _db.GetProjectScans(projectId);
        if (scans.Count > 0)
        {
            Console.WriteLine($"\n  Scan History (latest {scans.Count}):");
            Console.WriteLine($"    {"ID",-6} {"Files",-7} {"Dirs",-6} {"Size",-10} Scanned At");
            Console.WriteLine($"    {"--",-6} {"-----",-7} {"----",-6} {"----",-10} ----------");
            foreach (var s in scans)
            {
                Console.WriteLine($"    {s.Id,-6} {s.FileCount,-7} {s.DirCount,-6} {FormatSize(s.TotalSize),-10} {s.ScannedAt}");
            }
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
