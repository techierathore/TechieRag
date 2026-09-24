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
}
