using Microsoft.ML.OnnxRuntimeGenAI;

namespace TechieRag.Local.Runtime;

/// <summary>
/// A model's own tokenizer through ONNX Runtime GenAI, loaded from the model folder without the
/// weights (REQ-RAG-065 / BRD-105).
/// </summary>
internal sealed class OnnxGenAiTokenizer : ILocalTokenizer
{
    private readonly Tokenizer tokenizer;

    /// <summary>Loads the tokenizer files of a model folder.</summary>
    /// <param name="modelDirectory">The folder with <c>genai_config.json</c> and the tokenizer files.</param>
    public OnnxGenAiTokenizer(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        tokenizer = new Tokenizer(modelDirectory);
    }

    /// <inheritdoc/>
    public int CountTokens(string text) => Count(tokenizer, text);

    /// <inheritdoc/>
    public void Dispose() => tokenizer.Dispose();

    /// <summary>Counts a text's tokens with a GenAI tokenizer.</summary>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="text">The text.</param>
    /// <returns>The number of tokens.</returns>
    internal static int Count(Tokenizer tokenizer, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        using var sequences = tokenizer.Encode(text);
        return sequences[0].Length;
    }
}
