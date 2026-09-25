using Microsoft.Extensions.Logging;
using TechieRag.Llm;

namespace TechieRag.Local;

/// <summary>
/// Connects the local model to the core's configuration paths: <c>LlmSource.Local</c>,
/// <c>LlmProviderFactory</c> and the <c>local/&lt;model&gt;</c> route (REQ-RAG-064 / BRD-104).
/// </summary>
public static class LocalLlm
{
    /// <summary>
    /// Registers this package as the provider of <c>LlmSource.Local</c> for the process. Every
    /// <c>UseLocalLlm()</c> overload calls it; call it yourself when you configure the model through
    /// <c>IConfiguration</c>, <c>UseLlm(LlmSource.Local, …)</c> or <c>LlmProviderFactory.CreateForModel("local/…")</c>.
    /// </summary>
    /// <param name="configure">
    /// Defaults for every provider created through those paths, typically
    /// <see cref="LocalLlmOptions.ConfirmTermsAsync"/>; the model always comes from the route or configuration.
    /// </param>
    public static void Register(Action<LocalLlmOptions>? configure = null) =>
        LocalLlmProviderRegistry.Register((modelId, loggerFactory) => Create(modelId, loggerFactory, configure));

    /// <summary>
    /// Creates a local provider; nothing is downloaded or loaded until first use.
    /// </summary>
    /// <param name="modelId">A model id from <see cref="LocalModel.All"/>, or null for the platform default.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="configure">Optional further settings.</param>
    /// <returns>The provider.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelId"/> names no known model.</exception>
    /// <exception cref="NotSupportedException">The model is too large for Android or iOS.</exception>
    public static LocalLlmProvider Create(
        string? modelId = null,
        ILoggerFactory? loggerFactory = null,
        Action<LocalLlmOptions>? configure = null)
    {
        var options = new LocalLlmOptions();
        configure?.Invoke(options);
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            options.Model = RequireModel(modelId);
        }

        options.LoggerFactory ??= loggerFactory;
        return new LocalLlmProvider(options, options.LoggerFactory?.CreateLogger<LocalLlmProvider>());
    }

    /// <summary>
    /// Finds a model by id, throwing with the known ids when it is unknown.
    /// </summary>
    /// <param name="modelId">The id.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">No model has that id.</exception>
    internal static LocalModel RequireModel(string modelId) =>
        LocalModel.FromId(modelId)
        ?? throw new ArgumentException(
            $"Unknown local model '{modelId}'. Known models: {string.Join(", ", LocalModel.All.Select(m => m.Id))}. "
            + "For a model of your own, use UseLocalLlm(new DirectoryInfo(folder), template).",
            nameof(modelId));
}
