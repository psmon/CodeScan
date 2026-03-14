using Microsoft.Data.Sqlite;
using CodeScan.Models;

namespace CodeScan.Services;

public sealed class SqliteStore : IResultStore, IDisposable
{
    private readonly SqliteConnection _conn;

    public SqliteStore(string? dbPath = null)
    {
        dbPath ??= Path.Combine(AppContext.BaseDirectory, "codescan.db");
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY,
                root_path TEXT NOT NULL UNIQUE,
                last_scanned_at TEXT,
                file_count INTEGER DEFAULT 0,
                dir_count INTEGER DEFAULT 0,
                total_size INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS scans (
                id INTEGER PRIMARY KEY,
                project_id INTEGER REFERENCES projects(id),
                scanned_at TEXT NOT NULL,
                file_count INTEGER,
                dir_count INTEGER,
                total_size INTEGER
            );

            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY,
                scan_id INTEGER REFERENCES scans(id),
                relative_path TEXT NOT NULL,
                name TEXT NOT NULL,
                extension TEXT,
                size INTEGER,
                is_directory INTEGER,
                depth INTEGER
            );

            CREATE TABLE IF NOT EXISTS methods (
                id INTEGER PRIMARY KEY,
                file_id INTEGER REFERENCES files(id),
                class_name TEXT,
                method_name TEXT NOT NULL,
                start_line INTEGER,
                end_line INTEGER,
                last_author TEXT,
                last_date TEXT,
                commit_summary TEXT
            );

            CREATE TABLE IF NOT EXISTS project_docs (
                id INTEGER PRIMARY KEY,
                scan_id INTEGER REFERENCES scans(id),
                doc_path TEXT NOT NULL,
                content TEXT
            );

            CREATE TABLE IF NOT EXISTS comments (
                id INTEGER PRIMARY KEY,
                file_id INTEGER REFERENCES files(id),
                comment TEXT NOT NULL,
                nearby_code TEXT,
                before_code TEXT,
                start_line INTEGER,
                end_line INTEGER
            );

            CREATE INDEX IF NOT EXISTS idx_files_scan ON files(scan_id);
            CREATE INDEX IF NOT EXISTS idx_comments_file ON comments(file_id);
            CREATE INDEX IF NOT EXISTS idx_methods_file ON methods(file_id);
            CREATE INDEX IF NOT EXISTS idx_methods_name ON methods(method_name);
            CREATE INDEX IF NOT EXISTS idx_methods_author ON methods(last_author);
            CREATE INDEX IF NOT EXISTS idx_files_ext ON files(extension);
            """;
        cmd.ExecuteNonQuery();

        // FTS5 for full-text search (trigram tokenizer for Korean substring matching)
        try
        {
            cmd.CommandText = """
                CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
                    type, name, content, path, scan_id UNINDEXED,
                    tokenize='trigram'
                );
                """;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Fallback: trigram not available, use default tokenizer
            try
            {
                cmd.CommandText = """
                    CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
                        type, name, content, path, scan_id UNINDEXED
                    );
                    """;
                cmd.ExecuteNonQuery();
            }
            catch { /* FTS5 not available */ }
        }
    }

    // ========================
    // IResultStore (legacy file compat)
    // ========================
    public void Save(string command, string content)
    {
        // Also save as file for backward compat
        var baseDir = Path.Combine(
            Path.GetDirectoryName(_conn.DataSource) ?? Directory.GetCurrentDirectory(),
            "TestFileDB");
        Directory.CreateDirectory(baseDir);
        var ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var filePath = Path.Combine(baseDir, $"{ts}_{command}.log");
        File.WriteAllText(filePath, content);
    }

    // ========================
    // Project management
    // ========================
    public long UpsertProject(string rootPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO projects(root_path) VALUES(@p) ON CONFLICT(root_path) DO NOTHING";
        cmd.Parameters.AddWithValue("@p", rootPath);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM projects WHERE root_path = @p";
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateProject(long projectId, int fileCount, int dirCount, long totalSize)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE projects SET last_scanned_at = @t, file_count = @fc, dir_count = @dc, total_size = @ts
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@fc", fileCount);
        cmd.Parameters.AddWithValue("@dc", dirCount);
        cmd.Parameters.AddWithValue("@ts", totalSize);
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.ExecuteNonQuery();
    }

    public List<ProjectInfo> GetProjects()
    {
        var list = new List<ProjectInfo>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, root_path, last_scanned_at, file_count, dir_count, total_size FROM projects ORDER BY last_scanned_at DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ProjectInfo
            {
                Id = reader.GetInt64(0),
                RootPath = reader.GetString(1),
                LastScannedAt = reader.IsDBNull(2) ? null : reader.GetString(2),
                FileCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                DirCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                TotalSize = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
            });
        }
        return list;
    }

    // ========================
    // Scan storage
    // ========================
    public long InsertScan(long projectId, List<FileEntry> entries)
    {
        using var tx = _conn.BeginTransaction();

        var fileCount = entries.Count(e => !e.IsDirectory);
        var dirCount = entries.Count(e => e.IsDirectory);
        var totalSize = entries.Where(e => !e.IsDirectory).Sum(e => e.Size);

        // Insert scan
        using var scanCmd = _conn.CreateCommand();
        scanCmd.CommandText = """
            INSERT INTO scans(project_id, scanned_at, file_count, dir_count, total_size)
            VALUES(@pid, @t, @fc, @dc, @ts)
            """;
        scanCmd.Parameters.AddWithValue("@pid", projectId);
        scanCmd.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        scanCmd.Parameters.AddWithValue("@fc", fileCount);
        scanCmd.Parameters.AddWithValue("@dc", dirCount);
        scanCmd.Parameters.AddWithValue("@ts", totalSize);
        scanCmd.ExecuteNonQuery();

        var scanId = GetLastId();

        // Batch insert files + methods
        foreach (var entry in entries)
        {
            using var fileCmd = _conn.CreateCommand();
            fileCmd.CommandText = """
                INSERT INTO files(scan_id, relative_path, name, extension, size, is_directory, depth)
                VALUES(@sid, @rp, @n, @ext, @sz, @dir, @d)
                """;
            fileCmd.Parameters.AddWithValue("@sid", scanId);
            fileCmd.Parameters.AddWithValue("@rp", entry.RelativePath);
            fileCmd.Parameters.AddWithValue("@n", entry.Name);
            fileCmd.Parameters.AddWithValue("@ext", entry.Extension);
            fileCmd.Parameters.AddWithValue("@sz", entry.Size);
            fileCmd.Parameters.AddWithValue("@dir", entry.IsDirectory ? 1 : 0);
            fileCmd.Parameters.AddWithValue("@d", entry.Depth);
            fileCmd.ExecuteNonQuery();
            var fileId = GetLastId();

            if (entry.Methods.Count > 0)
            {
                foreach (var m in entry.Methods)
                {
                    using var mCmd = _conn.CreateCommand();
                    mCmd.CommandText = """
                        INSERT INTO methods(file_id, class_name, method_name, start_line, end_line, last_author, last_date, commit_summary)
                        VALUES(@fid, @cls, @mn, @sl, @el, @auth, @dt, @cs)
                        """;
                    mCmd.Parameters.AddWithValue("@fid", fileId);
                    mCmd.Parameters.AddWithValue("@cls", m.ClassName);
                    mCmd.Parameters.AddWithValue("@mn", m.MethodName);
                    mCmd.Parameters.AddWithValue("@sl", m.StartLine);
                    mCmd.Parameters.AddWithValue("@el", m.EndLine);
                    mCmd.Parameters.AddWithValue("@auth", (object?)m.LastAuthor ?? DBNull.Value);
                    mCmd.Parameters.AddWithValue("@dt", (object?)m.LastDate ?? DBNull.Value);
                    mCmd.Parameters.AddWithValue("@cs", (object?)m.LastCommitSummary ?? DBNull.Value);
                    mCmd.ExecuteNonQuery();

                    // FTS index
                    IndexForSearch(scanId, "method", m.MethodName,
                        m.LastCommitSummary ?? "", entry.RelativePath);
                }
            }

            // Comments
            if (entry.Comments.Count > 0)
            {
                foreach (var c in entry.Comments)
                {
                    using var cCmd = _conn.CreateCommand();
                    cCmd.CommandText = """
                        INSERT INTO comments(file_id, comment, nearby_code, before_code, start_line, end_line)
                        VALUES(@fid, @cm, @nc, @bc, @sl, @el)
                        """;
                    cCmd.Parameters.AddWithValue("@fid", fileId);
                    cCmd.Parameters.AddWithValue("@cm", c.Comment);
                    cCmd.Parameters.AddWithValue("@nc", c.NearbyCode);
                    cCmd.Parameters.AddWithValue("@bc", c.BeforeCode);
                    cCmd.Parameters.AddWithValue("@sl", c.StartLine);
                    cCmd.Parameters.AddWithValue("@el", c.EndLine);
                    cCmd.ExecuteNonQuery();

                    // FTS: searchable by comment text, shows nearby code as excerpt
                    var searchContent = $"{c.Comment} | {c.NearbyCode}";
                    IndexForSearch(scanId, "comment", $"L{c.StartLine}", searchContent, entry.RelativePath);
                }
            }

            // FTS index files
            if (!entry.IsDirectory)
            {
                IndexForSearch(scanId, "file", entry.Name, "", entry.RelativePath);
            }
        }

        // Update project stats
        UpdateProject(projectId, fileCount, dirCount, totalSize);

        tx.Commit();
        return scanId;
    }

    public void InsertProjectDoc(long scanId, string docPath, string content)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO project_docs(scan_id, doc_path, content) VALUES(@sid, @dp, @c)";
        cmd.Parameters.AddWithValue("@sid", scanId);
        cmd.Parameters.AddWithValue("@dp", docPath);
        cmd.Parameters.AddWithValue("@c", content);
        cmd.ExecuteNonQuery();

        IndexForSearch(scanId, "doc", Path.GetFileName(docPath), content, docPath);
    }

    // ========================
    // Search
    // ========================
    public List<SearchResult> Search(string query, string? type = null, int limit = 30)
    {
        // Try FTS5 first, fallback to LIKE for short queries (< 3 chars per term)
        var results = SearchFts(query, type, limit);
        if (results.Count == 0)
            results = SearchLike(query, type, limit);
        return results;
    }

    private List<SearchResult> SearchFts(string query, string? type, int limit)
    {
        var results = new List<SearchResult>();
        try
        {
            using var cmd = _conn.CreateCommand();
            var typeFilter = type != null ? "AND type = @type" : "";
            cmd.CommandText = $"""
                SELECT type, name, snippet(search_index, 2, '>>>', '<<<', '...', 40) as excerpt, path, scan_id
                FROM search_index
                WHERE search_index MATCH @q {typeFilter}
                ORDER BY rank
                LIMIT @lim
                """;
            var safeQuery = string.Join(" ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(term => $"\"{term.Replace("\"", "")}\""));
            cmd.Parameters.AddWithValue("@q", safeQuery);
            cmd.Parameters.AddWithValue("@lim", limit);
            if (type != null) cmd.Parameters.AddWithValue("@type", type);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SearchResult
                {
                    Type = reader.GetString(0),
                    Name = reader.GetString(1),
                    Excerpt = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Path = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ScanId = reader.GetInt64(4)
                });
            }
        }
        catch { /* FTS error, fall through to LIKE */ }
        return results;
    }

    private List<SearchResult> SearchLike(string query, string? type, int limit)
    {
        var results = new List<SearchResult>();
        using var cmd = _conn.CreateCommand();
        var typeFilter = type != null ? "AND type = @type" : "";
        var like = $"%{query}%";
        cmd.CommandText = $"""
            SELECT type, name, content, path, scan_id
            FROM search_index
            WHERE (name LIKE @q OR content LIKE @q) {typeFilter}
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@q", like);
        cmd.Parameters.AddWithValue("@lim", limit);
        if (type != null) cmd.Parameters.AddWithValue("@type", type);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var content = reader.IsDBNull(2) ? "" : reader.GetString(2);
            // Extract a short excerpt around the match
            var excerpt = ExtractExcerpt(content, query, 60);

            results.Add(new SearchResult
            {
                Type = reader.GetString(0),
                Name = reader.GetString(1),
                Excerpt = excerpt,
                Path = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ScanId = reader.GetInt64(4)
            });
        }
        return results;
    }

    private static string ExtractExcerpt(string content, string query, int maxLen)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length > maxLen ? content[..maxLen] + "..." : content;

        var start = Math.Max(0, idx - 20);
        var end = Math.Min(content.Length, idx + query.Length + 40);
        var excerpt = content[start..end];
        if (start > 0) excerpt = "..." + excerpt;
        if (end < content.Length) excerpt += "...";
        return excerpt;
    }

    // ========================
    // Helpers
    // ========================
    private void IndexForSearch(long scanId, string type, string name, string content, string path)
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO search_index(type, name, content, path, scan_id) VALUES(@t, @n, @c, @p, @sid)";
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@c", content);
            cmd.Parameters.AddWithValue("@p", path);
            cmd.Parameters.AddWithValue("@sid", scanId);
            cmd.ExecuteNonQuery();
        }
        catch { /* FTS not available */ }
    }

    private long GetLastId()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}

public sealed class ProjectInfo
{
    public long Id { get; init; }
    public required string RootPath { get; init; }
    public string? LastScannedAt { get; init; }
    public int FileCount { get; init; }
    public int DirCount { get; init; }
    public long TotalSize { get; init; }
}

public sealed class SearchResult
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public string Excerpt { get; init; } = "";
    public string Path { get; init; } = "";
    public long ScanId { get; init; }
}
