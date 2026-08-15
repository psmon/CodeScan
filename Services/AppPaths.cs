namespace CodeScan.Services;

public static class AppPaths
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codescan");

    public static string DbDir => Path.Combine(BaseDir, "db");

    public static string LogDir => Path.Combine(BaseDir, "logs");

    public static string RunDir => Path.Combine(BaseDir, "run");

    public static string SemanticDir => Path.Combine(BaseDir, "semantic");

    public static string GetSemanticDir()
    {
        Directory.CreateDirectory(SemanticDir);
        return SemanticDir;
    }

    // Schema epoch is encoded in the DB file name. Backward-incompatible schema
    // changes ship a new file (codescan.db → codescan-v2.db) instead of an
    // in-place destructive migration, so the previous DB stays intact for
    // rollback. Incremental, compatible migrations within an epoch are handled
    // by PRAGMA user_version inside SqliteStore. v2 = project-scoped,
    // incrementally reconciled graph.
    public const string DbFileName = "codescan-v2.db";

    public static string DbPath
    {
        get
        {
            Directory.CreateDirectory(DbDir);
            return Path.Combine(DbDir, DbFileName);
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
