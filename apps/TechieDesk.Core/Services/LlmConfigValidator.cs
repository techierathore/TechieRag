using TechieRag;

namespace TechieDesk.Services;

/// <summary>
/// A single save-time validation failure, named against the form field that caused it.
/// </summary>
/// <param name="Field">The logical field key, one of the <see cref="LlmConfigValidator"/> field constants.</param>
/// <param name="MessageKey">Resource key for the failure shown under the offending field.</param>
/// <param name="ProviderName">
/// The provider's brand name, substituted into <paramref name="MessageKey"/> as <c>{0}</c>.
/// </param>
/// <param name="BlocksInstanceBuild">
/// True when the missing value makes <c>TechieRagBuilder.Build()</c> throw, so the configuration must
/// never reach disk and must never be handed to the builder (REQ-UI-043).
/// </param>
/// <remarks>
/// <para>
/// REQ-UI-051: the failure is carried as a KEY plus its one argument rather than as an English
/// sentence. Before this, the LLM Settings page had to re-derive which of six messages the
/// validator had produced from the <c>Field</c> constant alone — including guessing the
/// endpoint-versus-base-URL and model-versus-deployment splits a second time — and any arm it
/// guessed wrong about fell through to the validator's English.
/// </para>
/// <para>
/// <paramref name="ProviderName"/> is deliberately NOT a key. Every provider but
/// <see cref="LlmSource.None"/> is a brand rendered as itself, in Latin script, inside a Devanagari
/// sentence; the same rule the razor counters already apply to "LM Studio" and "Azure AI Foundry".
/// </para>
/// </remarks>
public sealed record LlmValidationError(
    string Field,
    string MessageKey,
    string ProviderName,
    bool BlocksInstanceBuild)
{
    /// <summary>
    /// Gets an invariant, greppable rendering of the failure, for a log line or an exception.
    /// </summary>
    /// <returns>The key and provider, e.g. <c>LlmValidationEndpointRequired(OpenAI-compatible)</c>.</returns>
    /// <remarks>
    /// A log is read by whoever is debugging the install, not by the person using it, and it has to
    /// mean the same thing whatever language the machine is set to. Naming the resource key rather
    /// than resolving it keeps the line greppable, keeps it identical across cultures, and — the
    /// reason it matters here — means <see cref="LlmConfigValidationException"/> can be constructed
    /// by <c>TechieRagConfigService</c> and <c>TechieRagManager</c> without either of them needing
    /// a localizer they have no business holding.
    /// </remarks>
    public string Describe() => $"{MessageKey}({ProviderName})";
}

/// <summary>
/// Raised when a configuration that cannot produce a working TechieRag instance is offered for saving.
/// </summary>
/// <remarks>
/// <para><b>Why (REQ-UI-043 / BRD-136):</b> a half-configured provider — the canonical case being
/// OpenAI-compatible with an API key but no endpoint — used to save cleanly and then throw
/// <c>InvalidOperationException: Endpoint is required for OpenAI-compatible LLM provider</c> on every
/// unrelated page that builds a TechieRag instance, <c>/token-usage</c> included. Validation now
/// happens at save time, named on the field that caused it.</para>
/// </remarks>
public sealed class LlmConfigValidationException : InvalidOperationException
{
    /// <summary>
    /// Creates a new <see cref="LlmConfigValidationException"/>.
    /// </summary>
    /// <param name="errors">The validation failures that blocked the save.</param>
    public LlmConfigValidationException(IReadOnlyList<LlmValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Gets the validation failures that blocked the save.</summary>
    public IReadOnlyList<LlmValidationError> Errors { get; }

    /// <summary>
    /// Renders the failures into one sentence suitable for a toast or a log line.
    /// </summary>
    /// <param name="errors">The validation failures.</param>
    /// <returns>A single-line summary of every failure.</returns>
    /// <remarks>
    /// REQ-UI-051: invariant by design. This text is an EXCEPTION message — it lands in the
    /// application log and in a developer's debugger, never on a screen; the LLM Settings page
    /// reads <see cref="Errors"/> and localizes each one itself. Translating an exception would
    /// make the log say something different depending on the machine's language setting.
    /// </remarks>
    private static string BuildMessage(IReadOnlyList<LlmValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return string.Join(" ", errors.Select(e => e.Describe()));
    }
}

/// <summary>
/// Decides which fields a given <see cref="LlmSource"/> actually needs, and validates an
/// <see cref="LlmConfig"/> against that set (REQ-UI-043 / BRD-136).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> one authority for the provider field matrix, shared by the
/// <c>/llm-settings</c> form (which shows only the fields the chosen provider needs and marks each of
/// them required), by <see cref="TechieRagConfigService"/> (which refuses to persist an unbuildable
/// configuration) and by <see cref="TechieRagManager"/> (which refuses to hand an unbuildable
/// configuration to the builder, so a legacy bad file on disk cannot take down unrelated pages).</para>
/// <para><b>Field matrix:</b></para>
/// <list type="bullet">
/// <item>Ollama / LM Studio — base URL and model; NO API key.</item>
/// <item>OpenAI-compatible — base URL, API key and model.</item>
/// <item>Azure AI Foundry — endpoint, deployment name and API version, plus API key; NO model box,
/// because Azure addresses deployments rather than model names.</item>
/// <item>Google Gemini / Anthropic — API key and model; NO base URL.</item>
/// <item>None — nothing; direct chat is disabled.</item>
/// </list>
/// </remarks>
public static class LlmConfigValidator
{
    /// <summary>Field key for the endpoint / base URL input.</summary>
    public const string EndpointField = "Endpoint";

    /// <summary>Field key for the API key input.</summary>
    public const string ApiKeyField = "ApiKey";

    /// <summary>Field key for the model (or, on Azure, the deployment name) input.</summary>
    public const string ModelField = "Model";

    /// <summary>Field key for the Azure API version input.</summary>
    public const string ApiVersionField = "ApiVersion";

    /// <summary>
    /// Gets whether the provider needs an endpoint / base URL.
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>True when the endpoint field must be shown and filled.</returns>
    public static bool RequiresEndpoint(LlmSource source) => source is
        LlmSource.Ollama or LlmSource.LmStudio or LlmSource.OpenAICompatible or LlmSource.AzureAIFoundry;

    /// <summary>
    /// Gets whether the provider needs an API key.
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>True when the API key field must be shown and filled.</returns>
    public static bool RequiresApiKey(LlmSource source) => source is
        LlmSource.OpenAICompatible or LlmSource.AzureAIFoundry or LlmSource.GoogleGemini or LlmSource.Anthropic;

    /// <summary>
    /// Gets whether the provider addresses a deployment name instead of a model name.
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>True for Azure AI Foundry, which has a deployment box and no model box.</returns>
    public static bool UsesDeploymentName(LlmSource source) => source is LlmSource.AzureAIFoundry;

    /// <summary>
    /// Gets whether the provider needs an API version.
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>True for Azure AI Foundry.</returns>
    public static bool RequiresApiVersion(LlmSource source) => source is LlmSource.AzureAIFoundry;

    /// <summary>
    /// Gets whether the provider needs a model or deployment name at all.
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>True for every provider other than <see cref="LlmSource.None"/>.</returns>
    public static bool RequiresModel(LlmSource source) => source is not LlmSource.None;

    /// <summary>
    /// Gets the canonical endpoint for a provider that has one, or an empty string (REQ-FN-052).
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>The well-known base URL for the provider, or an empty string when there is none.</returns>
    /// <remarks>
    /// <para><b>Why this exists.</b> The LLM Settings form showed <c>http://localhost:11434</c> in the
    /// Ollama base-URL box as a <i>placeholder</i> — grey text that reads as a filled-in value while
    /// the bound field is still empty. Selecting Ollama and pressing <b>Save &amp; apply</b> was
    /// therefore refused with "Base URL is required for the Ollama provider" (recorded verbatim in the
    /// application log at 2026-07-30 23:06:11), so the provider never reached
    /// <c>techierag-config.json</c> — which is the REQ-FN-052 defect as the operator experienced it.
    /// </para>
    /// <para>Only the two local servers have a canonical address worth materialising. A remote
    /// provider's endpoint is account-specific and is deliberately left blank rather than guessed;
    /// Azure AI Foundry in particular has no default at all.</para>
    /// </remarks>
    public static string DefaultEndpoint(LlmSource source) => source switch
    {
        LlmSource.Ollama => "http://localhost:11434",
        LlmSource.LmStudio => "http://localhost:1234",
        LlmSource.OpenAICompatible => "https://api.openai.com",
        LlmSource.AzureAIFoundry => "https://your-resource.openai.azure.com",
        _ => string.Empty
    };

    /// <summary>
    /// Gets whether a provider's <see cref="DefaultEndpoint"/> is safe to fill in for the operator
    /// (REQ-FN-052).
    /// </summary>
    /// <param name="source">The selected provider.</param>
    /// <returns>True when the default is a real, usable address rather than an illustration.</returns>
    /// <remarks>
    /// True only for the local servers, whose default IS the address the operator would type. The
    /// OpenAI-compatible and Azure defaults are examples shown in the placeholder and must never be
    /// written into the configuration — a fabricated endpoint that then fails to connect is worse than
    /// an empty required field.
    /// </remarks>
    public static bool HasUsableDefaultEndpoint(LlmSource source) => source is
        LlmSource.Ollama or LlmSource.LmStudio;

    /// <summary>
    /// Validates a provider configuration against the field matrix for its selected source.
    /// </summary>
    /// <param name="config">The provider configuration to validate; null yields no errors.</param>
    /// <returns>Every failure, each named against the field that caused it. Empty when valid.</returns>
    /// <remarks>
    /// A configuration whose source is <see cref="LlmSource.None"/> is always valid — no provider means
    /// nothing to misconfigure.
    /// </remarks>
    public static IReadOnlyList<LlmValidationError> Validate(LlmConfig? config)
    {
        var errors = new List<LlmValidationError>();
        if (config is null || config.Source == LlmSource.None)
        {
            return errors;
        }

        var source = config.Source;
        var providerName = DescribeSource(source);

        if (RequiresEndpoint(source) && string.IsNullOrWhiteSpace(config.Endpoint))
        {
            // Azure addresses an "endpoint"; everything else has a "base URL". The validator knows
            // which, so it names the key — the page no longer has to re-derive the split.
            var key = source == LlmSource.AzureAIFoundry
                ? "LlmValidationEndpointRequired"
                : "LlmValidationBaseUrlRequired";

            // Endpoint is the field whose absence makes the builder throw for the two remote
            // providers that construct from it — this is the regression REQ-UI-043 exists to close.
            var blocks = source is LlmSource.OpenAICompatible or LlmSource.AzureAIFoundry;
            errors.Add(new LlmValidationError(EndpointField, key, providerName, blocks));
        }

        if (RequiresApiKey(source) && string.IsNullOrWhiteSpace(config.ApiKey))
        {
            // Azure / Gemini / Anthropic throw at build time without a key; an OpenAI-compatible
            // provider builds with an empty key (many local gateways accept anything) but is still
            // an incomplete configuration the form must refuse.
            var blocks = source is LlmSource.AzureAIFoundry or LlmSource.GoogleGemini or LlmSource.Anthropic;
            errors.Add(new LlmValidationError(
                ApiKeyField, "LlmValidationApiKeyRequired", providerName, blocks));
        }

        if (RequiresModel(source) && string.IsNullOrWhiteSpace(config.Model))
        {
            var key = UsesDeploymentName(source)
                ? "LlmValidationDeploymentRequired"
                : "LlmValidationModelRequired";
            errors.Add(new LlmValidationError(ModelField, key, providerName, false));
        }

        if (RequiresApiVersion(source) && string.IsNullOrWhiteSpace(config.ApiVersion))
        {
            errors.Add(new LlmValidationError(
                ApiVersionField, "LlmValidationApiVersionRequired", providerName, false));
        }

        return errors;
    }

    /// <summary>
    /// Validates the primary and fallback providers of a whole TechieRag configuration.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <param name="buildBlockingOnly">
    /// True to report only the failures that would make <c>TechieRagBuilder.Build()</c> throw.
    /// </param>
    /// <returns>Every failure found across both providers. Empty when valid.</returns>
    public static IReadOnlyList<LlmValidationError> ValidateConfig(TechieRagConfig? config, bool buildBlockingOnly)
    {
        if (config is null)
        {
            return [];
        }

        var errors = new List<LlmValidationError>();
        errors.AddRange(Validate(config.Llm));
        errors.AddRange(Validate(config.LlmFallback));

        return buildBlockingOnly
            ? errors.Where(e => e.BlocksInstanceBuild).ToList()
            : errors;
    }

    /// <summary>
    /// Gets whether a provider configuration can be handed to the TechieRag builder without throwing.
    /// </summary>
    /// <param name="config">The provider configuration to test.</param>
    /// <returns>True when the builder can construct this provider.</returns>
    public static bool IsBuildable(LlmConfig? config)
        => !Validate(config).Any(e => e.BlocksInstanceBuild);

    /// <summary>
    /// Gets the display name of a provider, used in validation messages.
    /// </summary>
    /// <param name="source">The provider source.</param>
    /// <returns>The human-readable provider name.</returns>
    /// <remarks>
    /// REQ-UI-051: deliberately NOT keyed. Every value but <c>None</c> is a brand name rendered as
    /// itself in Latin script, which is the same rule the razor counters already apply to
    /// "LM Studio", "Azure AI Foundry" and "Google Gemini". Only <c>None</c> is a word, and it is
    /// never shown by this method — the LLM Settings picker localizes that one option itself.
    /// </remarks>
    public static string DescribeSource(LlmSource source) => source switch
    {
        LlmSource.Ollama => "Ollama",
        LlmSource.LmStudio => "LM Studio",
        LlmSource.OpenAICompatible => "OpenAI-compatible",
        LlmSource.AzureAIFoundry => "Azure AI Foundry",
        LlmSource.GoogleGemini => "Google Gemini",
        LlmSource.Anthropic => "Anthropic",
        _ => "None"
    };
}
