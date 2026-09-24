namespace TechieRag.Local.Runtime;

/// <summary>
/// The seam between the one public provider and the inference engine underneath it: one
/// implementation per platform, chosen per the recorded comparison in <c>DECISIONS.md</c>
/// (REQ-RAG-058 / BRD-97).
/// </summary>
/// <remarks>
/// <para><b>Internal on purpose.</b> The app sees <see cref="LocalLlmProvider"/> only, so it cannot
/// tell which runtime is in use. Everything a runtime could do differently — the chat template, stop
/// sequences, the context-length refusal, the memory check, token counting for usage, the event
/// order — is done by the provider above this interface, so every runtime behaves the same.</para>
/// <para><b>What an implementation does.</b> Load a model folder in its own format, tokenize with the
/// model's own tokenizer, and generate one decoded piece per token for an already-templated prompt,
/// honouring the sampling settings, the JSON constraint where it can, and cancellation.</para>
/// </remarks>
internal interface ILocalLlmRuntime
{
    /// <summary>Gets the runtime's name, for logs only; never surfaced through the provider.</summary>
    string Name { get; }

    /// <summary>Gets the model format this runtime loads.</summary>
    LocalModelFormat Format { get; }

    /// <summary>
    /// Loads only the tokenizer of a model folder, cheaply, so token counts are exact before the
    /// weights are loaded (REQ-RAG-065).
    /// </summary>
    /// <param name="modelDirectory">The model folder.</param>
    /// <returns>The tokenizer.</returns>
    ILocalTokenizer LoadTokenizer(string modelDirectory);

    /// <summary>Loads a model folder for generation.</summary>
    /// <param name="modelDirectory">The model folder.</param>
    /// <param name="contextSize">The context size in tokens.</param>
    /// <param name="threads">CPU threads, or null for the runtime's choice.</param>
    /// <returns>The loaded model.</returns>
    ILocalLlmModel Load(string modelDirectory, int contextSize, int? threads);
}
