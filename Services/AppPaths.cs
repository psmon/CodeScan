namespace CodeScan.Services;

public static class AppPaths
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codescan");

    public static string DbDir => Path.Combine(BaseDir, "db");

    public static string LogDir => Path.Combine(BaseDir, "logs");

    public static string RunDir => Path.Combine(BaseDir, "run");

    public static string DbPath
    {
        get
        {
            Directory.CreateDirectory(DbDir);
            return Path.Combine(DbDir, "codescan.db");
        }
    }

    public static string GetLogDir()
    {
        Directory.CreateDirectory(LogDir);
        return LogDir;
    }

    public static string GetRunDir()
    {
        Directory.CreateDirectory(RunDir);
        return RunDir;
    }
}
