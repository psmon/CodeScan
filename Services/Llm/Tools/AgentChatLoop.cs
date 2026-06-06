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
    // "stream_delta" — incremental clean text from a `done` message body
    //                  as it is being generated. Multiple deltas per turn.
    //                  UI appends directly to the transcript.
    // "stream_end"   — terminal marker for a turn rendered via stream_delta.
    //                  No payload; UI closes the speaker block. Replaces
    //                  "done" when streaming was used.
    // "raw"          — raw model JSON for THIS turn (always emitted before parse)
    // "tool"         — parsed tool call about to be executed
    // "tool_result"  — result string returned by the toolbelt (truncated)
    // "done"         — final user-visible message. Only emitted on
    //                  non-streamed paths (empty raw, salvage without
    //                  prior streaming, fallthrough).
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

    // Channel used by the generation task to push live updates (progress
    // counts AND stream_delta body chunks) back into the SendAsync async
    // iterator. Stays alive for one turn; reset on every new
    // GenerateOneTurnAsync call.
    private System.Threading.Channels.Channel<ChatTurnUpdate>? _streamChannel;

    // Tracks the JSON streaming state of the current generation turn —
    // detects when the model has crossed into the `done` tool's `message`
    // body and emits cleaned text chunks. Reset per turn alongside the
    // channel above. Null between turns.
    private JsonDoneStreamer? _doneStreamer;

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

            // Start generation and stream live updates alongside it. The
            // generation task pushes ChatTurnUpdate values — periodic
            // "progress" counters every _progressTokenInterval tokens AND
            // "stream_delta" chunks of clean body text as soon as we know
            // the model is writing the `done` message. We drain in lockstep
            // with the iterator's yields so the UI sees the answer appear
            // mid-generation instead of waiting for the full turn.
            _streamChannel = System.Threading.Channels.Channel.CreateUnbounded<ChatTurnUpdate>(
                new System.Threading.Channels.UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                });
            _doneStreamer = new JsonDoneStreamer();

            // Push the synchronous-from-tok-to-tok llama.cpp inference loop
            // onto a thread-pool worker. Otherwise the StatelessExecutor's
            // InferAsync — which returns a sync-completed ValueTask per
            // token on the Vulkan backend — would tie up the calling
            // (UI) thread for the whole turn, and the channel writes below
            // would only be DRAINED after generation finishes. The reader
            // here lives on the iterator's caller thread; the writer side
            // now runs in parallel, so each TryWrite wakes the reader.
            // Without this, the prior `stream_delta` work appears to "not
            // stream" — all deltas arrive at the very end in one burst.
            var generateTask = Task.Run(() => GenerateOneTurnAsync(turnInput, ct), ct);

            await foreach (var upd in _streamChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return upd;

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
                // Token-budget truncation rescue: when the model has emitted
                // {"tool":"done","args":{"message":"…} and then runs out of
                // tokens mid-string, JsonDocument throws. The user has waited
                // for the whole turn — surfacing "[error] unparseable JSON"
                // throws their work away. Salvage the partial message text
                // when we can clearly tell that's what happened, and present
                // it as a done update with a truncation notice.
                var salvaged = TrySalvageTruncatedDoneMessage(rawJson!);
                if (salvaged != null)
                {
                    var notice = "\n\n_(응답이 토큰 한도에 도달해 잘렸습니다 — 응답 길이를 Max로 올리거나 더 좁은 범위로 다시 질문해 주세요.)_";
                    _logger?.Write("done", $"(salvaged from truncated JSON) {salvaged}");
                    if (_doneStreamer?.StreamedAnything == true)
                    {
                        // Body was already painted to the transcript by the
                        // stream_delta pipeline; only the closing notice is
                        // missing. Avoid the duplicate by piggy-backing it
                        // as one more delta and closing out.
                        yield return new ChatTurnUpdate("stream_delta", notice);
                        yield return new ChatTurnUpdate("stream_end", "");
                    }
                    else
                    {
                        yield return new ChatTurnUpdate("done", salvaged + notice);
                    }
                    yield break;
                }
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
                if (_doneStreamer?.StreamedAnything == true)
                {
                    // Body painted live during generation — UI just needs
                    // the close signal so it can restore the prompt.
                    yield return new ChatTurnUpdate("stream_end", "");
                }
                else
                {
                    yield return new ChatTurnUpdate("done", msg);
                }
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
                // Penalize tokens that already appear in the last
                // PenaltyCount-token window. Cheap insurance against the
                // degenerate `\t\t\t…` (or `\u…\u…`) loops that this model
                // family slides into when a long code-fence echo runs out
                // of natural variety mid-string. The logit penalty is
                // applied before the grammar accept/reject step, so the
                // grammar still guarantees structurally valid JSON.
                // 1.08 is light enough not to noticeably hurt natural
                // Korean/English prose; 64 tokens is roughly two screen
                // rows of wrap, big enough to catch the loop early.
                // PenalizeNewline stays false so paragraph formatting
                // isn't smeared.
                RepeatPenalty = 1.08f,
                PenaltyCount = 64,
                PenalizeNewline = false,
            },
        };

        var sb = new StringBuilder();
        var tokensSeen = 0;
        var channel = _streamChannel;    // local copies — both get reset per turn
        var streamer = _doneStreamer;
        try
        {
            await foreach (var tok in _executor.InferAsync(prompt, inferenceParams, ct))
            {
                sb.Append(tok);
                tokensSeen++;
                if (channel == null) continue;

                // Push body chunks the moment the streamer detects we're
                // inside the `done` message string. The first delta pays
                // for the JSON-preamble scan; subsequent deltas are a thin
                // per-character pass.
                var delta = streamer?.Feed(tok);
                if (!string.IsNullOrEmpty(delta))
                    channel.Writer.TryWrite(new ChatTurnUpdate("stream_delta", delta));

                if (tokensSeen % _progressTokenInterval == 0)
                    channel.Writer.TryWrite(new ChatTurnUpdate("progress", $"generating… {tokensSeen} tokens"));
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

    /// <summary>
    /// When grammar-constrained generation runs out of tokens mid-string,
    /// the rawJson looks like:
    ///   {"tool": "done", "args": {"message": "## Heading\n\nSome text...
    /// — no closing quote, no closing braces. JsonDocument refuses to parse
    /// that. We can still return the in-flight message text by:
    ///   1) finding the opening of `"message":"…`
    ///   2) unescaping just the JSON string body up to wherever it cuts off
    ///   3) dropping any trailing dangling backslash (would be a half-emitted
    ///      escape sequence — `\u00`, `\x`, etc.)
    /// Returns null when the raw doesn't look like a truncated done call
    /// (e.g. truncated tool call or malformed in a different way) — callers
    /// fall back to the regular error path in that case.
    /// </summary>
    internal static string? TrySalvageTruncatedDoneMessage(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return null;
        // Must look like a done call. Grammar emits the keys in a fixed
        // order (tool first, args second) so a substring check is enough.
        if (!rawJson.Contains("\"tool\"") || !rawJson.Contains("\"done\"")) return null;

        // Locate the start of the message value. Allow whitespace around
        // the colon — the grammar permits it. Hand-rolled scan because the
        // project pins all regex behind [GeneratedRegex] for AOT safety
        // (see CLAUDE.md) and the pattern here is too cheap to need one.
        var keyIdx = rawJson.IndexOf("\"message\"", StringComparison.Ordinal);
        if (keyIdx < 0) return null;
        var i0 = keyIdx + "\"message\"".Length;
        while (i0 < rawJson.Length && (rawJson[i0] == ' ' || rawJson[i0] == '\t')) i0++;
        if (i0 >= rawJson.Length || rawJson[i0] != ':') return null;
        i0++;
        while (i0 < rawJson.Length && (rawJson[i0] == ' ' || rawJson[i0] == '\t')) i0++;
        if (i0 >= rawJson.Length || rawJson[i0] != '"') return null;
        var bodyStart = i0 + 1;
        if (bodyStart >= rawJson.Length) return null;

        // Walk forward, decoding JSON escapes, until we either hit a real
        // unescaped closing quote (the normal terminator — but then the
        // outer parser would have succeeded, so we wouldn't be here) OR
        // we run off the end of the string mid-escape, which is the case
        // we're rescuing.
        var sb = new StringBuilder(rawJson.Length - bodyStart);
        for (var i = bodyStart; i < rawJson.Length; i++)
        {
            var c = rawJson[i];
            if (c == '"') break;   // properly closed — fall through to return
            if (c == '\\')
            {
                if (i + 1 >= rawJson.Length) break;  // dangling backslash
                var esc = rawJson[i + 1];
                switch (esc)
                {
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '/': sb.Append('/'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case 'f': sb.Append('\f'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'u':
                        if (i + 5 >= rawJson.Length) return Result(sb);  // half-emitted \uXX
                        var hex = rawJson.AsSpan(i + 2, 4);
                        if (!ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                             System.Globalization.CultureInfo.InvariantCulture, out var cp))
                            return Result(sb);
                        sb.Append((char)cp);
                        i += 5;
                        break;
                    default:
                        // Unknown escape — grammar shouldn't allow it, but
                        // if it slipped through just stop and return what
                        // we have.
                        return Result(sb);
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return Result(sb);

        static string? Result(StringBuilder sb)
        {
            var text = sb.ToString().TrimEnd();
            return text.Length == 0 ? null : text;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        // StatelessExecutor owns no LLamaContext to dispose — it recreates
        // the inference state on each InferAsync. Weights stay alive on the
        // host until LlmHost itself is disposed.
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Incremental scanner that watches the model's per-token JSON output,
    /// detects the moment it enters the `done` tool's `message` string,
    /// and yields cleaned body chunks (JSON-unescaped) ready to paint to
    /// the TUI transcript. Built char-by-char so the cost is amortised
    /// across the existing token-loop without re-scanning the buffer.
    ///
    /// Lifecycle: instantiated fresh at the start of every generation
    /// turn, then `Feed(token)` once per llama-token. The streamer is a
    /// no-op for non-done tool calls — `Feed` simply returns null forever
    /// because the `"message":"` anchor never appears.
    /// </summary>
    internal sealed class JsonDoneStreamer
    {
        // The "message" key — we then need to skip optional whitespace
        // around the `:` and find the opening `"`. Real Gemma/Nemotron
        // output uses `"message": "` (with a space after the colon), not
        // the canonically-tight `"message":"`. An exact-literal IndexOf
        // for the tight form never matches the spaced form, the streamer
        // stays parked in Preamble forever, and the user sees the answer
        // arrive in one burst at the end — which is exactly the bug that
        // chased the earlier "streaming doesn't work" reports.
        private const string MessageKey = "\"message\"";

        private enum Phase
        {
            // Buffering raw output, scanning for the message anchor.
            Preamble,
            // Inside the message string — process chars with JSON escape rules.
            InMessage,
            // Saw the closing quote — done emitting.
            AfterMessage,
        }

        private Phase _phase = Phase.Preamble;
        private readonly StringBuilder _preamble = new();
        private bool _afterBackslash;
        private int _uHexLeft;             // 0 normally; 4..1 while collecting hex digits of `\u`
        private readonly char[] _uHex = new char[4];
        private int _uHexIdx;
        private bool _emitted;

        public bool StreamedAnything => _emitted;

        public string? Feed(string chunk)
        {
            if (_phase == Phase.AfterMessage || string.IsNullOrEmpty(chunk)) return null;

            string toScan;
            if (_phase == Phase.Preamble)
            {
                _preamble.Append(chunk);
                if (!TryFindBodyStart(_preamble.ToString(), out var bodyStart))
                    return null;            // anchor incomplete — keep buffering
                _phase = Phase.InMessage;
                toScan = _preamble.ToString()[bodyStart..];
                _preamble.Clear();
            }
            else
            {
                toScan = chunk;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < toScan.Length; i++)
            {
                if (_phase == Phase.AfterMessage) break;
                var c = toScan[i];

                if (_uHexLeft > 0)
                {
                    _uHex[_uHexIdx++] = c;
                    _uHexLeft--;
                    if (_uHexLeft == 0)
                    {
                        var hex = new string(_uHex, 0, _uHexIdx);
                        _uHexIdx = 0;
                        if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                            System.Globalization.CultureInfo.InvariantCulture, out var cp))
                            sb.Append((char)cp);
                    }
                    continue;
                }

                if (_afterBackslash)
                {
                    _afterBackslash = false;
                    switch (c)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':  _uHexLeft = 4; _uHexIdx = 0; break;
                        default:   /* grammar rejects others — ignore defensively */ break;
                    }
                    continue;
                }

                if (c == '\\') { _afterBackslash = true; continue; }
                if (c == '"')  { _phase = Phase.AfterMessage; break; }
                sb.Append(c);
            }

            if (sb.Length == 0) return null;
            _emitted = true;
            return sb.ToString();
        }

        // Looks for `"message"<ws>?:<ws>?"` and reports the index of the
        // first character of the body — i.e. the byte just past the
        // opening quote. Returns false when the anchor is incomplete so
        // the caller knows to keep buffering more tokens.
        private static bool TryFindBodyStart(string buf, out int bodyStart)
        {
            bodyStart = 0;
            var keyIdx = buf.IndexOf(MessageKey, StringComparison.Ordinal);
            if (keyIdx < 0) return false;
            var i = keyIdx + MessageKey.Length;
            while (i < buf.Length && IsJsonWs(buf[i])) i++;
            if (i >= buf.Length) return false;
            if (buf[i] != ':') return false;            // shouldn't happen under grammar
            i++;
            while (i < buf.Length && IsJsonWs(buf[i])) i++;
            if (i >= buf.Length) return false;
            if (buf[i] != '"') return false;            // shouldn't happen under grammar
            bodyStart = i + 1;
            return true;

            static bool IsJsonWs(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }
    }
}
