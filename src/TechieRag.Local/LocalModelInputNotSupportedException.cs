using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// Thrown before any inference when a chat turn carries input the loaded local model cannot read, such as a
/// picture sent to a text-only model (REQ-RAG-151 / BRD-209). The input is never dropped without notice.
/// </summary>
public sealed class LocalModelInputNotSupportedException : NotSupportedException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="modelId">The model's id.</param>
    /// <param name="modelName">The model's display name.</param>
    /// <param name="inputKind">The kind of input the model cannot read.</param>
    /// <param name="count">How many parts of that kind the turn carried.</param>
    public LocalModelInputNotSupportedException(string modelId, string modelName, ChatContentKind inputKind, int count)
        : base($"The local model '{modelName}' ({modelId}) does not read pictures, and this chat turn has {count} "
               + $"{(count == 1 ? "picture" : "pictures")}. Nothing was sent to the model. Use a model that reads pictures, "
               + "such as LocalModel.Phi35VisionInstruct (check LocalModel.ReadsImages), or send the turn without pictures.")
    {
        ModelId = modelId;
        ModelName = modelName;
        InputKind = inputKind;
        Count = count;
    }

    /// <summary>Gets the id of the model that cannot read the input.</summary>
    public string ModelId { get; }

    /// <summary>Gets the display name of the model that cannot read the input.</summary>
    public string ModelName { get; }

    /// <summary>Gets the kind of input the model cannot read.</summary>
    public ChatContentKind InputKind { get; }

    /// <summary>Gets how many parts of that kind the turn carried.</summary>
    public int Count { get; }
}
