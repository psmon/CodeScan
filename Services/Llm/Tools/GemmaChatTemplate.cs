namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Gemma 4 chat-template markers. We used to emit Gemma 2/3 tokens
/// (<c>&lt;start_of_turn&gt;</c> / <c>&lt;end_of_turn&gt;</c>) here — but the
/// Gemma 4 GGUF's <c>tokenizer.chat_template</c> metadata and its EOS
/// token (id 106 = <c>&lt;turn|&gt;</c>) make it clear those names are
/// gone in this generation. The old format silently degraded on big
/// tool_result turns: the model couldn't parse the unfamiliar markers
/// and emitted EOS as its very first token, producing empty raw.
///
/// Reference: the chat_template macros extracted from the GGUF use:
///   • <c>&lt;|turn&gt;role\n…&lt;turn|&gt;\n</c> for every role
///     (system / user / model / tool)
///   • <c>&lt;|tool_response&gt;…&lt;tool_response|&gt;</c> for tool replies
///   • <c>&lt;|tool_call&gt;…&lt;tool_call|&gt;</c> for tool calls
///
/// We don't use the native tool_call/response brackets yet (our JSON
/// tool protocol is grammar-enforced on the model side and the toolbelt
/// reads JSON; switching is a follow-up) but the surrounding turn
/// markers MUST be correct or the model loses its place.
/// </summary>
public static class GemmaChatTemplate
{
    // EOS token = id 106, surfaced as the literal string "<turn|>" by the
    // tokenizer. We also list "<eos>" (id 1, marked EOG) as a safety net.
    public static readonly string[] AntiPrompts = new[] { "<turn|>", "<eos>" };

    /// <summary>
    /// First turn of a session: emit the system prompt as its own turn,
    /// then the user message, then leave the cursor inside an open model
    /// turn so generation continues directly.
    /// </summary>
    public static string FormatFirstTurn(string systemPrompt, string userMessage) =>
        $"<|turn>system\n{systemPrompt}<turn|>\n" +
        $"<|turn>user\n{userMessage}<turn|>\n" +
        $"<|turn>model\n";

    /// <summary>
    /// Continuation user turn — KV cache still warm from a prior turn,
    /// no system prompt needed.
    /// </summary>
    public static string FormatUserTurn(string userMessage) =>
        $"<|turn>user\n{userMessage}<turn|>\n" +
        $"<|turn>model\n";

    /// <summary>
    /// Tool-result turn. We deliberately do NOT use the Gemma 4 native
    /// <c>&lt;|tool_response&gt;</c> brackets here even though the model's
    /// chat_template macro emits them — token id 50 (<c>&lt;|tool_response&gt;</c>)
    /// is explicitly marked as an EOG token by the GGUF. Tokenising that
    /// literal string inside a user prompt makes the BPE tokenizer match
    /// the special-token id, the inference loop sees EOG on the very
    /// first decode step, and we get an empty raw response. Native
    /// tool_response usage requires emitting the marker from inside the
    /// model's own turn (paired with a prior &lt;|tool_call&gt;); since our
    /// JSON GBNF doesn't emit native tool_calls, we'd be lying about the
    /// turn structure if we faked tool_response brackets in user input.
    ///
    /// Fallback: plain text wrapper labelled with the tool name. Matches
    /// the Gemma 2/3 pattern the model also tolerates well.
    /// </summary>
    public static string FormatToolResult(string toolName, string toolResultJson) =>
        $"<|turn>user\n" +
        $"Tool result for `{toolName}`:\n{toolResultJson}<turn|>\n" +
        $"<|turn>model\n";
}
