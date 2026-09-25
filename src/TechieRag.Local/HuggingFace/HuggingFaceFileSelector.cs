using System.Text.RegularExpressions;
using TechieRag.Local.Runtime;

namespace TechieRag.Local.HuggingFace;

/// <summary>
/// Picks, from a Hugging Face folder, only the files ONNX Runtime GenAI loads (REQ-RAG-108).
/// </summary>
/// <remarks>
/// A model repository often holds far more than the model: a README, example scripts, benchmark
/// results, profiling traces (Arm's Gemma 3 1B carries a 10 MB one), lock files. Only these are
/// fetched, from the named folder itself and not its sub-folders: <c>genai_config.json</c>; the ONNX
/// graphs and their external weights (<c>*.onnx</c>, <c>*.onnx.data</c>, <c>*.onnx_data</c>); the
/// tokenizer's files; the chat template; and <c>config.json</c>. After the download every file
/// <c>genai_config.json</c> names is checked to be among them.
/// </remarks>
internal static partial class HuggingFaceFileSelector
{
    private static readonly HashSet<string> NamedFiles = new(StringComparer.Ordinal)
    {
        GenAiConfigInfo.FileName,
        "config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "added_tokens.json",
        "tokenizer.model",
        "vocab.json",
        "vocab.txt",
        "merges.txt",
        "chat_template.jinja",
        "chat_template.json"
    };

    /// <summary>Gets whether a file in the folder is one the engine needs.</summary>
    /// <param name="fileName">The file name, without its folder.</param>
    /// <returns>True to fetch it.</returns>
    public static bool IsRuntimeFile(string fileName) =>
        IsSafeFileName(fileName)
        && (NamedFiles.Contains(fileName)
            || fileName.EndsWith(".onnx", StringComparison.Ordinal)
            || fileName.EndsWith(".onnx.data", StringComparison.Ordinal)
            || fileName.EndsWith(".onnx_data", StringComparison.Ordinal));

    /// <summary>Gets whether a name is safe as a single file name in the model folder.</summary>
    /// <param name="fileName">The name.</param>
    /// <returns>True when it cannot leave the folder.</returns>
    public static bool IsSafeFileName(string fileName) =>
        FileNamePattern().IsMatch(fileName) && !fileName.Contains("..", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9_-][A-Za-z0-9._-]{0,199}$")]
    private static partial Regex FileNamePattern();
}
