using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Terminal.Gui;
using CodeScan.Services;

namespace CodeScan.Tui;

public class TuiApp
{
    // Windows Console API: disable mouse input at OS level
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;

    private static void DisableConsoleMouse()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var handle = GetStdHandle(STD_INPUT_HANDLE);
        if (GetConsoleMode(handle, out uint mode))
        {
            mode &= ~ENABLE_MOUSE_INPUT;   // disable mouse input
            mode |= ENABLE_QUICK_EDIT_MODE; // re-enable quick edit
            SetConsoleMode(handle, mode);
        }
    }

    // Windows Console: set code page to UTF-8
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);

    public void Run()
    {
        try
        {
            // Force UTF-8 encoding for Korean/CJK support
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetConsoleOutputCP(65001); // UTF-8
                SetConsoleCP(65001);
            }

            DisableConsoleMouse();
            Application.ForceDriver = "NetDriver";
            Application.Init();
            Application.IsMouseDisabled = true;
            DisableConsoleMouse();

            var main = new MainView();
            Application.Run(main);
            main.Dispose();
            Application.Shutdown();
        }
        catch (Exception ex)
        {
            try { Application.Shutdown(); } catch { }

            var logPath = Path.Combine(AppPaths.GetLogDir(), "tui-crash.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fatal:\n{ex}\n\n");

            Console.Error.WriteLine($"TUI crashed. See {logPath}");
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}

public class MainView : Toplevel
{
    private enum Mode { RootSelect, DirBrowse, ScanOptions, Scanning, Results, Projects, SearchInput, SearchResults, ProjectDetail }

    private Mode _mode = Mode.RootSelect;
    private string _currentPath = "";
    private readonly Stack<string> _pathHistory = new();
    private volatile bool _scanning = false;

    // current project context (for project detail/addinfo)
    private long _currentProjectId = 0;

    // scan options
    private bool _optTree = true;
    private bool _optDetail = true;
    private bool _optStats = true;
    private string _optInclude = "";

    // UI
    private readonly Label _titleLabel;
    private readonly Label _pathLabel;
    private readonly Label _hintLabel;
    private readonly ListView _listView;
    private readonly TextView _resultView;
    private readonly Button _btnBack;

    // scan option controls
    private readonly CheckBox _chkTree;
    private readonly CheckBox _chkDetail;
    private readonly CheckBox _chkStats;
    private readonly TextField _txtInclude;
    private readonly Button _btnExecute;
    private readonly Label _lblInclude;

    // search controls
    private readonly Label _lblSearch;
    private readonly TextField _txtSearch;
    private readonly Button _btnSearch;

    // addinfo controls
    private readonly Label _lblAddInfo;
    private readonly TextField _txtAddInfo;
    private readonly Button _btnAddInfo;

    // update path controls
    private readonly Label _lblUpdatePath;
    private readonly TextField _txtUpdatePath;
    private readonly Button _btnUpdatePath;

    private ObservableCollection<string> _listItems = [];
    private List<string> _dirEntries = [];

    public MainView()
    {
        Title = "CodeScan TUI";

        // Black background + white text color scheme
        var darkScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.Black, Color.BrightCyan),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.Black, Color.BrightCyan),
            Disabled = new Terminal.Gui.Attribute(Color.DarkGray, Color.Black)
        };

        var titleScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
            Disabled = new Terminal.Gui.Attribute(Color.DarkGray, Color.Black)
        };

        var hintScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            Disabled = new Terminal.Gui.Attribute(Color.DarkGray, Color.Black)
        };

        ColorScheme = darkScheme;

        _titleLabel = new Label
        {
            Text = "Select Root",
            X = 1, Y = 0,
            ColorScheme = titleScheme
        };

        _pathLabel = new Label { Text = " ", X = 1, Y = 1, ColorScheme = titleScheme };

        _hintLabel = new Label
        {
            Text = "[Enter] Select  [Q] Exit  [H] Home",
            X = 1, Y = Pos.AnchorEnd(1),
            ColorScheme = hintScheme
        };

        // Back button - always visible at top-right
        _btnBack = new Button
        {
            Text = "Q:Back H:Home",
            X = Pos.AnchorEnd(16),
            Y = 0,
        };
        _btnBack.Accepting += (_, _) => HandleBack();

        _listView = new ListView
        {
            X = 1, Y = 3,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
        };
        _listView.SetSource(_listItems);
        _listView.OpenSelectedItem += OnItemSelected;

        _resultView = new TextView
        {
            X = 1, Y = 3,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Visible = false
        };

        _chkTree = new CheckBox
        {
            Text = "Tree output (--tree)",
            X = 3, Y = 4,
            CheckedState = CheckState.Checked,
            Visible = false
        };

        _chkDetail = new CheckBox
        {
            Text = "Method analysis + git blame (--detail)",
            X = 3, Y = 6,
            CheckedState = CheckState.Checked,
            Visible = false
        };

        _chkStats = new CheckBox
        {
            Text = "Include stats (--stats)",
            X = 3, Y = 8,
            CheckedState = CheckState.Checked,
            Visible = false
        };

        _lblInclude = new Label
        {
            Text = "Include extensions (empty = all):",
            X = 3, Y = 10,
            Visible = false
        };

        _txtInclude = new TextField
        {
            Text = " ",
            X = 3, Y = 11,
            Width = 40,
            Visible = false
        };

        _btnExecute = new Button
        {
            Text = ">>> Run Scan <<<",
            X = 3, Y = 13,
            Visible = false
        };
        _btnExecute.Accepting += OnExecuteScan;

        // Search UI (hidden by default)
        _lblSearch = new Label
        {
            Text = "Search query:",
            X = 3, Y = 4,
            Visible = false
        };
        _txtSearch = new TextField
        {
            Text = " ",
            X = 3, Y = 5,
            Width = 50,
            Visible = false
        };
        _btnSearch = new Button
        {
            Text = ">>> Search <<<",
            X = 3, Y = 7,
            Visible = false
        };
        _btnSearch.Accepting += OnExecuteSearch;

        // AddInfo UI (hidden by default)
        _lblAddInfo = new Label
        {
            Text = "Project description (addinfo):",
            X = 3, Y = 4,
            Visible = false
        };
        _txtAddInfo = new TextField
        {
            Text = " ",
            X = 3, Y = 5,
            Width = Dim.Fill(4),
            Visible = false
        };
        _btnAddInfo = new Button
        {
            Text = ">>> Save AddInfo <<<",
            X = 3, Y = 7,
            Visible = false
        };
        _btnAddInfo.Accepting += OnSaveAddInfo;

        // Update Path UI (hidden by default)
        _lblUpdatePath = new Label
        {
            Text = "New project path:",
            X = 3, Y = 4,
            Visible = false
        };
        _txtUpdatePath = new TextField
        {
            Text = " ",
            X = 3, Y = 5,
            Width = Dim.Fill(4),
            Visible = false
        };
        _btnUpdatePath = new Button
        {
            Text = ">>> Update Path <<<",
            X = 3, Y = 7,
            Visible = false
        };
        _btnUpdatePath.Accepting += OnUpdatePath;

        Add(_titleLabel, _pathLabel, _hintLabel, _btnBack, _listView, _resultView,
            _chkTree, _chkDetail, _chkStats, _lblInclude, _txtInclude, _btnExecute,
            _lblSearch, _txtSearch, _btnSearch,
            _lblAddInfo, _txtAddInfo, _btnAddInfo,
            _lblUpdatePath, _txtUpdatePath, _btnUpdatePath);

        // Application-level key intercept - fires BEFORE any view processes keys
        Application.KeyDown += OnGlobalKeyDown;

        ShowRootSelect();
    }

    private static bool IsQKey(Key key)
    {
        // Q, Shift+Q, and Korean 'ㅂ' (0x3142)
        if (key == Key.Q || key == Key.Q.WithShift) return true;
        if ((KeyCode)key == (KeyCode)0x3142) return true; // ㅂ
        return false;
    }

    private static bool IsHomeKey(Key key)
    {
        if (key == Key.H || key == Key.H.WithShift) return true;
        if ((KeyCode)key == (KeyCode)0x314E) return true; // ㅎ (Korean H)
        return false;
    }

    private void OnGlobalKeyDown(object? sender, Key key)
    {
        // block ESC completely - prevent accidental exit
        if (key == Key.Esc)
        {
            key.Handled = true;
            return;
        }

        // ignore when typing in text field
        if (_txtInclude.HasFocus || _txtSearch.HasFocus || _txtAddInfo.HasFocus || _txtUpdatePath.HasFocus) return;

        if (IsQKey(key))
        {
            key.Handled = true;
            HandleBack();
        }
        else if (IsHomeKey(key) && _mode != Mode.Scanning)
        {
            key.Handled = true;
            _pathHistory.Clear();
            ShowRootSelect();
        }
    }

    private void HandleBack()
    {
        switch (_mode)
        {
            case Mode.RootSelect:
                // exit only from root - show confirmation
                Application.RequestStop();
                break;

            case Mode.DirBrowse:
                if (_pathHistory.Count > 0)
                {
                    _currentPath = _pathHistory.Pop();
                    ShowDirBrowse(_currentPath);
                }
                else
                    ShowRootSelect();
                break;

            case Mode.ScanOptions:
                ShowDirBrowse(_currentPath);
                break;

            case Mode.Scanning:
                _scanning = false; // signal cancel
                break;

            case Mode.Results:
                ShowScanOptions();
                break;

            case Mode.SearchInput:
            case Mode.Projects:
                ShowRootSelect();
                break;

            case Mode.SearchResults:
                ShowSearchInput();
                break;

            case Mode.ProjectDetail:
                ShowProjects();
                break;
        }
    }

    private void OnItemSelected(object? sender, ListViewItemEventArgs e)
    {
        var idx = e.Item;
        if (idx < 0 || idx >= _dirEntries.Count) return;
        var selected = _dirEntries[idx];
        if (selected is "__SEP__") return;

        if (_mode == Mode.RootSelect)
        {
            if (selected is "__EXIT__") { Application.RequestStop(); return; }
            if (selected is "__SEARCH__") { ShowSearchInput(); return; }
            if (selected is "__PROJECTS__") { ShowProjects(); return; }
            _pathHistory.Clear();
            _currentPath = selected;
            ShowDirBrowse(_currentPath);
        }
        else if (_mode == Mode.DirBrowse)
        {
            if (selected == "__SCAN__") { ShowScanOptions(); return; }
            if (selected == "__UP__")
            {
                var parent = Directory.GetParent(_currentPath)?.FullName;
                if (parent != null)
                {
                    _pathHistory.Push(_currentPath);
                    _currentPath = parent;
                    ShowDirBrowse(_currentPath);
                }
                return;
            }
            if (Directory.Exists(selected))
            {
                _pathHistory.Push(_currentPath);
                _currentPath = selected;
                ShowDirBrowse(_currentPath);
            }
        }
        else if (_mode == Mode.Projects)
        {
            if (selected is "__HOME__") { ShowRootSelect(); return; }
            if (selected.StartsWith("__DETAIL_"))
            {
                var idStr = selected["__DETAIL_".Length..];
                if (long.TryParse(idStr, out var pid))
                    ShowProjectDetail(pid);
                return;
            }
            if (Directory.Exists(selected))
            {
                _pathHistory.Clear();
                _currentPath = selected;
                ShowDirBrowse(_currentPath);
            }
        }
        else if (_mode == Mode.ProjectDetail)
        {
            if (selected is "__PROJECTS__") { ShowProjects(); return; }
            if (selected is "__ADDINFO__") { ShowAddInfoInput(); return; }
            if (selected is "__UPDATEPATH__") { ShowUpdatePathInput(); return; }
            if (selected is "__DELETE__") { ConfirmDeleteProject(); return; }
            if (selected is "__HOME__") { ShowRootSelect(); return; }
            if (Directory.Exists(selected))
            {
                _pathHistory.Clear();
                _currentPath = selected;
                ShowDirBrowse(_currentPath);
            }
        }
    }

    private void OnExecuteScan(object? sender, CommandEventArgs e)
    {
        if (_scanning) return;

        _optTree = _chkTree.CheckedState == CheckState.Checked;
        _optDetail = _chkDetail.CheckedState == CheckState.Checked;
        _optStats = _chkStats.CheckedState == CheckState.Checked;
        _optInclude = _txtInclude.Text?.ToString()?.Trim() ?? "";

        _scanning = true;
        _mode = Mode.Scanning;
        HideOptions();
        _titleLabel.Text = $"Scanning: {_currentPath}";
        _hintLabel.Text = "[Q] Cancel scan";
        _resultView.Text = "Starting scan...\n";
        _resultView.Visible = true;

        var path = _currentPath;
        var optTree = _optTree;
        var optDetail = _optDetail;
        var optStats = _optStats;
        var optInclude = _optInclude;

        Task.Run(() =>
        {
            try
            {
                ExecuteScanAsync(path, optTree, optDetail, optStats, optInclude);
            }
            catch (Exception ex)
            {
                SafeInvoke(() =>
                {
                    _resultView.Text += $"\nError: {ex.Message}";
                    _scanning = false;
                    _mode = Mode.Results;
                });
            }
        });
    }

    // Safe wrapper for Application.Invoke - never throws
    private static void SafeInvoke(Action action)
    {
        try
        {
            Application.Invoke(() =>
            {
                try { action(); }
                catch { /* UI update failed, ignore */ }
            });
        }
        catch { /* Invoke itself failed, ignore */ }
    }

    private void AppendResult(string text)
    {
        SafeInvoke(() =>
        {
            _resultView.Text += text;
            _resultView.MoveEnd();
        });
    }

    private void ExecuteScanAsync(string path, bool optTree, bool optDetail, bool optStats, string optInclude)
    {
        List<string>? include = null;
        if (!string.IsNullOrWhiteSpace(optInclude))
        {
            include = optInclude.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).ToList();
        }

        AppendResult("[1/3] Scanning directory...\n");

        var scanner = new DirectoryScanner(
            includeExts: include,
            respectGitignore: true);

        var entries = scanner.Scan(path);
        var fileCount = entries.Count(e => !e.IsDirectory);
        var dirCount = entries.Count(e => e.IsDirectory);
        AppendResult($"  Found {fileCount} files in {dirCount} directories\n");

        if (!_scanning) { AppendResult("\n-- Cancelled --\n"); FinishScan(); return; }

        if (optDetail)
        {
            AppendResult("\n[2/3] Analyzing source files + git blame...\n");
            var gitBlame = new GitBlameService(path);
            var sourceFiles = entries.Where(e =>
                !e.IsDirectory && SourceAnalyzer.IsSourceFile(e.Extension)).ToList();

            var total = sourceFiles.Count;
            AppendResult($"  {total} source files to analyze\n");

            for (int i = 0; i < sourceFiles.Count; i++)
            {
                if (!_scanning)
                {
                    AppendResult("\n-- Cancelled --\n");
                    FinishScan();
                    return;
                }

                var file = sourceFiles[i];
                file.Methods = SourceAnalyzer.ExtractMethods(file.FullPath);
                    file.Comments = CommentExtractor.Extract(file.FullPath);
                if (gitBlame.IsAvailable && file.Methods.Count > 0)
                    gitBlame.EnrichWithBlame(file.FullPath, file.Methods);

                if ((i + 1) % 5 == 0 || i == sourceFiles.Count - 1)
                {
                    var progress = i + 1;
                    var methodCount = file.Methods.Count;
                    AppendResult($"  [{progress}/{total}] {file.Name} ({methodCount} methods)\n");
                }
            }
        }
        else
        {
            AppendResult("\n[2/3] Skipped (--detail off)\n");
        }

        if (!_scanning) { AppendResult("\n-- Cancelled --\n"); FinishScan(); return; }

        AppendResult("\n[3/3] Generating output...\n");

        var output = optTree
            ? TreeFormatter.Format(path, entries, optStats)
            : TreeFormatter.FormatFlat(path, entries, optStats);

        // Save to DB + file log
        string savedInfo = "";
        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(path));
            var scanId = db.InsertScan(projectId, entries);

            var docPath = ProjectDocFinder.FindDoc(path);
            if (docPath != null)
            {
                try
                {
                    var docText = File.ReadAllText(docPath);
                    db.InsertProjectDoc(scanId, Path.GetRelativePath(path, docPath), docText);
                }
                catch { }
            }

            var methodTotal = entries.SelectMany(e => e.Methods).Count();
            var fileCountFinal = entries.Count(e => !e.IsDirectory);
            savedInfo = $"\n\n--- DB: {fileCountFinal} files, {methodTotal} methods indexed ---\n";

            // Also save file log
            var docContent = ProjectDocFinder.ReadDoc(path);
            var logOutput = docContent != null ? output + docContent : output;
            var store = new FileResultStore(AppPaths.GetLogDir());
            store.Save("tui-list", logOutput);
            var latest = Directory.GetFiles(AppPaths.GetLogDir(), "*.log")
                .OrderByDescending(f => f).FirstOrDefault();
            if (latest != null) savedInfo += $"--- Log: {latest} ---\n";
        }
        catch (Exception ex) { savedInfo = $"\n\n--- Save error: {ex.Message} ---\n"; }

        SafeInvoke(() =>
        {
            _resultView.Text = output + savedInfo;
            _resultView.MoveHome();
            _titleLabel.Text = $"Scan Complete: {path}";
            _hintLabel.Text = "[Q] Back  [H] Home  [Up/Down] Scroll";
            _scanning = false;
            _mode = Mode.Results;
            _resultView.SetFocus();
        });
    }

    private void FinishScan()
    {
        SafeInvoke(() =>
        {
            _scanning = false;
            _mode = Mode.Results;
            _titleLabel.Text = "Scan Cancelled";
            _hintLabel.Text = "[Q] Back to options";
        });
    }

    // ========================
    // Search
    // ========================
    private void ShowSearchInput()
    {
        _mode = Mode.SearchInput;
        _titleLabel.Text = "Search Indexed Data";
        _pathLabel.Text = "Search methods, files, and docs by keyword";
        _hintLabel.Text = "[Enter] Search  [Q] Back  [H] Home";

        _listView.Visible = false;
        _resultView.Visible = false;
        HideOptions();

        _lblSearch.Visible = true;
        _txtSearch.Visible = true;
        _btnSearch.Visible = true;

        _txtSearch.Text = " ";
        _txtSearch.SetFocus();
    }

    private void OnExecuteSearch(object? sender, CommandEventArgs e)
    {
        var query = _txtSearch.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(query)) return;

        _lblSearch.Visible = false;
        _txtSearch.Visible = false;
        _btnSearch.Visible = false;

        try
        {
            var dbPath = AppPaths.DbPath;
            if (!File.Exists(dbPath))
            {
                ShowSearchResults("No database found. Run 'list --detail' first to index a project.", query);
                return;
            }

            using var db = new SqliteStore(dbPath);
            var dbResults = db.Search(query, null, 50);
            var gitResults = GitLogSearchService.Search(query, 10);

            if (dbResults.Count == 0 && gitResults.Count == 0)
            {
                ShowSearchResults($"No results for: {query}", query);
                return;
            }

            var sb = new System.Text.StringBuilder();

            if (dbResults.Count > 0)
            {
                sb.AppendLine($"=== DB Index ({dbResults.Count} results) ===\n");
                FormatResults(sb, dbResults);
            }

            if (gitResults.Count > 0)
            {
                sb.AppendLine($"=== Git Log ({gitResults.Count} results) ===\n");
                FormatResults(sb, gitResults);
            }

            ShowSearchResults(sb.ToString(), query);
        }
        catch (Exception ex)
        {
            ShowSearchResults($"Search error: {ex.Message}", query);
        }
    }

    private static void FormatResults(System.Text.StringBuilder sb, List<SearchResult> results)
    {
        foreach (var r in results)
        {
            var typeTag = r.Type switch
            {
                "method" => "[METHOD]",
                "file"   => "[FILE]  ",
                "doc"    => "[DOC]   ",
                "commit"  => "[COMMIT]",
                "comment" => "[COMMENT]",
                _ => $"[{r.Type.ToUpper()}]"
            };
            sb.AppendLine($"  {typeTag} {r.Name}");
            if (!string.IsNullOrEmpty(r.Path))
                sb.AppendLine($"           {r.Path}");
            if (!string.IsNullOrEmpty(r.Excerpt))
                sb.AppendLine($"           {r.Excerpt}");
            sb.AppendLine();
        }
    }

    private void ShowSearchResults(string content, string query)
    {
        _mode = Mode.SearchResults;
        _titleLabel.Text = $"Search Results: {query}";
        _hintLabel.Text = "[Q] Back to search  [H] Home  [Up/Down] Scroll";

        _listView.Visible = false;
        HideOptions();

        _resultView.Text = content;
        _resultView.Visible = true;
        _resultView.MoveHome();
        _resultView.SetFocus();
    }

    // ========================
    // Projects
    // ========================
    private void ShowProjects()
    {
        _mode = Mode.Projects;
        _titleLabel.Text = "Indexed Projects";
        _pathLabel.Text = "Projects that have been scanned and indexed";
        _hintLabel.Text = "[Enter] View detail  [Q] Back  [H] Home";

        try
        {
            var dbPath = AppPaths.DbPath;
            if (!File.Exists(dbPath))
            {
                _listItems.Clear();
                _dirEntries.Clear();
                _listItems.Add("  No database found. Run 'list --detail' first.");
                _dirEntries.Add("__SEP__");
                ShowListMode();
                _listView.SetSource(_listItems);
                _listView.SetFocus();
                return;
            }

            using var db = new SqliteStore(dbPath);
            var projects = db.GetProjects();

            _listItems.Clear();
            _dirEntries.Clear();

            if (projects.Count == 0)
            {
                _listItems.Add("  No indexed projects yet.");
                _dirEntries.Add("__SEP__");
            }
            else
            {
                foreach (var p in projects)
                {
                    var size = FormatSize(p.TotalSize);
                    var lastScan = p.LastScannedAt ?? "(never)";
                    var addInfoTag = string.IsNullOrWhiteSpace(p.AddInfo) ? "" : " [has addinfo]";
                    _listItems.Add($"  #{p.Id} {p.RootPath}{addInfoTag}");
                    _dirEntries.Add($"__DETAIL_{p.Id}");
                    _listItems.Add($"    Files: {p.FileCount}  Dirs: {p.DirCount}  Size: {size}  Last: {lastScan}");
                    _dirEntries.Add("__SEP__");
                }
            }

            _listItems.Add("  ----------------");
            _dirEntries.Add("__SEP__");
            _listItems.Add("  [Back to Home]");
            _dirEntries.Add("__HOME__");

            ShowListMode();
            _listView.SetSource(_listItems);
            _listView.SelectedItem = 0;
            _listView.SetFocus();
        }
        catch (Exception ex)
        {
            _listItems.Clear();
            _dirEntries.Clear();
            _listItems.Add($"  Error: {ex.Message}");
            _dirEntries.Add("__SEP__");
            ShowListMode();
            _listView.SetSource(_listItems);
            _listView.SetFocus();
        }
    }

    // ========================
    // Project Detail
    // ========================
    private void ShowProjectDetail(long projectId)
    {
        _mode = Mode.ProjectDetail;
        _currentProjectId = projectId;

        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var project = db.GetProject(projectId);
            if (project == null)
            {
                _titleLabel.Text = "Project Not Found";
                _listItems.Clear();
                _dirEntries.Clear();
                _listItems.Add($"  Project #{projectId} not found.");
                _dirEntries.Add("__SEP__");
                _listItems.Add("  [Back to Projects]");
                _dirEntries.Add("__PROJECTS__");
                ShowListMode();
                _listView.SetSource(_listItems);
                _listView.SetFocus();
                return;
            }

            var methodCount = db.GetProjectMethodCount(projectId);
            var commentCount = db.GetProjectCommentCount(projectId);
            var docs = db.GetProjectDocs(projectId);
            var scans = db.GetProjectScans(projectId, 5);

            _titleLabel.Text = $"Project #{projectId} Detail";
            _pathLabel.Text = project.RootPath;
            _hintLabel.Text = "[Enter] Select action  [Q] Back  [H] Home";

            _listItems.Clear();
            _dirEntries.Clear();

            _listItems.Add($"  Path:       {project.RootPath}");
            _dirEntries.Add(project.RootPath);
            _listItems.Add($"  Files: {project.FileCount}  Dirs: {project.DirCount}  Size: {FormatSize(project.TotalSize)}");
            _dirEntries.Add("__SEP__");
            _listItems.Add($"  Methods: {methodCount}  Comments: {commentCount}  Last: {project.LastScannedAt ?? "(never)"}");
            _dirEntries.Add("__SEP__");

            if (docs.Count > 0)
            {
                _listItems.Add($"  Docs: {string.Join(", ", docs)}");
                _dirEntries.Add("__SEP__");
            }

            _listItems.Add("  ----------------");
            _dirEntries.Add("__SEP__");

            if (!string.IsNullOrWhiteSpace(project.AddInfo))
            {
                _listItems.Add($"  AddInfo: {project.AddInfo}");
                _dirEntries.Add("__SEP__");
                _listItems.Add("  [Edit AddInfo]");
                _dirEntries.Add("__ADDINFO__");
            }
            else
            {
                _listItems.Add("  AddInfo: (none)");
                _dirEntries.Add("__SEP__");
                _listItems.Add("  [Add Description] - Add project description");
                _dirEntries.Add("__ADDINFO__");
                _listItems.Add("  ----------------");
                _dirEntries.Add("__SEP__");
                _listItems.Add("  Tip: No description set. Add one to help LLM understand this project.");
                _dirEntries.Add("__SEP__");
            }

            if (scans.Count > 0)
            {
                _listItems.Add("  ----------------");
                _dirEntries.Add("__SEP__");
                _listItems.Add("  Scan History:");
                _dirEntries.Add("__SEP__");
                foreach (var s in scans)
                {
                    _listItems.Add($"    #{s.Id}  Files: {s.FileCount}  Dirs: {s.DirCount}  {s.ScannedAt}");
                    _dirEntries.Add("__SEP__");
                }
            }

            _listItems.Add("  ----------------");
            _dirEntries.Add("__SEP__");
            _listItems.Add("  [Update Path] - Change project root path");
            _dirEntries.Add("__UPDATEPATH__");
            _listItems.Add("  [Delete Project] - Remove from DB");
            _dirEntries.Add("__DELETE__");
            _listItems.Add("  ----------------");
            _dirEntries.Add("__SEP__");
            _listItems.Add("  [Back to Projects]");
            _dirEntries.Add("__PROJECTS__");
            _listItems.Add("  [Home]");
            _dirEntries.Add("__HOME__");

            ShowListMode();
            _listView.SetSource(_listItems);
            _listView.SelectedItem = 0;
            _listView.SetFocus();
        }
        catch (Exception ex)
        {
            _listItems.Clear();
            _dirEntries.Clear();
            _listItems.Add($"  Error: {ex.Message}");
            _dirEntries.Add("__SEP__");
            _listItems.Add("  [Back to Projects]");
            _dirEntries.Add("__PROJECTS__");
            ShowListMode();
            _listView.SetSource(_listItems);
            _listView.SetFocus();
        }
    }

    // ========================
    // AddInfo
    // ========================
    private void ShowAddInfoInput()
    {
        _titleLabel.Text = $"Add Description - Project #{_currentProjectId}";
        _pathLabel.Text = "Enter a description for this project";
        _hintLabel.Text = "[Enter] Save  [Q] Back";

        _listView.Visible = false;
        _resultView.Visible = false;
        HideOptions();

        // Load existing addinfo
        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var project = db.GetProject(_currentProjectId);
            _txtAddInfo.Text = project?.AddInfo ?? " ";
        }
        catch { _txtAddInfo.Text = " "; }

        _lblAddInfo.Visible = true;
        _txtAddInfo.Visible = true;
        _btnAddInfo.Visible = true;

        _txtAddInfo.SetFocus();
    }

    private void OnSaveAddInfo(object? sender, CommandEventArgs e)
    {
        var info = _txtAddInfo.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(info)) return;

        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            db.SetProjectAddInfo(_currentProjectId, info);

            _lblAddInfo.Visible = false;
            _txtAddInfo.Visible = false;
            _btnAddInfo.Visible = false;

            ShowProjectDetail(_currentProjectId);
        }
        catch (Exception ex)
        {
            _pathLabel.Text = $"Error: {ex.Message}";
        }
    }

    // ========================
    // Update Path
    // ========================
    private void ShowUpdatePathInput()
    {
        _titleLabel.Text = $"Update Path - Project #{_currentProjectId}";
        _pathLabel.Text = "Enter the new root path for this project";
        _hintLabel.Text = "[Enter] Save  [Q] Back";

        _listView.Visible = false;
        _resultView.Visible = false;
        HideOptions();

        // Load existing path
        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var project = db.GetProject(_currentProjectId);
            _txtUpdatePath.Text = project?.RootPath ?? " ";
        }
        catch { _txtUpdatePath.Text = " "; }

        _lblUpdatePath.Visible = true;
        _txtUpdatePath.Visible = true;
        _btnUpdatePath.Visible = true;

        _txtUpdatePath.SetFocus();
    }

    private void OnUpdatePath(object? sender, CommandEventArgs e)
    {
        var newPath = _txtUpdatePath.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(newPath)) return;

        try
        {
            var fullPath = Path.GetFullPath(newPath);
            using var db = new SqliteStore(AppPaths.DbPath);
            db.SetProjectPath(_currentProjectId, fullPath);

            _lblUpdatePath.Visible = false;
            _txtUpdatePath.Visible = false;
            _btnUpdatePath.Visible = false;

            ShowProjectDetail(_currentProjectId);
        }
        catch (Exception ex)
        {
            _pathLabel.Text = $"Error: {ex.Message}";
        }
    }

    // ========================
    // Delete Project
    // ========================
    private void ConfirmDeleteProject()
    {
        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var project = db.GetProject(_currentProjectId);
            if (project == null) return;

            var result = MessageBox.Query(
                "Delete Project",
                $"Delete project #{_currentProjectId}?\n{project.RootPath}\n\nThis removes all scan data from DB.\nSource files on disk are NOT affected.",
                "Delete", "Cancel");

            if (result == 0) // Delete
            {
                db.DeleteProject(_currentProjectId);
                ShowProjects();
            }
        }
        catch (Exception ex)
        {
            _pathLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void ShowRootSelect()
    {
        _mode = Mode.RootSelect;
        _titleLabel.Text = "Select Root Directory";
        _pathLabel.Text = "OS: " + (OperatingSystem.IsWindows() ? "Windows" : "Linux/macOS");
        _hintLabel.Text = "[Enter] Select  [Q] Exit  [H] Home";

        _listItems.Clear();
        _dirEntries.Clear();

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var vol = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                _listItems.Add($"  {drive.Name}  ({vol}, {FormatSize(drive.TotalSize)})");
                _dirEntries.Add(drive.RootDirectory.FullName);
            }
        }
        else
        {
            _listItems.Add("  /  (root)");
            _dirEntries.Add("/");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(home))
            {
                _listItems.Add($"  {home}  (home)");
                _dirEntries.Add(home);
            }
        }

        _listItems.Add("  ----------------");
        _dirEntries.Add("__SEP__");
        _listItems.Add("  [Search] Search indexed methods, files, docs");
        _dirEntries.Add("__SEARCH__");
        _listItems.Add("  [Projects] View indexed projects");
        _dirEntries.Add("__PROJECTS__");
        _listItems.Add("  ----------------");
        _dirEntries.Add("__SEP__");
        _listItems.Add("  [Exit]");
        _dirEntries.Add("__EXIT__");

        ShowListMode();
        _listView.SetSource(_listItems);
        _listView.SelectedItem = 0;
        _listView.SetFocus();
    }

    private void ShowDirBrowse(string path)
    {
        _mode = Mode.DirBrowse;
        _titleLabel.Text = "Browse Directory";
        _pathLabel.Text = path;
        _hintLabel.Text = "[Enter] Select/Enter  [Q] Back  [H] Home";

        _listItems.Clear();
        _dirEntries.Clear();

        _listItems.Add("  >> [SCAN THIS DIRECTORY] <<");
        _dirEntries.Add("__SCAN__");

        var parent = Directory.GetParent(path);
        if (parent != null)
        {
            _listItems.Add("  [..] Parent Directory");
            _dirEntries.Add("__UP__");
        }

        try
        {
            var dirs = Directory.GetDirectories(path)
                .OrderByDescending(d => SafeGetWriteTime(d))
                .ToArray();

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || IsDefaultExcluded(name))
                    _listItems.Add($"  {name}/  (excluded)");
                else
                {
                    var fileCount = SafeCountFiles(dir);
                    _listItems.Add($"  {name}/  ({fileCount} files)");
                }
                _dirEntries.Add(dir);
            }
        }
        catch (Exception)
        {
            _listItems.Add("  (access denied - skipped)");
            _dirEntries.Add("__SEP__");
        }

        ShowListMode();
        _listView.SetSource(_listItems);
        _listView.SelectedItem = 0;
        _listView.SetFocus();
    }

    private void ShowScanOptions()
    {
        _mode = Mode.ScanOptions;
        _titleLabel.Text = "Scan Options";
        _pathLabel.Text = $"Target: {_currentPath}";
        _hintLabel.Text = "[Tab] Navigate  [Enter] Run Scan  [Q] Back  [H] Home";

        _listView.Visible = false;
        _resultView.Visible = false;

        _chkTree.Visible = true;
        _chkDetail.Visible = true;
        _chkStats.Visible = true;
        _lblInclude.Visible = true;
        _txtInclude.Visible = true;
        _btnExecute.Visible = true;

        _chkTree.SetFocus();
    }

    private void ShowListMode()
    {
        _listView.Visible = true;
        _resultView.Visible = false;
        HideOptions();
    }

    private void HideOptions()
    {
        _chkTree.Visible = false;
        _chkDetail.Visible = false;
        _chkStats.Visible = false;
        _lblInclude.Visible = false;
        _txtInclude.Visible = false;
        _btnExecute.Visible = false;
        _lblSearch.Visible = false;
        _txtSearch.Visible = false;
        _btnSearch.Visible = false;
        _lblAddInfo.Visible = false;
        _txtAddInfo.Visible = false;
        _btnAddInfo.Visible = false;
        _lblUpdatePath.Visible = false;
        _txtUpdatePath.Visible = false;
        _btnUpdatePath.Visible = false;
    }

    private static bool IsDefaultExcluded(string dirName)
    {
        return dirName is "bin" or "obj" or "node_modules"
            or ".next" or "dist" or "build" or "__pycache__";
    }

    private static int SafeCountFiles(string dir)
    {
        try { return Directory.GetFiles(dir).Length; }
        catch { return 0; }
    }

    private static DateTime SafeGetWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
    };
}
