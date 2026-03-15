using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ProjectAddInfoCommand
{
    private readonly SqliteStore _db;

    public ProjectAddInfoCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(long projectId, string description)
    {
        var project = _db.GetProject(projectId);
        if (project == null)
        {
            Console.Error.WriteLine($"Error: project #{projectId} not found.");
            Console.Error.WriteLine("Run 'codescan projects' to see available projects.");
            return 1;
        }

        _db.SetProjectAddInfo(projectId, description);
        Console.WriteLine($"AddInfo set for project #{projectId} ({project.RootPath}):");
        Console.WriteLine($"  {description}");
        return 0;
    }
}
