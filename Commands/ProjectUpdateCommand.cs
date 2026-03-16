using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ProjectUpdateCommand
{
    private readonly SqliteStore _db;

    public ProjectUpdateCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(long projectId, string? newPath, string? newAddInfo)
    {
        var project = _db.GetProject(projectId);
        if (project == null)
        {
            Console.Error.WriteLine($"Error: project #{projectId} not found.");
            Console.Error.WriteLine("Run 'codescan projects' to see available projects.");
            return 1;
        }

        bool updated = false;

        if (newPath != null)
        {
            var fullPath = Path.GetFullPath(newPath);
            _db.SetProjectPath(projectId, fullPath);
            Console.WriteLine($"  path: {project.RootPath} -> {fullPath}");
            updated = true;
        }

        if (newAddInfo != null)
        {
            _db.SetProjectAddInfo(projectId, newAddInfo);
            Console.WriteLine($"  addinfo: {newAddInfo}");
            updated = true;
        }

        if (updated)
            Console.WriteLine($"Project #{projectId} updated.");
        else
        {
            Console.Error.WriteLine("Error: no fields specified to update.");
            Console.Error.WriteLine("Usage: codescan project-update <id> [--path <path>] [--addinfo <text>]");
            return 1;
        }

        return 0;
    }
}
