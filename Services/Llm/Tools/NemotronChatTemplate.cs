namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// NVIDIA Nemotron 3 Nano chat template — ChatML format with
/// <c>&lt;|im_start|&gt;role\n…&lt;|im_end|&gt;</c> turn markers. Distinct from
/// Gemma 4's <c>&lt;|turn&gt;…&lt;turn|&gt;</c> family. Using the wrong template
/// for the model means EOS fires on the very first decode step.
///
/// Reasoning suppression: Nemotron 3 Nano is a "unified" model trained to
/// emit <c>&lt;think&gt;…&lt;/think&gt;</c> blocks before its visible answer when
/// the system prompt allows it. Our GBNF grammar forbids anything before
/// the JSON tool-call object, so a thinking preamble would be rejected on
/// the very first token and produce empty raw. We therefore inject an
/// explicit "no-thinking" instruction into the system turn for this
/// family — this matches the documented <c>/no_think</c> control directive
/// the NVIDIA model card describes for non-reasoning operation.
/// </summary>
public sealed class NemotronChatTemplate : IChatTemplate
{
    public static readonly NemotronChatTemplate Instance = new();

    public string Name => "Nemotron 3 Nano (ChatML)";

    // Primary stop: the per-turn closer that the model's tokenizer emits as
    // a single special token. We also list a bare <|endoftext|> as a safety
    // net for the OpenAI-style EOS that some Nemotron GGUF builds carry.
    public static readonly string[] AntiPromptStrings = new[] { "<|im_end|>", "<|endoftext|>" };

    public IReadOnlyList<string> AntiPrompts => AntiPromptStrings;

    private const string NoThinkPreamble =
        "/no_think\nRespond with ONE JSON tool-call object per turn — no <think> blocks, no prose, no code fences.\n\n";

    public string BuildPrompt(string systemPrompt, IReadOnlyList<ChatHistoryEntry> history)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<|im_start|>system\n")
          .Append(NoThinkPreamble)
          .Append(systemPrompt)
          .Append("<|im_end|>\n");
        foreach (var entry in history)
        {
            switch (entry.Role)
            {
                case ChatHistoryRole.User:
                    sb.Append("<|im_start|>user\n")
                      .Append(entry.Content)
                      .Append("<|im_end|>\n");
                    break;
                case ChatHistoryRole.ToolResult:
                    sb.Append("<|im_start|>user\n")
                      .Append("Tool result for `").Append(entry.ToolName ?? "(unknown)").Append("`:\n")
                      .Append(entry.Content)
                      .Append("<|im_end|>\n");
                    break;
                case ChatHistoryRole.Model:
                    sb.Append("<|im_start|>assistant\n")
                      .Append(entry.Content)
                      .Append("<|im_end|>\n");
                    break;
            }
        }
        // Open assistant turn so generation begins inside the model's reply.
        sb.Append("<|im_start|>assistant\n");
        return sb.ToString();
    }
}
