using TechieRag;

namespace TechieDesk.Services.Settings;

/// <summary>
/// The four app-wide defaults surfaced by the operator App settings screen (REQ-UI-028, BRD-75).
/// </summary>
/// <remarks>
/// Three of these are the live TechieRag configuration — changing them changes what the running
/// application uses. The fourth, <see cref="MaxUploadSizeMb"/>, is app-owned and kept in the
/// <c>InstanceSetting</c> table. The type is a value snapshot on purpose: the screen compares the
/// snapshot it loaded against the one it is about to save to decide what actually changed.
/// </remarks>
/// <param name="LlmProvider">The configured LLM provider.</param>
/// <param name="LlmModel">The configured LLM model or deployment name.</param>
/// <param name="EmbeddingProvider">The configured embedding provider.</param>
/// <param name="VectorStore">The configured vector store.</param>
/// <param name="MaxUploadSizeMb">The app-wide document upload ceiling, in megabytes.</param>
public sealed record AppDefaults(
    LlmSource LlmProvider,
    string LlmModel,
    EmbeddingSource EmbeddingProvider,
    VectorStoreType VectorStore,
    int MaxUploadSizeMb);
