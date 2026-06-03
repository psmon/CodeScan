namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Role of a recorded chat turn. Identical across every model family —
/// the family-specific bit is only how each role gets serialised into the
/// model's expected turn markers, which is the job of <see cref="IChatTemplate"/>.
/// </summary>
public enum ChatHistoryRole
{
    User,
    Model,
    ToolResult,
}

/// <summary>
/// One row in the running chat history. <see cref="ToolName"/> is only set
/// for <see cref="ChatHistoryRole.ToolResult"/> entries so templates can
/// label the result block ("Tool result for `db_search`: …").
/// </summary>
public sealed record ChatHistoryEntry(ChatHistoryRole Role, string Content, string? ToolName = null);

/// <summary>
/// Per-family chat-template renderer. Each implementation knows the model's
/// turn markers, EOG / antiprompt strings, and any family-specific guidance
/// to prepend to the user-supplied system prompt.
///
/// Why this is an interface and not just a static utility: Gemma 4 and
/// Nemotron 3 Nano use mutually incompatible markers (Gemma's
/// <c>&lt;|turn&gt;…&lt;turn|&gt;</c> vs. Nemotron's ChatML
/// <c>&lt;|im_start|&gt;…&lt;|im_end|&gt;</c>). Mixing them at runtime makes
/// the model emit EOS on its first decode step. Pinning a single template
/// per <see cref="AgentChatLoop"/> instance keeps the boundary explicit.
/// </summary>
public interface IChatTemplate
{
    string Name { get; }
    IReadOnlyList<string> AntiPrompts { get; }
    string BuildPrompt(string systemPrompt, IReadOnlyList<ChatHistoryEntry> history);
}
