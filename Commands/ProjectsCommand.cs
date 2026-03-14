using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ProjectsCommand
{
    private readonly SqliteStore _db;

    public ProjectsCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute()
    {
        var projects = _db.GetProjects();

        if (projects.Count == 0)
        {
            Console.WriteLine("No indexed projects. Run 'codescan list <path> --detail' to index.");
            return 0;
        }

        Console.WriteLine($"Indexed Projects ({projects.Count}):\n");
        Console.WriteLine($"  {"ID",-4} {"Files",-7} {"Dirs",-6} {"Size",-10} {"Last Scan",-20} Path");
        Console.WriteLine($"  {"--",-4} {"-----",-7} {"----",-6} {"----",-10} {"---------",-20} ----");

        foreach (var p in projects)
        {
            var size = FormatSize(p.TotalSize);
            var lastScan = p.LastScannedAt ?? "(never)";
            Console.WriteLine($"  {p.Id,-4} {p.FileCount,-7} {p.DirCount,-6} {size,-10} {lastScan,-20} {p.RootPath}");
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
