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
    /// Formats a conversation with the model's own chat template and opens the assistant's turn
    /// (<see cref="LocalChatTemplate.ModelDefined"/>, REQ-RAG-108).
    /// </summary>
    /// <param name="messagesJson">The conversation as a JSON array of <c>{"role", "content"}</c> objects.</param>
    /// <returns>The prompt text, with the model's start marker when its template writes one.</returns>
    /// <exception cref="NotSupportedException">The runtime cannot apply a model's own template.</exception>
    string ApplyChatTemplate(string messagesJson) =>
        throw new NotSupportedException("This runtime cannot apply a model's own chat template.");
}
