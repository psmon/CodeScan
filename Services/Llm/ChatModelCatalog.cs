namespace CodeScan.Services.Llm;

/// <summary>
/// Chat-template family a GGUF was trained for. Drives turn-marker selection
/// in <see cref="Tools.ChatTemplateRegistry"/>; getting it wrong means the
/// model can't parse its own prompt and the first turn returns empty raw.
/// </summary>
public enum ChatModelFamily
{
    Gemma,
    Nemotron,
}

public sealed record ChatModelEntry(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    long ApproxBytes,
    ChatModelFamily Family);

public static class ChatModelCatalog
{
    public static readonly ChatModelEntry Gemma4E4B = new(
        Id: "gemma-4-E4B-UD-Q4_K_XL",
        DisplayName: "Gemma 4 E4B (UD-Q4_K_XL, ~5.1 GB)",
        FileName: "gemma-4-E4B-it-UD-Q4_K_XL.gguf",
        DownloadUrl: "https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-UD-Q4_K_XL.gguf",
        ApproxBytes: 5_101_718_208L,
        Family: ChatModelFamily.Gemma);

    public static readonly ChatModelEntry Gemma4E2B = new(
        Id: "gemma-4-E2B-UD-Q4_K_XL",
        DisplayName: "Gemma 4 E2B (UD-Q4_K_XL, ~3.2 GB)",
        FileName: "gemma-4-E2B-it-UD-Q4_K_XL.gguf",
        DownloadUrl: "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-UD-Q4_K_XL.gguf",
        ApproxBytes: 3_174_043_296L,
        Family: ChatModelFamily.Gemma);

    // NVIDIA's small reasoning SLM. Mamba-2 Hybrid architecture, 4B params,
    // trained at 262K context. Uses a ChatML-style chat template, so it
    // needs a separate IChatTemplate impl (see NemotronChatTemplate).
    public static readonly ChatModelEntry NemotronNano3_4B = new(
        Id: "nvidia-nemotron-3-nano-4b-q4_k_m",
        DisplayName: "NVIDIA Nemotron 3 Nano 4B (Q4_K_M, ~2.84 GB)",
        FileName: "NVIDIA-Nemotron3-Nano-4B-Q4_K_M.gguf",
        DownloadUrl: "https://huggingface.co/nvidia/NVIDIA-Nemotron-3-Nano-4B-GGUF/resolve/main/NVIDIA-Nemotron3-Nano-4B-Q4_K_M.gguf",
        ApproxBytes: 3_050_000_000L,
        Family: ChatModelFamily.Nemotron);

    public static readonly IReadOnlyList<ChatModelEntry> All = new[]
    {
        Gemma4E4B,
        Gemma4E2B,
        NemotronNano3_4B,
    };

    public static ChatModelEntry Default => Gemma4E4B;
}
