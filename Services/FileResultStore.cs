namespace CodeScan.Services;

public sealed class FileResultStore : IResultStore
{
    private readonly string _baseDir;

    public FileResultStore(string baseDir)
    {
        _baseDir = baseDir;
    }

    public void Save(string command, string content)
    {
        Directory.CreateDirectory(_baseDir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var fileName = $"{timestamp}_{command}.log";
        var filePath = Path.Combine(_baseDir, fileName);
        File.WriteAllText(filePath, content);
        Console.WriteLine($"[devmode] saved: {filePath}");
    }
}
