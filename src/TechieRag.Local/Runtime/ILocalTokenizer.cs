namespace TechieRag.Local.Runtime;

/// <summary>A model's own tokenizer (REQ-RAG-065 / BRD-105).</summary>
internal interface ILocalTokenizer : IDisposable
{
    /// <summary>
    /// Counts the tokens of a text exactly as the model sees it: special tokens parsed, no
    /// beginning-of-text token added, as ONNX Runtime GenAI 0.16.0's tokenizer encodes it (measured
    /// 2026-09-24 to agree with llama.cpp's count for Qwen2.5 and Phi-3).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number of tokens.</returns>
    int CountTokens(string text);
}
