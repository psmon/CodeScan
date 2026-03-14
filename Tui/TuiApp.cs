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

    public void Run()
    {
        try
        {
            DisableConsoleMouse();
            Application.Init();
            Application.IsMouseDisabled = true;
            DisableConsoleMouse(); // re-apply after Init (it may reset console mode)

            var main = new MainView();
            Application.Run(main);
            main.Dispose();
            Application.Shutdown();
        }
        catch (Exception ex)
        {
            try { Application.Shutdown(); } catch { }

            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "tui-crash.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fatal:\n{ex}\n\n");

            Console.Error.WriteLine($"TUI crashed. See tui-crash.log");
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}

public class MainView : Toplevel
{
    private enum Mode { RootSelect, DirBrowse, ScanOptions, Scanning, Results }

    private Mode _mode = Mode.RootSelect;
    private string _currentPath = "";
    private readonly Stack<string> _pathHistory = new();
    private volatile bool _scanning = false;

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

    private ObservableCollection<string> _listItems = [];
    private List<string> _dirEntries = [];

    public MainView()
    {
        Title = "CodeScan TUI";

        _titleLabel = new Label
        {
            Text = "Select Root",
            X = 1, Y = 0,
            ColorScheme = Colors.ColorSchemes["Menu"]
        };

        _pathLabel = new Label { Text = " ", X = 1, Y = 1 };

        _hintLabel = new Label
        {
            Text = "[Enter] Select  [Q] Back/Exit",
            X = 1, Y = Pos.AnchorEnd(1)
        };

        // Back button - always visible at top-right
        _btnBack = new Button
        {
            Text = "< Back (Q)",
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

        Add(_titleLabel, _pathLabel, _hintLabel, _btnBack, _listView, _resultView,
            _chkTree, _chkDetail, _chkStats, _lblInclude, _txtInclude, _btnExecute);

        // Q key = back (only when not typing in TextField)
        KeyDown += OnGlobalKeyDown;

        ShowRootSelect();
    }

    private void OnGlobalKeyDown(object? sender, Key key)
    {
        // ignore Q when typing in a text field
        if (_txtInclude.HasFocus) return;

        if (key == Key.Q || key == Key.Q.WithShift)
        {
            key.Handled = true;
            HandleBack();
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

        // save log
        try
        {
            var storeDir = Path.Combine(Directory.GetCurrentDirectory(), "Prompt", "TestFileDB");
            var store = new FileResultStore(storeDir);
            store.Save("tui-list", output);
        }
        catch { /* log save failed, continue */ }

        // show final result
        SafeInvoke(() =>
        {
            _resultView.Text = output;
            _resultView.MoveHome();
            _titleLabel.Text = $"Scan Complete: {path}";
            _hintLabel.Text = "[Q] Back to options  [Up/Down] Scroll";
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

    private void ShowRootSelect()
    {
        _mode = Mode.RootSelect;
        _titleLabel.Text = "Select Root Directory";
        _pathLabel.Text = "OS: " + (OperatingSystem.IsWindows() ? "Windows" : "Linux/macOS");
        _hintLabel.Text = "[Enter] Select  [Q] Exit";

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
        _hintLabel.Text = "[Enter] Select/Enter  [Q] Back";

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
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
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
        catch (UnauthorizedAccessException)
        {
            _listItems.Add("  (access denied)");
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
        _hintLabel.Text = "[Tab] Navigate  [Enter] Run Scan  [Q] Back";

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

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
    };
}
