namespace TechieRag.Local;

/// <summary>
/// The text a model reads in place of a picture, which ONNX Runtime GenAI's multimodal processor replaces
/// with that picture's tokens (REQ-RAG-147 / BRD-205).
/// </summary>
/// <remarks>
/// Phi-3 vision, Phi-3.5 vision (<c>phi3v</c>) and Phi-4 multimodal (<c>phi4mm</c>) number their pictures from
/// one across the whole prompt: <c>&lt;|image_1|&gt;</c>, <c>&lt;|image_2|&gt;</c>. A model of another type that
/// reads pictures is refused with a message naming its type, never sent a marker it was not trained on.
/// </remarks>
internal static class ImagePromptMarker
{
    /// <summary>Gets the marker for one picture of a prompt.</summary>
    /// <param name="model">The model the prompt is for.</param>
    /// <param name="number">The picture's number in the prompt, from one.</param>
    /// <returns>The marker text.</returns>
    /// <exception cref="NotSupportedException">The library does not know how this model type places pictures.</exception>
    public static string For(LocalModel model, int number)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return model.ModelType?.ToLowerInvariant() switch
        {
            "phi3v" or "phi4mm" => $"<|image_{number}|>",
            _ => throw new NotSupportedException(
                $"The local model '{model.DisplayName}' ({model.Id}) reads pictures, but TechieRag.Local does not know how a model of type "
                + $"'{model.ModelType ?? "unknown"}' places them in its prompt; the pictures were not sent. "
                + "Use LocalModel.Phi35VisionInstruct, or a Phi-3 vision, Phi-3.5 vision or Phi-4 multimodal model.")
        };
    }
}
