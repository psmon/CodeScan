namespace CodeScan.Services.Llm.Tools;

/// <summary>
/// Picks an <see cref="IChatTemplate"/> for a given GGUF file. Three sources,
/// in priority order:
///   1) Explicit catalog hint (<see cref="ChatModelFamily"/>) — set when the
///      file came from <see cref="ChatModelCatalog.All"/>.
///   2) Filename sniff — "nemotron" / "gemma" substring matching for the
///      custom GGUFs the user drops into ~/.codescan/models/ themselves.
///   3) GGUF metadata's <c>general.architecture</c> field as a final hint.
/// Falls back to Gemma so existing single-model setups keep working.
/// </summary>
public static class ChatTemplateRegistry
{
    public static IChatTemplate ForFamily(ChatModelFamily family) => family switch
    {
        ChatModelFamily.Nemotron => NemotronChatTemplate.Instance,
        ChatModelFamily.Gemma => GemmaChatTemplate.Instance,
        _ => GemmaChatTemplate.Instance,
    };

    public static IChatTemplate For(string modelPath, GgufMetadata? metadata = null)
    {
        // 1) Catalog wins — exact filename match is unambiguous.
        var fileName = Path.GetFileName(modelPath);
        foreach (var entry in ChatModelCatalog.All)
        {
            if (string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                return ForFamily(entry.Family);
        }

        // 2) Filename heuristic for custom drops. We check before metadata
        // because metadata sniffing requires a full GGUF parse and may be
        // null on this code path.
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("nemotron")) return NemotronChatTemplate.Instance;
        if (lower.Contains("gemma")) return GemmaChatTemplate.Instance;

        // 3) Architecture string from GGUF metadata. "nemotron_h" is the
        // identifier the Mamba-2 Hybrid builds use; anything starting with
        // "gemma" goes through the Gemma template.
        if (metadata?.Architecture is string arch && arch.Length > 0)
        {
            var archLower = arch.ToLowerInvariant();
            if (archLower.StartsWith("nemotron")) return NemotronChatTemplate.Instance;
            if (archLower.StartsWith("gemma")) return GemmaChatTemplate.Instance;
        }

        return GemmaChatTemplate.Instance;
    }
}
