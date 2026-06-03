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
/// turn so the parser never has to wrestle with free prose.
///
/// Implementation note (commit history): we used to drive this with an
/// InteractiveExecutor that retained KV cache across turns. That
/// produced a reliable empty-raw on iter=1 whenever a tool returned a
/// non-trivial result — the anti-prompt-driven stop dropped the closing
/// &lt;turn|&gt; from the cached prior-turn tail, so the model saw an
/// unfinished prior model turn and chose EOG as its first output of the
/// next turn. The current code uses a StatelessExecutor and rebuilds
/// the full prompt from a history list every turn, which keeps each turn
/// boundary explicit at the cost of re-prefilling prior tokens.
/// </summary>
public sealed class AgentChatLoop : IAsyncDisposable
{
    private readonly LlmHost _host;
    private readonly CodeScanToolbelt _toolbelt;
    private readonly StatelessExecutor _executor;
    private readonly Grammar _grammar;
    private readonly IChatTemplate _template;
    private readonly int _maxIterations;
    private readonly int _maxTokensPerTurn;
    private readonly float _temperature;
    private readonly string _systemPrompt;
    private readonly ChatSessionLogger? _logger;
    private readonly int _progressTokenInterval;

    // Conversation history. Every committed user / tool_result / model
    // turn lands here in order; the next prompt is template.BuildPrompt(history).
    private readonly List<ChatHistoryEntry> _history = new();

    private bool _disposed;

    // Channel used by the generation task to push "progress" updates back
    // into the SendAsync async iterator. Stays alive for one turn; reset on
    // every new GenerateOneTurnAsync call.
    private System.Threading.Channels.Channel<int>? _progressChannel;

    public AgentChatLoop(
        LlmHost host,
        CodeScanToolbelt toolbelt,
        string? projectRoot = null,
        ChatSessionLogger? logger = null,
        IChatTemplate? template = null,
        int maxIterations = 6,
        int maxTokensPerTurn = 512,
        float temperature = 0.0f,
        int progressTokenInterval = 32)
    {
        _host = host;
        _toolbelt = toolbelt;
        _logger = logger;
        // Default to Gemma so existing call sites (incl. the smoke test)
        // keep their pre-Nemotron behaviour. ChatView resolves the right
        // template via ChatTemplateRegistry and passes it in explicitly.
        _template = template ?? GemmaChatTemplate.Instance;
        _maxIterations = maxIterations;
        _maxTokensPerTurn = maxTokensPerTurn;
        _temperature = temperature;
        _progressTokenInterval = Math.Max(1, progressTokenInterval);
        _systemPrompt = CodeScanToolGrammar.BuildSystemPrompt(projectRoot);

        var (weights, modelParams) = host.GetInternals();
        _executor = new StatelessExecutor(weights, modelParams);
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

        // Commit the new user message to history once at the start of this
        // exchange; iter>0 turns push tool results, not new user messages.
        _history.Add(new ChatHistoryEntry(ChatHistoryRole.User, userMessage));

        for (var iter = 0; iter < _maxIterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            // Build the FULL prompt every turn — history-as-source-of-truth.
            // The stateless executor doesn't carry KV cache across calls
            // (it re-prefills) so prior model turns must be explicit. This
            // is what fixes the iter=1 empty-raw — every turn boundary is
            // now closed by the template, no anti-prompt stripping artifacts.
            var turnInput = _template.BuildPrompt(_systemPrompt, _history);

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

            // Empty output recovery: model emits zero tokens before the
            // grammar accepts anything. With Gemma 4 chat-template markers
            // applied correctly, this should be rare — when it still
            // happens the most likely causes are now (a) tool_result payload
            // hitting a numerical-instability edge in the Vulkan backend, or
            // (b) prompt actually overflowed the configured ctx. Surface the
            // diagnostic facts so the user can decide which knob to turn.
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                var ctxK = _host.ContextSize / 1024;
                var backend = _host.GpuLayerCount > 0 ? "GPU/Vulkan" : "CPU";
                var fallback =
                    $"(모델이 빈 응답을 반환했습니다. 현재: ctx={ctxK}K, backend={backend}, iter={iter}. " +
                    "이전 tool 결과가 너무 컸거나 채팅 템플릿/문자 인코딩 이슈일 수 있어요. " +
                    "~/.codescan/logs/llama-native.log 의 마지막 라인을 확인하면 정확한 원인이 보입니다.)";
                _logger?.Write("done", $"(empty raw → synthesized done; ctx={_host.ContextSize}, backend={backend}, iter={iter})");
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

            // Commit THIS model turn to history before we either return
            // (done) or feed back a tool result — keeps the next prompt's
            // model turns closed correctly.
            _history.Add(new ChatHistoryEntry(ChatHistoryRole.Model, rawJson!));

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

            // Push the tool result into history so the next iteration's
            // BuildPrompt picks it up as a user-role turn.
            _history.Add(new ChatHistoryEntry(ChatHistoryRole.ToolResult, toolResult, c.Tool));

            _logger?.Write("result", toolResult);
            yield return new ChatTurnUpdate("tool_result", Truncate(toolResult, 600));
        }

        var capErr = $"max iterations ({_maxIterations}) reached without 'done'";
        _logger?.Write("error", capErr);
        yield return new ChatTurnUpdate("error", capErr);
    }

    private async Task<string> GenerateOneTurnAsync(string turnText, CancellationToken ct)
    {
        // turnText is already wrapped in Gemma 4 turn markers by the caller
        // (FormatFirstTurn / FormatUserTurn / FormatToolResult), so we pass
        // it straight to the executor — no double-wrapping.
        var prompt = turnText;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _maxTokensPerTurn,
            AntiPrompts = _template.AntiPrompts,
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

        return StripTrailingAntiPrompt(sb.ToString(), _template.AntiPrompts).Trim();
    }

    private static string StripTrailingAntiPrompt(string text, IReadOnlyList<string> antiPrompts)
    {
        foreach (var anti in antiPrompts)
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
        // StatelessExecutor owns no LLamaContext to dispose — it recreates
        // the inference state on each InferAsync. Weights stay alive on the
        // host until LlmHost itself is disposed.
        return ValueTask.CompletedTask;
    }
}
