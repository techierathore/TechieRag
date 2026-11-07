namespace TechieRag.Local.Runtime;

/// <summary>
/// Thrown by a runtime when a prompt with its pictures, as the engine counted it, does not fit the context;
/// the provider turns it into <see cref="LocalPromptTooLongException"/> naming the model (REQ-RAG-147).
/// </summary>
internal sealed class LocalImagePromptTooLongException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="promptTokens">The prompt's length the engine reported, or null when it gave none.</param>
    /// <param name="innerException">The engine's error.</param>
    public LocalImagePromptTooLongException(int? promptTokens, Exception? innerException = null)
        : base("The prompt with its pictures does not fit the context.", innerException)
    {
        PromptTokens = promptTokens;
    }

    /// <summary>Gets the prompt's length in tokens, or null.</summary>
    public int? PromptTokens { get; }
}
