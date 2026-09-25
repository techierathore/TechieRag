using Microsoft.Extensions.Logging;

namespace TechieRag.Local;

/// <summary>
/// <c>UseLocalLlm()</c>: an in-process language model on <see cref="TechieRagBuilder"/>
/// (REQ-RAG-057 / BRD-96).
/// </summary>
public static class LocalLlmBuilderExtensions
{
    /// <summary>
    /// Uses this platform's default local model: Qwen2.5 0.5B Instruct on Android and iOS, Phi-3 mini
    /// on desktops. Downloaded once into the model root after the user accepts its terms.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Optional settings, for example <see cref="LocalLlmOptions.ConfirmTermsAsync"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <code>
    /// var rag = new TechieRagBuilder()
    ///     .UseEmbedded()
    ///     .UseSqliteVec()
    ///     .UseLocalLlm(o => o.ConfirmTermsAsync = (terms, ct) => ShowTermsDialogAsync(terms))
    ///     .Build();
    /// </code>
    /// </remarks>
    public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, Action<LocalLlmOptions>? configure = null) =>
        UseLocalLlm(builder, LocalModel.PlatformDefault, configure);

    /// <summary>
    /// Uses a local model by id (see <see cref="LocalModel.All"/>).
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="modelId">The model id, for example <c>qwen2.5-0.5b-instruct</c>.</param>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelId"/> names no known model.</exception>
    /// <exception cref="NotSupportedException">The model is too large for Android or iOS.</exception>
    public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, string modelId, Action<LocalLlmOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return UseLocalLlm(builder, LocalLlm.RequireModel(modelId), configure);
    }

    /// <summary>
    /// Uses a model the host has already placed in a folder; nothing is downloaded and no terms are asked.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="modelFolder">The folder of an ONNX Runtime GenAI model (<c>genai_config.json</c>, the ONNX files and the tokenizer).</param>
    /// <param name="chatTemplate">The chat format the model was trained on.</param>
    /// <param name="contextLength">The longest context the model supports, in tokens.</param>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The builder, for chaining.</returns>
    public static TechieRagBuilder UseLocalLlm(
        this TechieRagBuilder builder,
        DirectoryInfo modelFolder,
        LocalChatTemplate chatTemplate,
        int contextLength = 4_096,
        Action<LocalLlmOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelFolder);
        return UseLocalLlm(builder, LocalModel.FromFolder(modelFolder.FullName, chatTemplate, contextLength), configure);
    }

    /// <summary>
    /// Uses a local model.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="model">The model.</param>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="NotSupportedException">The model is too large for Android or iOS.</exception>
    public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, LocalModel model, Action<LocalLlmOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);
        model.EnsureSupported(LocalModel.IsPhonePlatform);

        var options = new LocalLlmOptions();
        configure?.Invoke(options);
        options.Model = model;

        // The config records the choice so a settings screen shows it (and LlmSource.Local routes here).
        var config = builder.GetConfig();
        config.Llm.Source = LlmSource.Local;
        config.Llm.Model = model.Id;
        config.Llm.Endpoint = null;
        config.Llm.ApiKey = null;

        LocalLlm.Register(configure);
        builder.UseCustomLlmProvider(() => new LocalLlmProvider(options, options.LoggerFactory?.CreateLogger<LocalLlmProvider>()));
        return builder;
    }
}
