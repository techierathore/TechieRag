namespace TechieRag.Local.Runtime;

/// <summary>
/// The ONNX Runtime GenAI engine (<c>Microsoft.ML.OnnxRuntimeGenAI</c> 0.16.0) behind
/// <see cref="LocalLlmProvider"/> on every platform (REQ-RAG-058 / BRD-97, DECISIONS.md 2026-09-25).
/// </summary>
/// <remarks>
/// Loads a folder in GenAI's format on the CPU execution provider, tokenizes with the model's own
/// tokenizer, and generates one decoded piece per token. Stateless: each load returns its own
/// <see cref="OnnxGenAiModel"/>.
/// </remarks>
internal sealed class OnnxGenAiRuntime : ILocalLlmRuntime
{
    /// <summary>Gets the one instance; the runtime holds no state.</summary>
    public static OnnxGenAiRuntime Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "onnxruntime-genai";

    /// <inheritdoc/>
    public LocalModelFormat Format => LocalModelFormat.OnnxGenAi;

    /// <inheritdoc/>
    public ILocalTokenizer LoadTokenizer(string modelDirectory) => new OnnxGenAiTokenizer(modelDirectory);

    /// <inheritdoc/>
    public ILocalLlmModel Load(string modelDirectory, int contextSize, int? threads) =>
        new OnnxGenAiModel(modelDirectory, contextSize, threads);
}
