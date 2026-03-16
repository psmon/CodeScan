using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ProjectDeleteCommand
{
    private readonly SqliteStore _db;

    public ProjectDeleteCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(long projectId, bool force)
    {
        var project = _db.GetProject(projectId);
        if (project == null)
        {
            Console.Error.WriteLine($"Error: project #{projectId} not found.");
            Console.Error.WriteLine("Run 'codescan projects' to see available projects.");
            return 1;
        }

        if (!force)
        {
            Console.WriteLine($"Delete project #{projectId}?");
            Console.WriteLine($"  Path: {project.RootPath}");
            Console.WriteLine($"  Files: {project.FileCount}  Dirs: {project.DirCount}");
            Console.Write("Confirm (y/N): ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("y" or "yes"))
            {
                Console.WriteLine("Cancelled.");
                return 0;
            }
        }

        _db.DeleteProject(projectId);
        Console.WriteLine($"Deleted project #{projectId} ({project.RootPath})");
        return 0;
    }
}
