namespace TechieRag.Local.Runtime;

/// <summary>
/// Cleans text a runtime's tokenizer decoded, before the provider sees it (REQ-RAG-108).
/// </summary>
/// <remarks>
/// A SentencePiece tokenizer marks a space with <c>▁</c> (U+2581). ONNX Runtime GenAI 0.16.0 turns a
/// single leading marker back into a space but leaves a token made only of markers as it is: Gemma 3's
/// tokens for two, three or four spaces decode to <c>▁▁</c>, <c>▁▁▁</c>, <c>▁▁▁▁</c>, measured on
/// 2026-09-25 in both the streaming and the whole-sequence decoder, so an indented JSON answer came back
/// with the markers in its whitespace. The marker is never real answer text, so every one becomes a space.
/// </remarks>
internal static class DecodedText
{
    /// <summary>SentencePiece's word-boundary marker, LOWER ONE EIGHTH BLOCK.</summary>
    internal const char SentencePieceSpace = '▁';

    /// <summary>Replaces every SentencePiece space marker with a space.</summary>
    /// <param name="piece">A decoded piece.</param>
    /// <returns>The piece with plain spaces.</returns>
    public static string Clean(string piece) =>
        string.IsNullOrEmpty(piece) || piece.IndexOf(SentencePieceSpace) < 0
            ? piece
            : piece.Replace(SentencePieceSpace, ' ');
}
