namespace TechieRag.Local.Runtime;

/// <summary>
/// How <see cref="LocalGenerationSettings"/> map onto ONNX Runtime GenAI's search options and guidance
/// (REQ-RAG-058, REQ-RAG-059, REQ-RAG-063). Kept apart from the engine calls so the mapping is tested
/// without loading a model.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Length.</b> <c>max_length</c> counts prompt and answer together: prompt tokens plus
/// <see cref="LocalGenerationSettings.MaxTokens"/>, never more than the context size.</item>
/// <item><b>Temperature 0</b> is greedy (<c>do_sample</c> false); above 0 samples with
/// <c>temperature</c> and <c>top_p</c>. <c>top_k</c> stays at the model's <c>genai_config.json</c>
/// value (Qwen2.5 20, Phi-3 unset), which the options do not carry.</item>
/// <item><b>Seed</b> is <c>random_seed</c>.</item>
/// <item><b>Frequency and presence penalty</b> have no equivalent: GenAI has only the multiplicative
/// <c>repetition_penalty</c> (1 is none, above 1 discourages repeats, below 1 encourages them). Their
/// sum is mapped to <c>repetition_penalty = 1 + sum / 2</c>, clamped to 0.5…2, so OpenAI's range of
/// -2…2 per penalty keeps its direction; none set leaves the model's own value.</item>
/// <item><b>Stop sequences</b> are applied by the provider above the runtime.</item>
/// <item><b>JSON.</b> A schema is enforced with GenAI's <c>json_schema</c> guidance (constrained
/// decoding); JSON mode without a schema is guided by <see cref="AnyObjectSchema"/>.</item>
/// </list>
/// </remarks>
/// <param name="MaxLength">The <c>max_length</c> search option: prompt plus answer tokens.</param>
/// <param name="DoSample">The <c>do_sample</c> search option.</param>
/// <param name="Temperature">The <c>temperature</c> search option, or null when greedy.</param>
/// <param name="TopP">The <c>top_p</c> search option, or null when greedy.</param>
/// <param name="Seed">The <c>random_seed</c> search option, or null.</param>
/// <param name="RepetitionPenalty">The <c>repetition_penalty</c> search option, or null to keep the model's.</param>
/// <param name="JsonSchema">The <c>json_schema</c> guidance, or null for unconstrained text.</param>
internal sealed record OnnxGenAiSearchOptions(
    int MaxLength,
    bool DoSample,
    double? Temperature,
    double? TopP,
    double? Seed,
    double? RepetitionPenalty,
    string? JsonSchema)
{
    /// <summary>The guidance used for JSON mode when the call gives no schema: any JSON object.</summary>
    internal const string AnyObjectSchema = """{"type":"object"}""";

    /// <summary>
    /// Maps resolved settings to GenAI's options.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="promptTokens">The templated prompt's token count.</param>
    /// <param name="contextSize">The context size the model was loaded with.</param>
    /// <returns>The options.</returns>
    public static OnnxGenAiSearchOptions From(LocalGenerationSettings settings, int promptTokens, int contextSize)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var greedy = settings.Temperature <= 0f;
        return new OnnxGenAiSearchOptions(
            Math.Min(contextSize, promptTokens + Math.Max(1, settings.MaxTokens)),
            !greedy,
            greedy ? null : settings.Temperature,
            greedy ? null : settings.TopP,
            settings.Seed,
            RepetitionPenaltyFrom(settings.FrequencyPenalty, settings.PresencePenalty),
            settings.JsonSchema ?? (settings.JsonMode ? AnyObjectSchema : null));
    }

    /// <summary>
    /// Maps OpenAI-style additive penalties onto GenAI's multiplicative repetition penalty.
    /// </summary>
    /// <param name="frequencyPenalty">The frequency penalty, or null.</param>
    /// <param name="presencePenalty">The presence penalty, or null.</param>
    /// <returns>The repetition penalty, or null when neither is set.</returns>
    internal static double? RepetitionPenaltyFrom(float? frequencyPenalty, float? presencePenalty)
    {
        if (frequencyPenalty is null && presencePenalty is null)
        {
            return null;
        }

        var sum = (frequencyPenalty ?? 0f) + (presencePenalty ?? 0f);
        return Math.Clamp(1.0 + (sum / 2.0), 0.5, 2.0);
    }
}
