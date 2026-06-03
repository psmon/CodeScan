using System.Collections.ObjectModel;
using Terminal.Gui;
using CodeScan.Services;
using CodeScan.Services.Llm;
using CodeScan.Services.Llm.Tools;

namespace CodeScan.Tui;

/// <summary>
/// Chat panel for the TUI — wraps a Gemma 4 e4b CPU agent loop.
///
/// State machine:
///   Start  → choose model + (optional) project context
///   Loading → load GGUF into memory (slow, single-thread CPU)
///   Chat   → interactive conversation
/// All transitions show/hide the same set of views so the parent
/// <see cref="MainView"/> can host this as a single Toplevel panel without
/// stacking nested windows.
/// </summary>
public sealed class ChatView : IAsyncDisposable
{
    private readonly Toplevel _root;
    private readonly Action _onExit;

    // Start screen
    private readonly Label _titleLabel;
    private readonly Label _hintLabel;
    private readonly ListView _modelList;
    private readonly Label _modelPathLabel;
    private readonly Label _projectPathLabel;
    private readonly Label _deviceLabel;
    private readonly RadioGroup _deviceRadio;
    private readonly Label _ctxLabel;
    private readonly RadioGroup _ctxRadio;
    private readonly Label _responseLabel;
    private readonly RadioGroup _responseRadio;
    private readonly Label _modelInfoLabel;
    private readonly Button _startBtn;
    private readonly Button _pickProjectBtn;
    private readonly Button _downloadBtn;

    // Discovered runtime options — populated by RefreshDevices() and
    // OnModelPicked(), consumed by StartChatAsync. Both lists are kept in
    // lockstep with their respective RadioGroups: index 0 of _deviceRadio
    // is always "CPU"; indices 1+ map to _devices in order.
    private readonly List<GpuDevice> _devices = new();
    private readonly List<int> _ctxChoiceTokens = new();  // mirrors _ctxRadio order
    private GgufMetadata? _modelMeta;

    // Last tool name seen in the chat-update stream, so the matching
    // tool_result line can label its summary correctly. AgentChatLoop emits
    // tool → tool_result alternately within a turn so a single slot is
    // enough.
    private string _lastUiToolName = "";

    // Maps _responseRadio.SelectedItem → AgentChatLoop.maxTokensPerTurn.
    // Capped at 4096 so a runaway model can't burn its entire ctx window
    // in one turn; the user can raise this with a fresh ChatView.
    private static readonly int[] ResponseTokenChoices = { 512, 1024, 2048, 4096 };
    private const int DefaultResponseIndex = 1;  // Medium

    // Download screen
    private readonly Label _downloadTitleLabel;
    private readonly Label _downloadStatusLabel;
    private readonly Label _downloadBarLabel;
    private readonly Button _cancelDownloadBtn;

    // Chat screen
    private readonly TextView _historyView;
    private readonly Label _statusLabel;
    private readonly TextField _inputField;
    private readonly Button _sendBtn;

    private readonly ObservableCollection<string> _modelListItems = new();
    private readonly List<string> _modelPaths = new();

    private string? _selectedModelPath;
    private string? _selectedProjectRoot;
    private long _selectedProjectId;

    private LlmHost? _host;
    private AgentChatLoop? _loop;
    private SqliteStore? _db;
    private ChatSessionLogger? _logger;

    // Short tag printed in front of every assistant "done" line — switches
    // with the chat template so the user sees who's actually answering
    // when they swap models mid-session. Set when StartChatAsync resolves
    // the template via ChatTemplateRegistry.
    private string _assistantLabel = "Assistant";

    private enum Mode { Start, Downloading, Loading, Chat, TearingDown }
    private Mode _mode = Mode.Start;
    private volatile bool _busy;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _downloadCts;

    public ChatView(Toplevel root, Action onExit)
    {
        _root = root;
        _onExit = onExit;

        _titleLabel = new Label
        {
            Text = "Chat — on-device LLM (Gemma 4 / Nemotron 3 Nano)",
            X = 1, Y = 3,
            Visible = false,
        };

        _hintLabel = new Label
        {
            Text = "",
            X = 1, Y = 4,
            Width = Dim.Fill(2),
            Visible = false,
        };

        // Compact layout — start screen has to fit inside a 24-row terminal
        // with the Start button visible without scrolling. Every Y below
        // was tuned so the [Load Model & Start Chat] button lands at Y=21
        // even with the device radio carrying up to 4 GPU rows.
        _modelList = new ListView
        {
            X = 1, Y = 6,
            Width = Dim.Fill(2),
            Height = 4,
            Visible = false,
        };
        _modelList.SetSource(_modelListItems);
        _modelList.OpenSelectedItem += OnModelPicked;

        _modelPathLabel = new Label
        {
            Text = "Model: (none selected)",
            X = 1, Y = 10,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _projectPathLabel = new Label
        {
            Text = "Project: (none — abs paths only)",
            X = 1, Y = 11,
            Width = Dim.Fill(2),
            Visible = false,
        };

        // Runtime tunables — populated dynamically. The CPU row is always
        // present (index 0); enumerated Vulkan/WMI/nvidia-smi devices fill
        // 1..N. Default selection prefers a discrete GPU when one exists.
        _deviceLabel = new Label
        {
            Text = "Device:",
            X = 1, Y = 13,
            Visible = false,
        };

        _deviceRadio = new RadioGroup
        {
            X = 10, Y = 13,
            Orientation = Orientation.Vertical,
            RadioLabels = new[] { "CPU" },
            SelectedItem = 0,
            Visible = false,
        };
        _deviceRadio.SelectedItemChanged += (_, _) => RecomputeRecommendation();

        // Context radio options are derived from the GGUF's trained context
        // length — see OnModelPicked. Initial labels are a placeholder until
        // a model is selected.
        _ctxLabel = new Label
        {
            Text = "Ctx:",
            X = 1, Y = 15,
            Visible = false,
        };

        _ctxRadio = new RadioGroup
        {
            X = 6, Y = 15,
            Orientation = Orientation.Horizontal,
            RadioLabels = new[] { "4K", "8K", "16K", "32K" },
            SelectedItem = 1,
            Visible = false,
        };

        // Per-turn response cap. Labels stay short so this fits on one row.
        _responseLabel = new Label
        {
            Text = "Resp:",
            X = 1, Y = 16,
            Visible = false,
        };

        _responseRadio = new RadioGroup
        {
            X = 7, Y = 16,
            Orientation = Orientation.Horizontal,
            RadioLabels = new[] { "Short", "Med", "Long", "Max" },
            SelectedItem = DefaultResponseIndex,
            Visible = false,
        };

        _modelInfoLabel = new Label
        {
            Text = "",
            X = 1, Y = 17,
            Width = Dim.Fill(2),
            Height = 1,
            Visible = false,
        };

        _pickProjectBtn = new Button
        {
            Text = "[Pick Project]",
            X = 1, Y = 19,
            Visible = false,
        };
        _pickProjectBtn.Accepting += (_, _) => PickProject();

        _startBtn = new Button
        {
            Text = ">>> Start Chat <<<",
            X = 18, Y = 19,
            Visible = false,
        };
        _startBtn.Accepting += (_, _) => _ = StartChatAsync();

        _downloadBtn = new Button
        {
            Text = "[Download Model…]",
            X = 1, Y = 21,
            Visible = false,
        };
        _downloadBtn.Accepting += (_, _) => _ = PickAndDownloadModelAsync();

        // Download screen widgets ------------------------------------------
        _downloadTitleLabel = new Label
        {
            Text = "Downloading default model…",
            X = 1, Y = 3,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _downloadStatusLabel = new Label
        {
            Text = "",
            X = 1, Y = 5,
            Width = Dim.Fill(2),
            Height = 3,
            Visible = false,
        };

        _downloadBarLabel = new Label
        {
            Text = "",
            X = 1, Y = 9,
            Width = Dim.Fill(2),
            Visible = false,
        };

        _cancelDownloadBtn = new Button
        {
            Text = "[Cancel Download]",
            X = 1, Y = 11,
            Visible = false,
        };
        _cancelDownloadBtn.Accepting += (_, _) =>
        {
            try { _downloadCts?.Cancel(); } catch { }
        };

        // Chat screen widgets ----------------------------------------------
        _historyView = new TextView
        {
            X = 1, Y = 3,
            Width = Dim.Fill(2),
            Height = Dim.Fill(5),
            ReadOnly = true,
            WordWrap = true,
            Visible = false,
        };

        _statusLabel = new Label
        {
            Text = "ready",
            X = 1, Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(2),
            Visible = false,
        };

        _inputField = new TextField
        {
            Text = "",
            X = 1, Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(15),
            Visible = false,
        };

        _sendBtn = new Button
        {
            Text = "Send (Enter)",
            X = Pos.AnchorEnd(14), Y = Pos.AnchorEnd(3),
            Visible = false,
        };
        _sendBtn.Accepting += (_, _) => _ = SendAsync();

        // Enter inside the input field triggers send too — both the field's
        // Accepting event and a KeyDown fallback fire so we handle either
        // Terminal.Gui v2 binding path.
        _inputField.Accepting += (_, _) => _ = SendAsync();
        _inputField.KeyDown += (_, k) =>
        {
            if (k == Key.Enter)
            {
                k.Handled = true;
                _ = SendAsync();
            }
        };

        root.Add(_titleLabel, _hintLabel, _modelList, _modelPathLabel,
            _projectPathLabel, _deviceLabel, _deviceRadio, _ctxLabel, _ctxRadio,
            _responseLabel, _responseRadio,
            _modelInfoLabel, _pickProjectBtn, _startBtn, _downloadBtn,
            _downloadTitleLabel, _downloadStatusLabel, _downloadBarLabel, _cancelDownloadBtn,
            _historyView, _statusLabel, _inputField, _sendBtn);
    }

    // ------------------------------------------------------------------
    // Public surface called by MainView
    // ------------------------------------------------------------------
    public void Show()
    {
        _mode = Mode.Start;
        RefreshModels();
        ShowStart();
    }

    public void Hide()
    {
        foreach (var v in new View[]
                 { _titleLabel, _hintLabel, _modelList, _modelPathLabel,
                   _projectPathLabel, _deviceLabel, _deviceRadio, _ctxLabel, _ctxRadio,
                   _responseLabel, _responseRadio,
                   _modelInfoLabel, _pickProjectBtn, _startBtn, _downloadBtn,
                   _downloadTitleLabel, _downloadStatusLabel, _downloadBarLabel, _cancelDownloadBtn,
                   _historyView, _statusLabel, _inputField, _sendBtn })
        {
            v.Visible = false;
        }
    }

    /// <summary>True if the user is mid-chat (so HandleBack should not exit immediately).</summary>
    public bool IsActive => _mode != Mode.Start || _busy;

    /// <summary>True if the chat input field currently has keyboard focus. Used
    /// by MainView's global Q/H handler to let Korean ('ㅂ'/'ㅎ') keystrokes
    /// flow into the text field instead of triggering Back/Home.</summary>
    public bool IsInputFocused => _inputField.HasFocus;

    public bool HandleBack()
    {
        if (_mode == Mode.TearingDown)
            return true;  // already unloading — ignore extra Q presses

        if (_mode == Mode.Downloading)
        {
            // Q during download = cancel. The partial file stays on disk so a
            // later retry resumes from where we stopped.
            try { _downloadCts?.Cancel(); } catch { }
            return true;
        }

        // Download finished/cancelled with the post-download screen still up
        // (waiting for the user to read the success or error message). Swap
        // back to the model picker instead of bubbling Q to MainView.
        if (_mode == Mode.Start && _downloadTitleLabel.Visible)
        {
            _cancelDownloadBtn.Text = "[Cancel Download]";
            RefreshModels();
            ShowStart();
            return true;
        }

        if (_busy)
        {
            _cts?.Cancel();
            UpdateStatus("cancelling…");
            return true;  // swallow Back during in-flight inference
        }

        if (_mode == Mode.Chat)
        {
            // Teardown directly. Earlier builds showed a MessageBox.Query
            // confirmation here ("Unload" / "Stay"), but it broke in
            // Terminal.Gui v2 — pressing Enter on the focused "Unload"
            // button silently returned a non-zero index, so the popup
            // appeared and went away without ever invoking teardown. The
            // user explicitly pressed Q to exit chat; trust the intent.
            _mode = Mode.TearingDown;
            UpdateStatus("unloading model…");
            _ = TeardownAsync();
            return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync() => await TeardownAsync();

    // ------------------------------------------------------------------
    // Start screen
    // ------------------------------------------------------------------
    private void ShowStart()
    {
        _titleLabel.Visible = true;
        _hintLabel.Visible = true;
        _modelList.Visible = true;
        _modelPathLabel.Visible = true;
        _projectPathLabel.Visible = true;
        _deviceLabel.Visible = true;
        _deviceRadio.Visible = true;
        _ctxLabel.Visible = true;
        _ctxRadio.Visible = true;
        _responseLabel.Visible = true;
        _responseRadio.Visible = true;
        _modelInfoLabel.Visible = true;
        _pickProjectBtn.Visible = true;
        _startBtn.Visible = true;
        // _downloadBtn visibility is decided by RefreshModels() based on
        // whether the default model is already present on disk.

        _downloadTitleLabel.Visible = false;
        _downloadStatusLabel.Visible = false;
        _downloadBarLabel.Visible = false;
        _cancelDownloadBtn.Visible = false;

        _historyView.Visible = false;
        _statusLabel.Visible = false;
        _inputField.Visible = false;
        _sendBtn.Visible = false;

        _modelList.SetFocus();
    }

    private void RefreshModels()
    {
        _modelListItems.Clear();
        _modelPaths.Clear();

        // 1) Catalog entries that already have a downloaded file on disk.
        foreach (var (entry, path) in ModelLocator.EnumerateAvailable())
        {
            _modelListItems.Add($"  {entry.DisplayName}");
            _modelPaths.Add(path);
        }

        // 2) Any other *.gguf files dropped into ~/.codescan/models/ manually.
        foreach (var extra in ModelLocator.EnumerateGgufFiles(ModelLocator.ModelsDir))
        {
            if (_modelPaths.Contains(extra)) continue;
            _modelListItems.Add($"  {Path.GetFileName(extra)}  (custom)");
            _modelPaths.Add(extra);
        }

        if (_modelListItems.Count == 0)
        {
            var defaultEntry = ChatModelCatalog.Default;
            _modelListItems.Add("  (no GGUF model found)");
            _modelPaths.Add("");
            _hintLabel.Text =
                $"No model on disk. Use the download button below, or drop a GGUF into {ModelLocator.ModelsDir}.";
        }
        else
        {
            _hintLabel.Text =
                "Pick model · device · ctx, then click [Start Chat]. Recommended ctx auto-fits the selected device.";
            // Default selection: first available.
            _selectedModelPath = _modelPaths[0];
            _modelPathLabel.Text = $"Model: {_selectedModelPath}";
        }

        // Surface the download button whenever ANY catalog model isn't yet
        // on disk. FindModel honours the dev-fallback path so a single Gemma
        // GGUF shared with AgentZeroLite counts as "present".
        _downloadBtn.Visible = ChatModelCatalog.All.Any(e => ModelLocator.FindModel(e) == null);

        _modelList.SetSource(_modelListItems);
        _modelList.SelectedItem = 0;

        // Refresh devices once when entering the screen — vulkaninfo + WMI
        // costs ~1s in the worst case; cache for the screen lifetime.
        RefreshDevices();

        // Peek the default model's GGUF header so Context options and the
        // recommendation are populated up front.
        if (!string.IsNullOrEmpty(_selectedModelPath))
            ReloadModelMetadata(_selectedModelPath);
    }

    private void RefreshDevices()
    {
        // Synchronous path with cache reuse. Earlier rounds tried a
        // background Task + Application.Invoke pattern to keep first-entry
        // snappy, but that produced a TUI-corruption side-effect when the
        // invoke landed before Application had finished the Chat-screen
        // transition. The cache + a Main-view-time prefetch (see MainView's
        // constructor) gives us the same "instant on second open" property
        // without the threading edge cases.
        _devices.Clear();
        try { _devices.AddRange(GpuEnumerator.Enumerate()); }
        catch { /* enumeration failed — CPU stays as the lone option */ }
        PopulateDeviceRadio();
    }

    private void PopulateDeviceRadio()
    {
        var labels = new List<string> { "CPU" };
        var defaultIdx = 0;
        for (int i = 0; i < _devices.Count; i++)
        {
            var d = _devices[i];
            var vram = d.VramBytes > 0 ? $"{d.VramBytes / (1024.0 * 1024 * 1024):F1} GB" : "?";
            var marker = d.VulkanIndex < 0 ? " [Vulkan driver missing]" : "";
            // Vendor often appears inside the device name already
            // ("AMD Radeon...", "NVIDIA GeForce...") — don't prepend it twice.
            var displayName = d.Name.StartsWith(d.Vendor, StringComparison.OrdinalIgnoreCase)
                ? d.Name
                : $"{d.Vendor} {d.Name}";
            labels.Add($"GPU{Math.Max(d.VulkanIndex, 0)}: {Trim(displayName, 36)} ({vram}){marker}");
            // Prefer the first Vulkan-capable discrete device; fall back to
            // any Vulkan-capable device; otherwise stay on CPU.
            if (defaultIdx == 0 && d.VulkanIndex >= 0 && d.IsDiscrete) defaultIdx = i + 1;
        }
        if (defaultIdx == 0)
        {
            // No discrete — pick the first Vulkan-capable device if there is one.
            for (int i = 0; i < _devices.Count; i++)
                if (_devices[i].VulkanIndex >= 0) { defaultIdx = i + 1; break; }
        }

        _deviceRadio.RadioLabels = labels.ToArray();
        _deviceRadio.SelectedItem = defaultIdx;
    }

    private void ReloadModelMetadata(string modelPath)
    {
        _modelMeta = GgufReader.Read(modelPath);

        // Build the ctx radio options from the model's trained max.
        var modelMax = _modelMeta?.ContextLength ?? 8192;
        var choices = CtxRecommender.ContextChoices(modelMax);
        _ctxChoiceTokens.Clear();
        var labels = new string[choices.Count];
        for (int i = 0; i < choices.Count; i++)
        {
            _ctxChoiceTokens.Add(choices[i].Tokens);
            labels[i] = choices[i].Label;
        }
        _ctxRadio.RadioLabels = labels;
        _ctxRadio.SelectedItem = 0;  // tentative; RecomputeRecommendation will move

        RecomputeRecommendation();
    }

    private void RecomputeRecommendation()
    {
        if (_modelMeta == null) { _modelInfoLabel.Text = ""; return; }

        var deviceIdx = _deviceRadio.SelectedItem;
        var gpuLayers = deviceIdx > 0 ? 999 : 0;
        var device = deviceIdx > 0 && deviceIdx - 1 < _devices.Count ? _devices[deviceIdx - 1] : null;

        var rec = CtxRecommender.For(_modelMeta, device, gpuLayers);

        // Move the radio selection to the recommended option (closest match).
        var bestIdx = 0;
        var bestDelta = int.MaxValue;
        for (int i = 0; i < _ctxChoiceTokens.Count; i++)
        {
            var delta = Math.Abs(_ctxChoiceTokens[i] - rec.RecommendedCtx);
            if (delta < bestDelta) { bestDelta = delta; bestIdx = i; }
        }
        _ctxRadio.SelectedItem = bestIdx;

        var arch = _modelMeta.Architecture;
        var modelMaxK = rec.ModelMaxCtx / 1024;
        var recK = rec.RecommendedCtx / 1024;
        // One-line summary — the verbose rationale was nice but cost a row
        // we can't spare in a 24-row terminal.
        _modelInfoLabel.Text =
            $"{arch} · max {modelMaxK}K · KV {FormatBytes(rec.PerTokenKvBytes)}/tok · recommended {recK}K";
    }

    private void OnModelPicked(object? sender, ListViewItemEventArgs e)
    {
        var idx = e.Item;
        if (idx < 0 || idx >= _modelPaths.Count) return;
        var path = _modelPaths[idx];
        if (string.IsNullOrEmpty(path)) return;
        _selectedModelPath = path;
        _modelPathLabel.Text = $"Model: {_selectedModelPath}";
        ReloadModelMetadata(path);
    }

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..(max - 1)] + "…";

    // ------------------------------------------------------------------
    // Friendly tool-call / tool-result rendering
    //
    // The AgentChatLoop emits raw JSON in the "tool" and "tool_result"
    // phases because the disk log (~/.codescan/logs/chat-*.log) is the
    // forensic source of truth. The chat transcript view, by contrast,
    // should keep noise out of the user's way — they're trying to follow
    // the agent's chain of reasoning, not parse JSON in their head.
    //
    // Each formatter parses just enough of the payload to surface the one
    // or two fields that matter for that tool. Anything unparseable falls
    // back to the raw text so we never silently hide an actual failure.
    // ------------------------------------------------------------------

    /// <summary>
    /// Pulls "toolName({...args...})" apart and returns the tool name
    /// plus a one-line Korean summary of the call.
    /// </summary>
    private static (string ToolName, string Brief) FormatToolCall(string raw)
    {
        var paren = raw.IndexOf('(');
        if (paren <= 0 || !raw.EndsWith(")")) return (raw, raw);

        var tool = raw[..paren];
        var argsText = raw[(paren + 1)..^1];

        System.Text.Json.Nodes.JsonObject? args = null;
        try { args = System.Text.Json.Nodes.JsonNode.Parse(argsText)?.AsObject(); }
        catch { /* malformed args fall back to the raw form below */ }

        string brief = (tool, args) switch
        {
            ("db_search", { } a) =>
                $"검색: \"{Trim(StrArg(a, "query"), 40)}\"" +
                (StrArg(a, "type") is { Length: > 0 } t ? $" (type={t})" : ""),
            ("project_tree", { } a) =>
                $"프로젝트 트리 (depth={StrArg(a, "max_depth", "3")})",
            ("read_file", { } a) =>
                $"읽기: {ShortPath(StrArg(a, "path"))} ({StrArg(a, "start", "1")}~{StrArg(a, "end", "?")})",
            ("grep_file", { } a) =>
                $"grep: \"{Trim(StrArg(a, "pattern"), 30)}\" in {ShortPath(StrArg(a, "path"))}",
            ("list_projects", _) => "프로젝트 목록 조회",
            ("project_info", { } a) =>
                $"프로젝트 정보 (#{StrArg(a, "id", "?")})",
            ("graph_query", { } a) =>
                $"그래프 쿼리: {Trim(StrArg(a, "query"), 40)}",
            ("done", _) => "최종 응답",
            _ => $"{tool}({Trim(argsText, 60)})",
        };
        return (tool, brief);
    }

    /// <summary>
    /// Compresses the tool's JSON result into a "✓ N hits" style line.
    /// Returns "실패: ..." when the tool reported ok=false so the user
    /// sees the failure inline instead of just a silent next-turn.
    /// </summary>
    private static string FormatToolResult(string toolName, string resultJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok)
                && ok.ValueKind == System.Text.Json.JsonValueKind.False)
            {
                var err = root.TryGetProperty("error", out var e) ? e.GetString() : "(no message)";
                return $"✗ 실패: {Trim(err ?? "", 80)}";
            }

            return toolName switch
            {
                "db_search" =>
                    $"✓ {IntArg(root, "count")} hits",
                "project_tree" =>
                    $"✓ {IntArg(root, "dir_count")} dirs / {IntArg(root, "file_count")} files",
                "read_file" =>
                    FormatReadFileSummary(root),
                "grep_file" =>
                    $"✓ {IntArg(root, "count")} matches",
                "list_projects" =>
                    $"✓ {IntArg(root, "total")} projects ({IntArg(root, "shown")} shown)",
                "project_info" =>
                    $"✓ 프로젝트 #{IntArg(root, "id")}",
                "graph_query" =>
                    $"✓ {IntArg(root, "node_count")} nodes / {IntArg(root, "edge_count")} edges",
                _ => "✓ 완료",
            };
        }
        catch
        {
            return "✓";
        }
    }

    private static string FormatReadFileSummary(System.Text.Json.JsonElement root)
    {
        var start = IntArg(root, "start");
        var end = IntArg(root, "end");
        var total = IntArg(root, "total_lines");
        var lines = Math.Max(0, end - start + 1);
        return total > 0
            ? $"✓ {lines}줄 ({start}~{end} / 총 {total}줄)"
            : $"✓ {lines}줄";
    }

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var sep = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return sep >= 0 && sep < path.Length - 1 ? path[(sep + 1)..] : path;
    }

    private static string StrArg(System.Text.Json.Nodes.JsonObject args, string key, string fallback = "")
    {
        if (!args.TryGetPropertyValue(key, out var v) || v is null) return fallback;
        if (v is System.Text.Json.Nodes.JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s)) return s;
            return jv.ToString();
        }
        return v.ToString();
    }

    private static int IntArg(System.Text.Json.JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
    }

    private static string FormatBytes(long b)
    {
        if (b <= 0) return "?";
        const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;
        if (b >= GiB) return $"{b / GiB:F1}G";
        if (b >= MiB) return $"{b / MiB:F0}M";
        if (b >= KiB) return $"{b / KiB:F0}K";
        return $"{b}B";
    }

    private void PickProject()
    {
        try
        {
            using var db = new SqliteStore(AppPaths.DbPath);
            var projects = db.GetProjects();
            if (projects.Count == 0)
            {
                MessageBox.Query("No projects",
                    "No indexed projects. Run 'codescan scan <path>' or use the Projects menu first.",
                    "OK");
                return;
            }

            var items = projects.Select(p => $"#{p.Id}  {p.RootPath}").ToArray();
            var pick = MessageBox.Query("Pick project context",
                "File paths in tool calls will resolve against this root.",
                items);
            if (pick < 0 || pick >= projects.Count) return;

            _selectedProjectId = projects[pick].Id;
            _selectedProjectRoot = projects[pick].RootPath;
            _projectPathLabel.Text =
                $"Project context: #{_selectedProjectId}  {_selectedProjectRoot}";
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", $"Project pick failed: {ex.Message}", "OK");
        }
    }

    // ------------------------------------------------------------------
    // Download default model (Gemma 4 E4B GGUF)
    // ------------------------------------------------------------------
    private void ShowDownloading()
    {
        _titleLabel.Visible = false;
        _hintLabel.Visible = false;
        _modelList.Visible = false;
        _modelPathLabel.Visible = false;
        _projectPathLabel.Visible = false;
        _deviceLabel.Visible = false;
        _deviceRadio.Visible = false;
        _ctxLabel.Visible = false;
        _ctxRadio.Visible = false;
        _responseLabel.Visible = false;
        _responseRadio.Visible = false;
        _modelInfoLabel.Visible = false;
        _pickProjectBtn.Visible = false;
        _startBtn.Visible = false;
        _downloadBtn.Visible = false;

        _downloadTitleLabel.Visible = true;
        _downloadStatusLabel.Visible = true;
        _downloadBarLabel.Visible = true;
        _cancelDownloadBtn.Visible = true;

        _cancelDownloadBtn.SetFocus();
    }

    private Task PickAndDownloadModelAsync()
    {
        if (_busy || _mode == Mode.Downloading) return Task.CompletedTask;

        // Only offer entries we don't already have on disk. Anything else
        // is just noise in the picker.
        var missing = ChatModelCatalog.All
            .Where(e => ModelLocator.FindModel(e) == null)
            .ToList();
        if (missing.Count == 0)
        {
            MessageBox.Query("Already downloaded",
                "All catalog models are already available. Drop other GGUFs into\n" +
                ModelLocator.ModelsDir + " manually.",
                "OK");
            return Task.CompletedTask;
        }

        ChatModelEntry chosen;
        if (missing.Count == 1)
        {
            chosen = missing[0];
        }
        else
        {
            // Button labels must stay short — MessageBox lays them out on a
            // single row and a typical TUI is 80 columns. Use the short Id
            // for the buttons and put the full DisplayName / size in the
            // message body so the user can still see what they're picking.
            var detailLines = string.Join("\n",
                missing.Select((m, i) => $"  {i + 1}. {m.DisplayName}"));
            var labels = missing
                .Select(m => ShortLabel(m))
                .Append("Cancel")
                .ToArray();
            var pick = MessageBox.Query("Pick model to download",
                $"Target dir:\n  {ModelLocator.ModelsDir}\n\n" +
                "Available downloads:\n" + detailLines,
                labels);
            if (pick < 0 || pick >= missing.Count) return Task.CompletedTask;
            chosen = missing[pick];
        }

        return DownloadModelAsync(chosen);
    }

    // Compact button labels for the download picker. Mapping by Id so a
    // future catalog change can't accidentally reintroduce a 40-char button.
    private static string ShortLabel(ChatModelEntry e) => e.Id switch
    {
        "gemma-4-E4B-UD-Q4_K_XL" => "Gemma 4 E4B",
        "gemma-4-E2B-UD-Q4_K_XL" => "Gemma 4 E2B",
        "nvidia-nemotron-3-nano-4b-q4_k_m" => "Nemotron 3 Nano",
        _ => Trim(e.DisplayName, 16),
    };

    // Short tag for the transcript's "[<who>] reply" line. Pulls from the
    // template's Name so a future template (Qwen, Llama, …) doesn't need a
    // ChatView edit to label its answers correctly.
    private static string SpeakerLabelFor(IChatTemplate template) => template switch
    {
        NemotronChatTemplate => "Nemotron",
        GemmaChatTemplate => "Gemma",
        _ => template.Name,  // fallback for future templates
    };

    private async Task DownloadModelAsync(ChatModelEntry entry)
    {
        if (_busy || _mode == Mode.Downloading) return;

        var dest = ModelLocator.TargetPath(entry);

        // Sanity: if the model raced into existence (e.g. user dropped it in),
        // skip the download entirely.
        if (File.Exists(dest))
        {
            RefreshModels();
            return;
        }

        _busy = true;
        _mode = Mode.Downloading;
        _downloadCts = new CancellationTokenSource();

        ShowDownloading();
        _downloadTitleLabel.Text = $"Downloading {entry.DisplayName}";
        UpdateDownloadStatus(
            $"Source: {entry.DownloadUrl}\n" +
            $"Target: {dest}\n" +
            "Preparing…");
        _downloadBarLabel.Text = "";

        var startedAt = DateTime.UtcNow;
        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            var pct = p.TotalBytes > 0 ? (double)p.BytesReceived / p.TotalBytes : 0.0;
            var bar = RenderBar(pct, 40);
            var eta = EstimateEta(p, startedAt);
            UpdateDownloadStatus(
                $"Source: {entry.DownloadUrl}\n" +
                $"Target: {dest}\n" +
                $"{FormatSize(p.BytesReceived)} / {FormatSize(p.TotalBytes)}  " +
                $"({pct * 100:F1}%)  speed: {FormatSpeed(p.BytesPerSecond)}  ETA: {eta}");
            UpdateDownloadBar($"  {bar}");
        });

        try
        {
            await Task.Run(() => ModelDownloader.DownloadAsync(
                entry.DownloadUrl, dest, progress, _downloadCts.Token));

            UpdateDownloadStatus(
                $"Downloaded to:\n  {dest}\n" +
                "Returning to model selection…");
            await Task.Delay(700);
            _mode = Mode.Start;
            RefreshModels();
            ShowStart();
        }
        catch (OperationCanceledException)
        {
            UpdateDownloadStatus(
                "Cancelled. A partial .part file was kept on disk so the next\n" +
                "download attempt will resume from where you stopped.\n" +
                "Press Q to go back.");
            _mode = Mode.Start;
            // Leave the download screen up until the user presses Q so they
            // can read the message; HandleBack will pop back to Start.
            _cancelDownloadBtn.Text = "[Back]";
        }
        catch (Exception ex)
        {
            UpdateDownloadStatus($"Download failed: {ex.Message}\nPress Q to go back.");
            _mode = Mode.Start;
            _cancelDownloadBtn.Text = "[Back]";
        }
        finally
        {
            _busy = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void UpdateDownloadStatus(string text)
    {
        Application.Invoke(() =>
        {
            try { _downloadStatusLabel.Text = text; }
            catch { /* ignore */ }
        });
    }

    private void UpdateDownloadBar(string text)
    {
        Application.Invoke(() =>
        {
            try { _downloadBarLabel.Text = text; }
            catch { /* ignore */ }
        });
    }

    private static string RenderBar(double fraction, int width)
    {
        var clamped = Math.Clamp(fraction, 0.0, 1.0);
        var filled = (int)Math.Round(clamped * width);
        return "[" + new string('#', filled) + new string('.', width - filled) + "]";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int idx = -1;
        do { v /= 1024.0; idx++; } while (v >= 1024 && idx < units.Length - 1);
        return $"{v:F2} {units[idx]}";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "—";
        return $"{FormatSize((long)bytesPerSecond)}/s";
    }

    private static string EstimateEta(ModelDownloadProgress p, DateTime startedAt)
    {
        if (p.BytesPerSecond <= 0 || p.TotalBytes <= 0) return "—";
        var remaining = p.TotalBytes - p.BytesReceived;
        if (remaining <= 0) return "0s";
        var seconds = remaining / p.BytesPerSecond;
        if (seconds < 60) return $"{seconds:F0}s";
        if (seconds < 3600) return $"{seconds / 60:F0}m {seconds % 60:F0}s";
        return $"{seconds / 3600:F0}h {(seconds % 3600) / 60:F0}m";
    }

    // ------------------------------------------------------------------
    // Loading + chat
    // ------------------------------------------------------------------
    private async Task StartChatAsync()
    {
        if (string.IsNullOrEmpty(_selectedModelPath) || !File.Exists(_selectedModelPath))
        {
            MessageBox.ErrorQuery("Model not found",
                "Pick a valid GGUF model first, or download one to ~/.codescan/models/.",
                "OK");
            return;
        }
        if (_busy) return;

        // Snapshot the runtime selections before the UI hides them.
        var deviceIdx = _deviceRadio.SelectedItem;
        var ctxIdx = _ctxRadio.SelectedItem;
        if (ctxIdx < 0 || ctxIdx >= _ctxChoiceTokens.Count) ctxIdx = 0;
        var ctxSize = (uint)_ctxChoiceTokens[ctxIdx];

        var respIdx = _responseRadio.SelectedItem;
        if (respIdx < 0 || respIdx >= ResponseTokenChoices.Length) respIdx = DefaultResponseIndex;
        var maxResponseTokens = ResponseTokenChoices[respIdx];

        var device = deviceIdx > 0 && deviceIdx - 1 < _devices.Count ? _devices[deviceIdx - 1] : null;
        var gpuLayers = device != null ? 999 : 0;
        var mainGpu = device != null ? Math.Max(0, device.VulkanIndex) : 0;
        var backendLabel = device != null
            ? $"GPU{Math.Max(0, device.VulkanIndex)}/{device.Vendor}/Vulkan"
            : "CPU";
        var ctxLabel = ctxSize >= 1024 ? $"{ctxSize / 1024}K ctx" : $"{ctxSize} ctx";

        // If the user picked a non-Vulkan device, gracefully fall back to CPU
        // and tell them why.
        if (device != null && device.VulkanIndex < 0)
        {
            MessageBox.ErrorQuery("Device unavailable",
                $"Selected device '{device.Name}' has no Vulkan driver loaded.\n" +
                $"Install/update GPU drivers, or pick CPU / a Vulkan-capable device.",
                "OK");
            _busy = false;
            return;
        }

        _busy = true;
        _mode = Mode.Loading;

        // Switch UI immediately so the user sees status.
        ShowChat();
        _historyView.Text = "";
        AppendHistory($"Loading model: {Path.GetFileName(_selectedModelPath)}  [{backendLabel}, {ctxLabel}] …\n");
        UpdateStatus($"loading model ({backendLabel}, {ctxLabel}; first load can take 10–30s)…");

        try
        {
            _db = new SqliteStore(AppPaths.DbPath);
            _logger = ChatSessionLogger.Create(_selectedModelPath, _selectedProjectRoot);

            // Wire the load-progress callback so the user sees movement
            // during the 10–30s GGUF mmap + tensor decode.
            var progress = new Progress<float>(pct =>
                UpdateStatus($"loading model ({backendLabel}, {ctxLabel})… {pct * 100:F0}%"));

            _host = await Task.Run(() =>
                LlmHost.LoadAsync(_selectedModelPath,
                    contextSize: ctxSize,
                    gpuLayerCount: gpuLayers,
                    mainGpu: mainGpu,
                    progress: progress));

            // Pick the chat template that matches the selected GGUF. Catalog
            // entries pin a family; custom drops fall back to filename /
            // arch sniffing via ChatTemplateRegistry.
            var template = ChatTemplateRegistry.For(_selectedModelPath, _modelMeta);
            _assistantLabel = SpeakerLabelFor(template);

            _loop = new AgentChatLoop(
                _host,
                new CodeScanToolbelt(_db, _selectedProjectRoot),
                projectRoot: _selectedProjectRoot,
                logger: _logger,
                template: template,
                maxIterations: 6,
                maxTokensPerTurn: maxResponseTokens);

            AppendHistory(
                $"Model loaded.\n" +
                $"Chat template: {template.Name}\n" +
                (_selectedProjectRoot != null
                    ? $"Project context: #{_selectedProjectId} {_selectedProjectRoot}\n"
                    : "Project context: (none — only absolute paths can be read)\n") +
                $"Chat log: {_logger.LogPath}\n" +
                "Ask me anything about this codebase. Type a question and press Enter.\n\n");
            UpdateStatus("ready");
            _mode = Mode.Chat;
            _inputField.Text = "";
            _inputField.SetFocus();
        }
        catch (Exception ex)
        {
            AppendHistory($"\nModel load failed: {ex.Message}\n");
            UpdateStatus("load failed — press Q to go back");
            _mode = Mode.Start;
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowChat()
    {
        _titleLabel.Visible = false;
        _hintLabel.Visible = false;
        _modelList.Visible = false;
        _modelPathLabel.Visible = false;
        _projectPathLabel.Visible = false;
        _deviceLabel.Visible = false;
        _deviceRadio.Visible = false;
        _ctxLabel.Visible = false;
        _ctxRadio.Visible = false;
        _responseLabel.Visible = false;
        _responseRadio.Visible = false;
        _modelInfoLabel.Visible = false;
        _pickProjectBtn.Visible = false;
        _startBtn.Visible = false;
        _downloadBtn.Visible = false;

        _historyView.Visible = true;
        _statusLabel.Visible = true;
        _inputField.Visible = true;
        _sendBtn.Visible = true;
    }

    private async Task SendAsync()
    {
        if (_busy || _loop == null) return;
        var text = _inputField.Text?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        AppendHistory($"\n[You] {text}\n");
        _inputField.Text = "";
        UpdateStatus("thinking…");

        try
        {
            await foreach (var update in _loop.SendAsync(text, _cts.Token))
            {
                switch (update.Phase)
                {
                    case "thinking":
                        UpdateStatus(update.Text);
                        break;
                    case "progress":
                        // Token-count heartbeats — status-line only so the
                        // transcript stays readable.
                        UpdateStatus(update.Text);
                        break;
                    case "raw":
                        // Raw model JSON is captured in the disk log; we don't
                        // surface it in the transcript to keep the UI clean.
                        break;
                    case "tool":
                    {
                        var (toolName, brief) = FormatToolCall(update.Text);
                        _lastUiToolName = toolName;
                        AppendHistory($"  → [CodeScan] {brief}\n");
                        UpdateStatus($"running: {toolName}");
                        break;
                    }
                    case "tool_result":
                    {
                        var summary = FormatToolResult(_lastUiToolName, update.Text);
                        AppendHistory($"  ← {summary}\n");
                        UpdateStatus("reasoning…");
                        break;
                    }
                    case "done":
                        AppendHistory($"\n[{_assistantLabel}] {update.Text}\n");
                        UpdateStatus("ready");
                        break;
                    case "error":
                        AppendHistory($"\n[error] {update.Text}\n");
                        UpdateStatus("error — see log above");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            AppendHistory("\n[cancelled]\n");
            UpdateStatus("ready");
        }
        catch (Exception ex)
        {
            AppendHistory($"\n[error] {ex.Message}\n");
            UpdateStatus("error");
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            _inputField.SetFocus();
        }
    }

    private async Task TeardownAsync()
    {
        try { _cts?.Cancel(); } catch { }
        if (_loop != null) { await _loop.DisposeAsync(); _loop = null; }
        if (_host != null) { await _host.DisposeAsync(); _host = null; }
        _db?.Dispose();
        _db = null;
        _logger?.Dispose();
        _logger = null;
        _mode = Mode.Start;
        _busy = false;
        Application.Invoke(() =>
        {
            Hide();
            _onExit();
        });
    }

    // ------------------------------------------------------------------
    // UI helpers (must marshal to UI thread)
    // ------------------------------------------------------------------
    private void AppendHistory(string text)
    {
        Application.Invoke(() =>
        {
            try
            {
                _historyView.Text += text;
                _historyView.MoveEnd();
            }
            catch { /* UI update failed, ignore */ }
        });
    }

    private void UpdateStatus(string text)
    {
        Application.Invoke(() =>
        {
            try { _statusLabel.Text = $"[status] {text}"; }
            catch { /* ignore */ }
        });
    }
}
