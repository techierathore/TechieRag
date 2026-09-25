namespace TechieRag.Local.Runtime;

/// <summary>
/// The on-disk format a local runtime loads. Internal on purpose: the app picks a model, never a
/// runtime or a format (REQ-RAG-058 / BRD-97).
/// </summary>
/// <remarks>
/// One format per model: ONNX Runtime GenAI is the only engine on every platform (DECISIONS.md
/// 2026-09-25), so GGUF left the catalogue with LLamaSharp.
/// </remarks>
internal enum LocalModelFormat
{
    /// <summary>A folder with <c>genai_config.json</c>, as ONNX Runtime GenAI loads it.</summary>
    OnnxGenAi,

    /// <summary>A scripted format used only by the conformance suite's test runtime.</summary>
    Test
}
