using CodeScan.Services;

namespace CodeScan.Commands;

public sealed class ProjectUpdateCommand
{
    private readonly SqliteStore _db;

    public ProjectUpdateCommand(SqliteStore db)
    {
        _db = db;
    }

    public int Execute(long projectId, string? newPath, string? newAddInfo, bool sourceUpdate = false)
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

        if (sourceUpdate)
        {
            Console.WriteLine($"Source update: project #{projectId} ({project.RootPath})");
            var svc = new SourceUpdateService(_db, msg => Console.WriteLine(msg));
            var result = svc.Execute(projectId, project.RootPath);

            if (!result.Success)
            {
                Console.Error.WriteLine($"Error: {result.ErrorMessage}");
                return 1;
            }

            Console.WriteLine($"Source update complete: {result.FileCount} files, {result.MethodCount} methods indexed.");
            updated = true;
        }

        if (updated)
            Console.WriteLine($"Project #{projectId} updated.");
        else
        {
            Console.Error.WriteLine("Error: no fields specified to update.");
            Console.Error.WriteLine("Usage: codescan project-update <id> [--path <path>] [--addinfo <text>] [--source]");
            return 1;
        }

        return 0;
    }
}
