using Microsoft.Data.Sqlite;
using CodeScan.Models;

namespace CodeScan.Services;

public sealed class SqliteStore : IResultStore, IDisposable
{
    private readonly SqliteConnection _conn;

    public SqliteStore(string? dbPath = null)
    {
        dbPath ??= AppPaths.DbPath;
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
                total_size INTEGER DEFAULT 0,
                addinfo TEXT
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

            CREATE TABLE IF NOT EXISTS graph_nodes (
                id INTEGER PRIMARY KEY,
                scan_id INTEGER NOT NULL REFERENCES scans(id),
                kind TEXT NOT NULL,
                stable_key TEXT NOT NULL,
                label TEXT NOT NULL,
                path TEXT,
                detail TEXT,
                UNIQUE(scan_id, stable_key)
            );

            CREATE TABLE IF NOT EXISTS graph_edges (
                id INTEGER PRIMARY KEY,
                scan_id INTEGER NOT NULL REFERENCES scans(id),
                from_node_id INTEGER NOT NULL REFERENCES graph_nodes(id),
                to_node_id INTEGER NOT NULL REFERENCES graph_nodes(id),
                kind TEXT NOT NULL,
                label TEXT,
                UNIQUE(scan_id, from_node_id, to_node_id, kind)
            );

            CREATE INDEX IF NOT EXISTS idx_graph_nodes_scan ON graph_nodes(scan_id);
            CREATE INDEX IF NOT EXISTS idx_graph_nodes_kind ON graph_nodes(kind);
            CREATE INDEX IF NOT EXISTS idx_graph_nodes_label ON graph_nodes(label);
            CREATE INDEX IF NOT EXISTS idx_graph_edges_scan ON graph_edges(scan_id);
            CREATE INDEX IF NOT EXISTS idx_graph_edges_from ON graph_edges(from_node_id);
            CREATE INDEX IF NOT EXISTS idx_graph_edges_to ON graph_edges(to_node_id);
            """;
        cmd.ExecuteNonQuery();

        // Migration: add addinfo column if missing (existing DBs)
        try
        {
            cmd.CommandText = "ALTER TABLE projects ADD COLUMN addinfo TEXT";
            cmd.ExecuteNonQuery();
        }
        catch { /* column already exists */ }

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
        var baseDir = AppPaths.GetLogDir();
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
        cmd.CommandText = "SELECT id, root_path, last_scanned_at, file_count, dir_count, total_size, addinfo FROM projects ORDER BY last_scanned_at DESC";
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
                TotalSize = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                AddInfo = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return list;
    }

    public ProjectInfo? GetProject(long projectId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, root_path, last_scanned_at, file_count, dir_count, total_size, addinfo FROM projects WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", projectId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new ProjectInfo
        {
            Id = reader.GetInt64(0),
            RootPath = reader.GetString(1),
            LastScannedAt = reader.IsDBNull(2) ? null : reader.GetString(2),
            FileCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            DirCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            TotalSize = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
            AddInfo = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
    }

    public void SetProjectAddInfo(long projectId, string info)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE projects SET addinfo = @info WHERE id = @id";
        cmd.Parameters.AddWithValue("@info", info);
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.ExecuteNonQuery();
    }

    public void SetProjectPath(long projectId, string newPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE projects SET root_path = @path WHERE id = @id";
        cmd.Parameters.AddWithValue("@path", newPath);
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.ExecuteNonQuery();
    }

    public bool DeleteProject(long projectId)
    {
        using var tx = _conn.BeginTransaction();

        // Delete search_index entries for this project's scans
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM search_index WHERE scan_id IN (SELECT id FROM scans WHERE project_id = @pid)";
        cmd.Parameters.AddWithValue("@pid", projectId);
        try { cmd.ExecuteNonQuery(); } catch { /* FTS may not exist */ }

        cmd.CommandText = "DELETE FROM graph_edges WHERE scan_id IN (SELECT id FROM scans WHERE project_id = @pid)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DELETE FROM graph_nodes WHERE scan_id IN (SELECT id FROM scans WHERE project_id = @pid)";
        cmd.ExecuteNonQuery();

        // Delete comments -> methods -> files -> project_docs -> scans -> project
        cmd.CommandText = """
            DELETE FROM comments WHERE file_id IN (
                SELECT f.id FROM files f JOIN scans s ON f.scan_id = s.id WHERE s.project_id = @pid);
            DELETE FROM methods WHERE file_id IN (
                SELECT f.id FROM files f JOIN scans s ON f.scan_id = s.id WHERE s.project_id = @pid);
            DELETE FROM files WHERE scan_id IN (SELECT id FROM scans WHERE project_id = @pid);
            DELETE FROM project_docs WHERE scan_id IN (SELECT id FROM scans WHERE project_id = @pid);
            DELETE FROM scans WHERE project_id = @pid;
            DELETE FROM projects WHERE id = @pid;
            """;
        var deleted = cmd.ExecuteNonQuery();

        tx.Commit();
        return deleted > 0;
    }

    public List<ScanInfo> GetProjectScans(long projectId, int limit = 10)
    {
        var list = new List<ScanInfo>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, scanned_at, file_count, dir_count, total_size
            FROM scans WHERE project_id = @pid
            ORDER BY scanned_at DESC LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@lim", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ScanInfo
            {
                Id = reader.GetInt64(0),
                ScannedAt = reader.GetString(1),
                FileCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                DirCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                TotalSize = reader.IsDBNull(4) ? 0 : reader.GetInt64(4)
            });
        }
        return list;
    }

    public int GetProjectMethodCount(long projectId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM methods m
            JOIN files f ON m.file_id = f.id
            JOIN scans s ON f.scan_id = s.id
            WHERE s.project_id = @pid
            AND s.id = (SELECT MAX(id) FROM scans WHERE project_id = @pid)
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        var result = cmd.ExecuteScalar();
        return result is long v ? (int)v : 0;
    }

    public int GetProjectCommentCount(long projectId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM comments c
            JOIN files f ON c.file_id = f.id
            JOIN scans s ON f.scan_id = s.id
            WHERE s.project_id = @pid
            AND s.id = (SELECT MAX(id) FROM scans WHERE project_id = @pid)
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        var result = cmd.ExecuteScalar();
        return result is long v ? (int)v : 0;
    }

    public List<string> GetProjectDocs(long projectId)
    {
        var docs = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT pd.doc_path FROM project_docs pd
            JOIN scans s ON pd.scan_id = s.id
            WHERE s.project_id = @pid
            ORDER BY pd.doc_path
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            docs.Add(reader.GetString(0));
        return docs;
    }

    // ========================
    // Project detail queries (latest scan)
    // ========================
    private long GetLatestScanId(long projectId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(id) FROM scans WHERE project_id = @pid";
        cmd.Parameters.AddWithValue("@pid", projectId);
        var result = cmd.ExecuteScalar();
        return result is long v ? v : 0;
    }

    public List<ProjectFileInfo> GetProjectFiles(long projectId)
    {
        var list = new List<ProjectFileInfo>();
        var scanId = GetLatestScanId(projectId);
        if (scanId == 0) return list;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.id, f.relative_path, f.name, f.extension, f.size, f.is_directory, f.depth
            FROM files f
            WHERE f.scan_id = @sid AND f.is_directory = 0
            ORDER BY f.relative_path
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ProjectFileInfo
            {
                Id = reader.GetInt64(0),
                RelativePath = reader.GetString(1),
                Name = reader.GetString(2),
                Extension = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Size = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                Depth = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
            });
        }
        return list;
    }

    public List<ProjectMethodInfo> GetProjectMethods(long projectId)
    {
        var list = new List<ProjectMethodInfo>();
        var scanId = GetLatestScanId(projectId);
        if (scanId == 0) return list;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.class_name, m.method_name, m.start_line, m.end_line,
                   m.last_author, m.last_date, m.commit_summary, f.relative_path
            FROM methods m
            JOIN files f ON m.file_id = f.id
            WHERE f.scan_id = @sid
            ORDER BY f.relative_path, m.start_line
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ProjectMethodInfo
            {
                ClassName = reader.IsDBNull(0) ? "" : reader.GetString(0),
                MethodName = reader.GetString(1),
                StartLine = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                EndLine = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                LastAuthor = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastDate = reader.IsDBNull(5) ? null : reader.GetString(5),
                CommitSummary = reader.IsDBNull(6) ? null : reader.GetString(6),
                FilePath = reader.GetString(7)
            });
        }
        return list;
    }

    public List<ProjectCommentInfo> GetProjectComments(long projectId)
    {
        var list = new List<ProjectCommentInfo>();
        var scanId = GetLatestScanId(projectId);
        if (scanId == 0) return list;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.comment, c.nearby_code, c.start_line, c.end_line, f.relative_path
            FROM comments c
            JOIN files f ON c.file_id = f.id
            WHERE f.scan_id = @sid
            ORDER BY f.relative_path, c.start_line
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ProjectCommentInfo
            {
                Comment = reader.GetString(0),
                NearbyCode = reader.IsDBNull(1) ? "" : reader.GetString(1),
                StartLine = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                EndLine = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                FilePath = reader.GetString(4)
            });
        }
        return list;
    }

    public List<ProjectDocContent> GetProjectDocContents(long projectId)
    {
        var list = new List<ProjectDocContent>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT pd.doc_path, pd.content FROM project_docs pd
            JOIN scans s ON pd.scan_id = s.id
            WHERE s.project_id = @pid
            AND s.id = (SELECT MAX(id) FROM scans WHERE project_id = @pid)
            ORDER BY pd.doc_path
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ProjectDocContent
            {
                DocPath = reader.GetString(0),
                Content = reader.IsDBNull(1) ? "" : reader.GetString(1)
            });
        }
        return list;
    }

    public List<string> GetProjectExtensions(long projectId)
    {
        var list = new List<string>();
        var scanId = GetLatestScanId(projectId);
        if (scanId == 0) return list;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT extension, COUNT(*) as cnt
            FROM files WHERE scan_id = @sid AND is_directory = 0 AND extension != ''
            GROUP BY extension ORDER BY cnt DESC
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add($"{reader.GetString(0)} ({reader.GetInt32(1)})");
        return list;
    }

    public List<string> GetProjectAuthors(long projectId)
    {
        var list = new List<string>();
        var scanId = GetLatestScanId(projectId);
        if (scanId == 0) return list;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.last_author, COUNT(*) as cnt
            FROM methods m JOIN files f ON m.file_id = f.id
            WHERE f.scan_id = @sid AND m.last_author IS NOT NULL
            GROUP BY m.last_author ORDER BY cnt DESC
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add($"{reader.GetString(0)} ({reader.GetInt32(1)} methods)");
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
        var project = GetProject(projectId);
        var projectNodeId = UpsertGraphNode(
            scanId,
            $"project:{projectId}",
            "project",
            project != null ? Path.GetFileName(project.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : $"Project {projectId}",
            project?.RootPath ?? "",
            $"Project #{projectId}");
        var pathNodeIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

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

            var entryKind = entry.IsDirectory ? "directory" : "file";
            var entryNodeId = UpsertGraphNode(
                scanId,
                $"path:{entry.RelativePath}",
                entryKind,
                entry.Name,
                entry.RelativePath,
                entry.IsDirectory ? "Directory" : $"{entry.Extension} {entry.Size} bytes");
            pathNodeIds[entry.RelativePath] = entryNodeId;

            var parentNodeId = projectNodeId;
            var parentPath = Path.GetDirectoryName(entry.RelativePath);
            if (!string.IsNullOrEmpty(parentPath) && pathNodeIds.TryGetValue(parentPath, out var foundParentNodeId))
                parentNodeId = foundParentNodeId;
            InsertGraphEdge(scanId, parentNodeId, entryNodeId, "contains", "contains");

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

                    var classNodeId = entryNodeId;
                    if (!string.IsNullOrWhiteSpace(m.ClassName))
                    {
                        classNodeId = UpsertGraphNode(
                            scanId,
                            $"class:{entry.RelativePath}:{m.ClassName}",
                            "class",
                            m.ClassName,
                            entry.RelativePath,
                            $"Class in {entry.RelativePath}");
                        InsertGraphEdge(scanId, entryNodeId, classNodeId, "contains", "contains");
                    }

                    var methodNodeId = UpsertGraphNode(
                        scanId,
                        $"method:{fileId}:{m.ClassName}:{m.MethodName}:{m.StartLine}",
                        "method",
                        string.IsNullOrWhiteSpace(m.ClassName) ? m.MethodName : $"{m.ClassName}.{m.MethodName}",
                        entry.RelativePath,
                        $"L{m.StartLine}-{m.EndLine} {m.LastCommitSummary ?? ""}".Trim());
                    InsertGraphEdge(scanId, classNodeId, methodNodeId, "defines", "defines");

                    if (!string.IsNullOrWhiteSpace(m.LastAuthor))
                    {
                        var authorNodeId = UpsertGraphNode(
                            scanId,
                            $"author:{m.LastAuthor.Trim().ToLowerInvariant()}",
                            "author",
                            m.LastAuthor.Trim(),
                            "",
                            "Git blame author");
                        InsertGraphEdge(scanId, authorNodeId, methodNodeId, "authored", "authored");
                    }
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

                    var commentNodeId = UpsertGraphNode(
                        scanId,
                        $"comment:{fileId}:{c.StartLine}",
                        "comment",
                        $"Comment L{c.StartLine}",
                        entry.RelativePath,
                        c.Comment.Length > 240 ? c.Comment[..240] : c.Comment);
                    InsertGraphEdge(scanId, entryNodeId, commentNodeId, "has_comment", "comment");
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

        var projectId = GetProjectIdForScan(scanId);
        if (projectId > 0)
        {
            var project = GetProject(projectId);
            var projectNodeId = UpsertGraphNode(
                scanId,
                $"project:{projectId}",
                "project",
                project != null ? Path.GetFileName(project.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : $"Project {projectId}",
                project?.RootPath ?? "",
                $"Project #{projectId}");
            var docNodeId = UpsertGraphNode(
                scanId,
                $"doc:{docPath}",
                "doc",
                Path.GetFileName(docPath),
                docPath,
                content.Length > 240 ? content[..240] : content);
            InsertGraphEdge(scanId, projectNodeId, docNodeId, "documents", "documents");
        }
    }

    // ========================
    // Search
    // ========================
    // 검색 전략:
    //   1차: FTS5 전문검색 (trigram tokenizer, 한국어 부분문자열 매칭 지원)
    //   2차: FTS5 결과 0건이면 LIKE 폴백 (짧은 쿼리나 FTS 미지원 환경)
    // 정렬: 최신 스캔 우선 (scan_id DESC) + 관련도 (FTS rank)
    // 범위: 전체 스캔 데이터 대상 (최신 한정 없음 — 과거 스캔에만 존재하는 데이터도 검색 가능)
    public List<SearchResult> Search(string query, string? type = null, int limit = 30, long? projectId = null)
    {
        var results = SearchFts(query, type, limit, projectId);
        if (results.Count == 0)
            results = SearchLike(query, type, limit, projectId);
        return results;
    }

    private List<SearchResult> SearchFts(string query, string? type, int limit, long? projectId)
    {
        var results = new List<SearchResult>();
        try
        {
            using var cmd = _conn.CreateCommand();
            var typeFilter = type != null ? "AND type = @type" : "";
            var projectFilter = projectId.HasValue
                ? "AND scan_id IN (SELECT id FROM scans WHERE project_id = @pid)"
                : "";
            cmd.CommandText = $"""
                SELECT type, name, snippet(search_index, 2, '>>>', '<<<', '...', 40) as excerpt, path, scan_id
                FROM search_index
                WHERE search_index MATCH @q {typeFilter} {projectFilter}
                ORDER BY scan_id DESC, rank
                LIMIT @lim
                """;
            var safeQuery = string.Join(" ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(term => $"\"{term.Replace("\"", "")}\""));
            cmd.Parameters.AddWithValue("@q", safeQuery);
            cmd.Parameters.AddWithValue("@lim", limit);
            if (type != null) cmd.Parameters.AddWithValue("@type", type);
            if (projectId.HasValue) cmd.Parameters.AddWithValue("@pid", projectId.Value);

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

    private List<SearchResult> SearchLike(string query, string? type, int limit, long? projectId)
    {
        var results = new List<SearchResult>();
        using var cmd = _conn.CreateCommand();
        var typeFilter = type != null ? "AND type = @type" : "";
        var projectFilter = projectId.HasValue
            ? "AND scan_id IN (SELECT id FROM scans WHERE project_id = @pid)"
            : "";
        var like = $"%{query}%";
        cmd.CommandText = $"""
            SELECT type, name, content, path, scan_id
            FROM search_index
            WHERE (name LIKE @q OR content LIKE @q) {typeFilter} {projectFilter}
            ORDER BY scan_id DESC
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@q", like);
        cmd.Parameters.AddWithValue("@lim", limit);
        if (type != null) cmd.Parameters.AddWithValue("@type", type);
        if (projectId.HasValue) cmd.Parameters.AddWithValue("@pid", projectId.Value);

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
    // Graph search
    // ========================
    public GraphData SearchGraph(string query, long? projectId = null, int depth = 1, int limit = 80)
    {
        depth = Math.Clamp(depth, 0, 4);
        limit = Math.Clamp(limit, 1, 300);

        var matchedIds = FindGraphNodeIds(query, projectId, limit);
        if (matchedIds.Count == 0)
            return new GraphData();

        var selectedIds = new HashSet<long>(matchedIds);
        var frontier = new HashSet<long>(matchedIds);

        for (int i = 0; i < depth && selectedIds.Count < 300; i++)
        {
            var neighbors = GetGraphNeighborIds(frontier, projectId, 300 - selectedIds.Count);
            neighbors.ExceptWith(selectedIds);
            if (neighbors.Count == 0) break;
            selectedIds.UnionWith(neighbors);
            frontier = neighbors;
        }

        var nodes = GetGraphNodes(selectedIds);
        var edges = GetGraphEdges(selectedIds, projectId);

        return new GraphData { Nodes = nodes, Edges = edges };
    }

    private List<long> FindGraphNodeIds(string query, long? projectId, int limit)
    {
        var ids = new List<long>();
        using var cmd = _conn.CreateCommand();
        var projectFilter = projectId.HasValue
            ? "AND n.scan_id IN (SELECT id FROM scans WHERE project_id = @pid)"
            : "";
        var latestFilter = projectId.HasValue
            ? "AND n.scan_id = (SELECT MAX(id) FROM scans WHERE project_id = @pid)"
            : "AND n.scan_id IN (SELECT MAX(id) FROM scans GROUP BY project_id)";
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var queryFilter = hasQuery
            ? "AND (n.label LIKE @q OR n.path LIKE @q OR n.detail LIKE @q OR n.kind LIKE @q)"
            : "";
        cmd.CommandText = $"""
            SELECT n.id
            FROM graph_nodes n
            WHERE 1 = 1 {projectFilter} {latestFilter} {queryFilter}
            ORDER BY n.scan_id DESC,
                     CASE n.kind
                        WHEN 'project' THEN 0
                        WHEN 'directory' THEN 1
                        WHEN 'file' THEN 2
                        WHEN 'class' THEN 3
                        WHEN 'method' THEN 4
                        ELSE 5
                     END,
                     n.label
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@lim", limit);
        if (projectId.HasValue) cmd.Parameters.AddWithValue("@pid", projectId.Value);
        if (hasQuery) cmd.Parameters.AddWithValue("@q", $"%{query}%");

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    private HashSet<long> GetGraphNeighborIds(HashSet<long> nodeIds, long? projectId, int limit)
    {
        var result = new HashSet<long>();
        if (nodeIds.Count == 0 || limit <= 0) return result;

        using var cmd = _conn.CreateCommand();
        var inClause = AddIdParameters(cmd, nodeIds, "@n");
        var projectFilter = projectId.HasValue
            ? "AND e.scan_id IN (SELECT id FROM scans WHERE project_id = @pid)"
            : "";
        cmd.CommandText = $"""
            SELECT e.from_node_id, e.to_node_id
            FROM graph_edges e
            WHERE (e.from_node_id IN ({inClause}) OR e.to_node_id IN ({inClause})) {projectFilter}
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@lim", limit * 2);
        if (projectId.HasValue) cmd.Parameters.AddWithValue("@pid", projectId.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read() && result.Count < limit)
        {
            result.Add(reader.GetInt64(0));
            if (result.Count < limit)
                result.Add(reader.GetInt64(1));
        }
        return result;
    }

    private List<GraphNode> GetGraphNodes(HashSet<long> nodeIds)
    {
        var nodes = new List<GraphNode>();
        if (nodeIds.Count == 0) return nodes;

        using var cmd = _conn.CreateCommand();
        var inClause = AddIdParameters(cmd, nodeIds, "@id");
        cmd.CommandText = $"""
            SELECT id, scan_id, kind, label, path, detail
            FROM graph_nodes
            WHERE id IN ({inClause})
            ORDER BY scan_id DESC, kind, label
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            nodes.Add(new GraphNode
            {
                Id = reader.GetInt64(0),
                ScanId = reader.GetInt64(1),
                Kind = reader.GetString(2),
                Label = reader.GetString(3),
                Path = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Detail = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }
        return nodes;
    }

    private List<GraphEdge> GetGraphEdges(HashSet<long> nodeIds, long? projectId)
    {
        var edges = new List<GraphEdge>();
        if (nodeIds.Count == 0) return edges;

        using var cmd = _conn.CreateCommand();
        var inClause = AddIdParameters(cmd, nodeIds, "@id");
        var projectFilter = projectId.HasValue
            ? "AND e.scan_id IN (SELECT id FROM scans WHERE project_id = @pid)"
            : "";
        cmd.CommandText = $"""
            SELECT e.id, e.scan_id, e.from_node_id, e.to_node_id, e.kind, e.label
            FROM graph_edges e
            WHERE e.from_node_id IN ({inClause}) AND e.to_node_id IN ({inClause}) {projectFilter}
            ORDER BY e.scan_id DESC, e.kind
            LIMIT 500
            """;
        if (projectId.HasValue) cmd.Parameters.AddWithValue("@pid", projectId.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            edges.Add(new GraphEdge
            {
                Id = reader.GetInt64(0),
                ScanId = reader.GetInt64(1),
                From = reader.GetInt64(2),
                To = reader.GetInt64(3),
                Kind = reader.GetString(4),
                Label = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }
        return edges;
    }

    private static string AddIdParameters(SqliteCommand cmd, IEnumerable<long> ids, string prefix)
    {
        var names = new List<string>();
        var i = 0;
        foreach (var id in ids)
        {
            var name = $"{prefix}{i++}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, id);
        }
        return string.Join(",", names);
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

    private long UpsertGraphNode(long scanId, string stableKey, string kind, string label, string path, string detail)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO graph_nodes(scan_id, kind, stable_key, label, path, detail)
            VALUES(@sid, @kind, @key, @label, @path, @detail)
            ON CONFLICT(scan_id, stable_key) DO UPDATE SET
                kind = excluded.kind,
                label = excluded.label,
                path = excluded.path,
                detail = excluded.detail
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@key", stableKey);
        cmd.Parameters.AddWithValue("@label", label);
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@detail", detail);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM graph_nodes WHERE scan_id = @sid AND stable_key = @key";
        return (long)cmd.ExecuteScalar()!;
    }

    private void InsertGraphEdge(long scanId, long fromNodeId, long toNodeId, string kind, string label)
    {
        if (fromNodeId <= 0 || toNodeId <= 0 || fromNodeId == toNodeId) return;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO graph_edges(scan_id, from_node_id, to_node_id, kind, label)
            VALUES(@sid, @from, @to, @kind, @label)
            ON CONFLICT(scan_id, from_node_id, to_node_id, kind) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("@sid", scanId);
        cmd.Parameters.AddWithValue("@from", fromNodeId);
        cmd.Parameters.AddWithValue("@to", toNodeId);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@label", label);
        cmd.ExecuteNonQuery();
    }

    private long GetProjectIdForScan(long scanId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT project_id FROM scans WHERE id = @sid";
        cmd.Parameters.AddWithValue("@sid", scanId);
        var result = cmd.ExecuteScalar();
        return result is long v ? v : 0;
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
    public string? AddInfo { get; init; }
}

public sealed class ScanInfo
{
    public long Id { get; init; }
    public required string ScannedAt { get; init; }
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

public sealed class ProjectFileInfo
{
    public long Id { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public string Extension { get; init; } = "";
    public long Size { get; init; }
    public int Depth { get; init; }
}

public sealed class ProjectMethodInfo
{
    public string ClassName { get; init; } = "";
    public required string MethodName { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string? LastAuthor { get; init; }
    public string? LastDate { get; init; }
    public string? CommitSummary { get; init; }
    public required string FilePath { get; init; }
}

public sealed class ProjectCommentInfo
{
    public required string Comment { get; init; }
    public string NearbyCode { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public required string FilePath { get; init; }
}

public sealed class ProjectDocContent
{
    public required string DocPath { get; init; }
    public required string Content { get; init; }
}
