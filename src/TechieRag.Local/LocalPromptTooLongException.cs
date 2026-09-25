namespace TechieRag.Local;

/// <summary>
/// Thrown before inference when a prompt, after the chat template, does not fit the model's context
/// (REQ-RAG-059 / BRD-98). Nothing was generated.
/// </summary>
public sealed class LocalPromptTooLongException : ArgumentException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="modelId">The model.</param>
    /// <param name="promptTokens">The prompt's length in the model's tokens.</param>
    /// <param name="contextTokens">The context size the model runs with.</param>
    public LocalPromptTooLongException(string modelId, int promptTokens, int contextTokens)
        : base($"The prompt is {promptTokens} tokens, but the local model '{modelId}' runs with a {contextTokens}-token "
               + "context and needs room for at least one token of answer. Shorten the prompt or the retrieved context, "
               + "or raise LocalLlmOptions.ContextSize up to the model's limit.")
    {
        ModelId = modelId;
        PromptTokens = promptTokens;
        ContextTokens = contextTokens;
    }

    /// <summary>Gets the model.</summary>
    public string ModelId { get; }

    /// <summary>Gets the prompt's length in the model's tokens.</summary>
    public int PromptTokens { get; }

    /// <summary>Gets the context size the model runs with.</summary>
    public int ContextTokens { get; }
}
