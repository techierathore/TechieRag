namespace TechieRag.Local.Runtime;

/// <summary>A model loaded by an <see cref="ILocalLlmRuntime"/>.</summary>
internal interface ILocalLlmModel : ILocalTokenizer
{
    /// <summary>
    /// Generates from an already-templated prompt, one decoded text piece per generated token, until
    /// the model's end token, <see cref="LocalGenerationSettings.MaxTokens"/>, or cancellation.
    /// </summary>
    /// <param name="prompt">The prompt with the chat template applied.</param>
    /// <param name="settings">Sampling, length and constraint settings.</param>
    /// <param name="cancellationToken">Stops generation.</param>
    /// <returns>One piece per token; a piece may be empty for a token that decodes to nothing yet.</returns>
    IAsyncEnumerable<string> GenerateAsync(string prompt, LocalGenerationSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Generates from a templated prompt that carries image markers and the pictures they stand for,
    /// one decoded text piece per generated token (REQ-RAG-147 / BRD-205).
    /// </summary>
    /// <param name="prompt">The prompt with the chat template applied and one marker per picture, in order.</param>
    /// <param name="images">The pictures, in the order of their markers.</param>
    /// <param name="promptTokens">
    /// The provider's lower bound of the prompt's length in tokens; the engine counts the pictures itself
    /// and refuses a prompt that leaves no room with <see cref="LocalImagePromptTooLongException"/>.
    /// </param>
    /// <param name="settings">Sampling, length and constraint settings.</param>
    /// <param name="cancellationToken">Stops generation.</param>
    /// <returns>One piece per token.</returns>
    /// <exception cref="NotSupportedException">The model does not read pictures.</exception>
    IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        IReadOnlyList<LocalImage> images,
        int promptTokens,
        LocalGenerationSettings settings,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This model does not read pictures.");

    /// <summary>
    /// Gets the prompt's length in tokens as the engine counted it on the last generation with pictures,
    /// or null when the runtime cannot say; the provider then reports its own upper bound.
    /// </summary>
    int? LastImagePromptTokens => null;

    /// <summary>
    /// Formats a conversation with the model's own chat template and opens the assistant's turn
    /// (<see cref="LocalChatTemplate.ModelDefined"/>, REQ-RAG-108).
    /// </summary>
    /// <param name="messagesJson">The conversation as a JSON array of <c>{"role", "content"}</c> objects.</param>
    /// <returns>The prompt text, with the model's start marker when its template writes one.</returns>
    /// <exception cref="NotSupportedException">The runtime cannot apply a model's own template.</exception>
    string ApplyChatTemplate(string messagesJson) =>
        throw new NotSupportedException("This runtime cannot apply a model's own chat template.");
}
