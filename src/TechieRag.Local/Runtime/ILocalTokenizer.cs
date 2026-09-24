namespace TechieRag.Local.Runtime;

/// <summary>A model's own tokenizer (REQ-RAG-065 / BRD-105).</summary>
internal interface ILocalTokenizer : IDisposable
{
    /// <summary>
    /// Counts the tokens of a text exactly as the model sees it: special tokens parsed, no
    /// beginning-of-text token added. Measured 2026-09-24, llama.cpp (LLamaSharp 0.27.0) and ONNX
    /// Runtime GenAI 0.16.0 agree on this count for Qwen2.5 and Phi-3.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number of tokens.</returns>
    int CountTokens(string text);
}
