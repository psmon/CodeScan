using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace CodeScan.Services.Llm.Tools;

public sealed record ToolCall(string Tool, JsonObject Args);

public sealed record ToolTurn(ToolCall Call, string ToolResult);

public sealed record ChatTurnUpdate(
    // "thinking"     — entering a turn, no token activity yet
    // "progress"     — periodic token-count update during generation
    // "raw"          — raw model JSON for THIS turn (always emitted before parse)
    // "tool"         — parsed tool call about to be executed
    // "tool_result"  — result string returned by the toolbelt (truncated)
    // "done"         — final user-visible message from the model
    // "error"        — fatal turn error; loop is over
    string Phase,
    string Text);

/// <summary>
/// Drives the Gemma 4 chat through a JSON tool-call loop. Each user
/// message kicks off N iterations:
///   model emits {"tool": "...", "args": {...}} → toolbelt runs it →
///   result fed back → repeat until `done` or iteration cap reached.
///
/// GBNF sampler-level grammar guarantees structurally valid JSON every
/// turn so the parser never has to wrestle with free prose. Across user
/// sends within ONE <see cref="AgentChatLoop"/> instance the KV cache is
/// preserved, so follow-up questions ("show me method X you just found")
/// keep the previous turn's context cheaply.
/// </summary>
public sealed class AgentChatLoop : IAsyncDisposable
{
    private readonly LlmHost _host;
    private readonly CodeScanToolbelt _toolbelt;
    private readonly LLamaContext _context;
    private readonly InteractiveExecutor _executor;
    private readonly Grammar _grammar;
    private readonly int _maxIterations;
    private readonly int _maxTokensPerTurn;
    private readonly float _temperature;
    private readonly string _systemPrompt;
    private readonly ChatSessionLogger? _logger;
    private readonly int _progressTokenInterval;

    private bool _firstMarkerSeen;   // tracks chat-template first vs continuation
    private bool _firstUserSend = true;  // emits system prompt only on first send
    private bool _disposed;
    private string _lastToolResult = "";

    // Channel used by the generation task to push "progress" updates back
    // into the SendAsync async iterator. Stays alive for one turn; reset on
    // every new GenerateOneTurnAsync call.
    private System.Threading.Channels.Channel<int>? _progressChannel;

    public AgentChatLoop(
        LlmHost host,
        CodeScanToolbelt toolbelt,
        string? projectRoot = null,
        ChatSessionLogger? logger = null,
        int maxIterations = 6,
        int maxTokensPerTurn = 512,
        float temperature = 0.0f,
        int progressTokenInterval = 32)
    {
        _host = host;
        _toolbelt = toolbelt;
        _logger = logger;
        _maxIterations = maxIterations;
        _maxTokensPerTurn = maxTokensPerTurn;
        _temperature = temperature;
        _progressTokenInterval = Math.Max(1, progressTokenInterval);
        _systemPrompt = CodeScanToolGrammar.BuildSystemPrompt(projectRoot);

        var (weights, modelParams) = host.GetInternals();
        _context = weights.CreateContext(modelParams);
        _executor = new InteractiveExecutor(_context);
        _grammar = new Grammar(CodeScanToolGrammar.Gbnf, CodeScanToolGrammar.GrammarRootRule);
    }

    /// <summary>
    /// Process one user message. Yields incremental updates so the TUI can
    /// stream the agent's tool chain as it unfolds. The final update has
    /// <see cref="ChatTurnUpdate.Phase"/> == "done" (success) or "error".
    /// </summary>
    public async IAsyncEnumerable<ChatTurnUpdate> SendAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AgentChatLoop));

        _logger?.Write("user", userMessage);

        for (var iter = 0; iter < _maxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            string turnInput;
            if (iter == 0)
            {
                turnInput = _firstUserSend
                    ? $"{_systemPrompt}\n\n--- USER ---\n{userMessage}"
                    : $"--- USER ---\n{userMessage}";
                _firstUserSend = false;
            }
            else
            {
                turnInput = $"--- TOOL RESULT ---\n{_lastToolResult}\n\n(Reply with the next single JSON tool call. Call \"done\" when satisfied.)";
            }

            yield return new ChatTurnUpdate("thinking", iter == 0 ? "Reading the question…" : "Reasoning…");

            // Start generation and stream token-count progress alongside it.
            // The generation task pushes ints into _progressChannel as it
            // crosses every _progressTokenInterval-token boundary; we drain
            // the channel in lockstep with the iterator's yields.
            _progressChannel = System.Threading.Channels.Channel.CreateUnbounded<int>(
                new System.Threading.Channels.UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                });

            var generateTask = GenerateOneTurnAsync(turnInput, ct);

            await foreach (var tokenCount in _progressChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return new ChatTurnUpdate("progress", $"generating… {tokenCount} tokens");

            string? rawJson = null;
            string? generateErr = null;
            bool cancelled = false;
            try { rawJson = await generateTask; }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { generateErr = $"model error: {ex.Message}"; }
            if (cancelled)
            {
                _logger?.Write("cancel", $"iter={iter}");
                yield break;
            }
            if (generateErr != null)
            {
                _logger?.Write("error", generateErr);
                yield return new ChatTurnUpdate("error", generateErr);
                yield break;
            }

            // Always emit + log the raw output BEFORE parsing so we keep
            // forensic evidence even when the parse fails.
            _logger?.Write("raw", rawJson!);
            yield return new ChatTurnUpdate("raw", rawJson!);

            // Empty output recovery: the model occasionally returns zero
            // tokens once the KV cache approaches the 4096 ctx limit
            // (observed after a verbose list_projects result). Treat it as
            // "give up gracefully" — synthesize a done message rather than
            // breaking the loop and stranding the user.
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                const string fallback =
                    "(모델이 응답을 생성하지 못했습니다. 컨텍스트가 한계에 가까울 수 있어요 — " +
                    "질문을 짧게 다시 보내거나 새 채팅 세션을 열어주세요.)";
                _logger?.Write("done", "(empty raw → synthesized done)");
                yield return new ChatTurnUpdate("done", fallback);
                yield break;
            }

            ToolCall? call = null;
            string? parseErr = null;
            try { call = ParseToolCall(rawJson!); }
            catch (JsonException ex) { parseErr = $"model returned unparseable JSON: {ex.Message}\nraw: {Truncate(rawJson!, 200)}"; }
            if (parseErr != null)
            {
                _logger?.Write("error", parseErr);
                yield return new ChatTurnUpdate("error", parseErr);
                yield break;
            }

            var c = call!;
            if (!CodeScanToolGrammar.KnownTools.Contains(c.Tool))
            {
                var err = $"model called unknown tool '{c.Tool}'";
                _logger?.Write("error", err);
                yield return new ChatTurnUpdate("error", err);
                yield break;
            }

            if (c.Tool == CodeScanToolGrammar.DoneToolName)
            {
                var msg = c.Args.TryGetPropertyValue("message", out var m) && m is JsonValue v
                    ? v.GetValue<string>()
                    : "(no message)";
                _logger?.Write("done", msg);
                yield return new ChatTurnUpdate("done", msg);
                yield break;
            }

            var toolPreview = $"{c.Tool}({Truncate(c.Args.ToJsonString(), 200)})";
            _logger?.Write("tool", toolPreview);
            yield return new ChatTurnUpdate("tool", toolPreview);

            var toolResult = _toolbelt.Execute(c.Tool, c.Args);
            _lastToolResult = toolResult;

            _logger?.Write("result", toolResult);
            yield return new ChatTurnUpdate("tool_result", Truncate(toolResult, 600));
        }

        var capErr = $"max iterations ({_maxIterations}) reached without 'done'";
        _logger?.Write("error", capErr);
        yield return new ChatTurnUpdate("error", capErr);
    }

    private async Task<string> GenerateOneTurnAsync(string turnText, CancellationToken ct)
    {
        var prompt = GemmaChatTemplate.Format(turnText, !_firstMarkerSeen);
        _firstMarkerSeen = true;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _maxTokensPerTurn,
            AntiPrompts = GemmaChatTemplate.AntiPrompts,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = _temperature,
                Grammar = _grammar,
                GrammarOptimization = DefaultSamplingPipeline.GrammarOptimizationMode.Extended,
            },
        };

        var sb = new StringBuilder();
        var tokensSeen = 0;
        var channel = _progressChannel;  // local copy — gets reset per turn
        try
        {
            await foreach (var tok in _executor.InferAsync(prompt, inferenceParams, ct))
            {
                sb.Append(tok);
                tokensSeen++;
                if (channel != null && tokensSeen % _progressTokenInterval == 0)
                    channel.Writer.TryWrite(tokensSeen);
            }
        }
        finally
        {
            channel?.Writer.TryComplete();
        }

        return StripTrailingAntiPrompt(sb.ToString()).Trim();
    }

    private static string StripTrailingAntiPrompt(string text)
    {
        foreach (var anti in GemmaChatTemplate.AntiPrompts)
        {
            for (var len = anti.Length; len > 0; len--)
                if (text.EndsWith(anti[..len], StringComparison.Ordinal))
                    return text[..^len];
        }
        return text;
    }

    internal static ToolCall ParseToolCall(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tool", out var toolEl))
            throw new JsonException("missing 'tool' field");
        if (toolEl.ValueKind != JsonValueKind.String)
            throw new JsonException($"'tool' must be a string, got {toolEl.ValueKind}");
        var tool = toolEl.GetString() ?? throw new JsonException("'tool' is null");
        var args = root.TryGetProperty("args", out var argsEl)
            ? JsonNode.Parse(argsEl.GetRawText())?.AsObject() ?? new JsonObject()
            : new JsonObject();
        return new ToolCall(tool, args);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _context.Dispose();
        return ValueTask.CompletedTask;
    }
}
