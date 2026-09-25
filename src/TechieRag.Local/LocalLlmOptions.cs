using Microsoft.Extensions.Logging;

namespace TechieRag.Local;

/// <summary>
/// Settings of the in-process local model (REQ-RAG-057 / BRD-96).
/// </summary>
public sealed class LocalLlmOptions
{
    /// <summary>Gets or sets the logger factory the provider logs loads through; null logs nothing.</summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>Gets or sets the model; null selects <see cref="LocalModel.PlatformDefault"/>.</summary>
    public LocalModel? Model { get; set; }

    /// <summary>
    /// Gets or sets the context size in tokens; null uses 2,048 on a phone and 4,096 elsewhere,
    /// never more than the model supports. A larger context takes more memory.
    /// </summary>
    public int? ContextSize { get; set; }

    /// <summary>Gets or sets the CPU threads inference uses; null lets the runtime decide.</summary>
    public int? Threads { get; set; }

    /// <summary>Gets or sets the answer length used when a call sets no <c>MaxTokens</c>.</summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>Gets or sets the sampling temperature used when a call sets none.</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Gets or sets the nucleus sampling value used when a call sets none.</summary>
    public float TopP { get; set; } = 0.95f;

    /// <summary>
    /// Gets or sets whether the user has already accepted the model's terms (the host showed
    /// <see cref="LocalModel.GetTerms"/> earlier). When false and <see cref="ConfirmTermsAsync"/> is not
    /// set, a download is refused with <see cref="LocalModelTermsNotAcceptedException"/> (REQ-RAG-062).
    /// </summary>
    public bool TermsAccepted { get; set; }

    /// <summary>
    /// Gets or sets the callback that shows the model's terms to the user at the moment a download is
    /// needed and returns whether they accepted. Called at most once per download, before any request.
    /// </summary>
    public Func<LocalModelTerms, CancellationToken, Task<bool>>? ConfirmTermsAsync { get; set; }
}
