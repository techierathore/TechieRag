namespace TechieRag.Llm;

/// <summary>
/// One model a service lists, with what the service itself publishes about it: its price, who runs it, and
/// whether it reasons (REQ-RAG-166, Lekhak TR-RAG-011).
/// </summary>
/// <remarks>
/// <para><b>Only what the service says.</b> Each value is read from the service's own model list and is empty
/// (<see langword="null"/>, or <see cref="LlmModelDataHandling.NotKnown"/>) when the service says nothing about
/// it. The library keeps no price table of its own for this list, so a figure here is always the service's.</para>
/// <para><b>Read today:</b> Venice (<c>model_spec.pricing</c> in US dollars per million tokens,
/// <c>model_spec.privacy</c>, <c>model_spec.capabilities.supportsReasoning</c>) and OpenRouter (<c>pricing.prompt</c>
/// and <c>pricing.completion</c> in US dollars per token, <c>supported_parameters</c>), recognised by the shape of
/// the answer, so any service answering in either shape is read the same way. LM Studio and Ollama models are
/// <see cref="LlmModelDataHandling.Local"/>. Every other service gives ids only.</para>
/// </remarks>
public sealed record LlmModelDetails
{
    /// <summary>Gets the model id, as a route takes it (Ollama's as <c>ollama/&lt;name&gt;</c> takes it).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the price of one million input tokens in US dollars, or <see langword="null"/> when the service publishes none.</summary>
    public decimal? InputPricePerMillionUsd { get; init; }

    /// <summary>Gets the price of one million output tokens in US dollars, or <see langword="null"/> when the service publishes none.</summary>
    public decimal? OutputPricePerMillionUsd { get; init; }

    /// <summary>Gets who sees the text: the service itself, another company it passes it to, or no one off this machine.</summary>
    public LlmModelDataHandling DataHandling { get; init; } = LlmModelDataHandling.NotKnown;

    /// <summary>Gets whether the model reasons before it answers, or <see langword="null"/> when the service does not say.</summary>
    public bool? SupportsReasoning { get; init; }
}
