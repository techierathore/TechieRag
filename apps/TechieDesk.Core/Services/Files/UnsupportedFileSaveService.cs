namespace TechieDesk.Services.Files;

/// <summary>
/// The fallback <see cref="IFileSaveService"/> for a head that has not registered a native save
/// panel — it saves nothing and says so (REQ-FN-010).
/// </summary>
/// <remarks>
/// This is the deliberate opposite of the defect it replaces. The WebView blob download also wrote
/// no file, but reported success; this reports <see cref="FileSaveStatus.Failed"/> with a reason the
/// user can act on. The Windows head currently has no <c>Platforms/Windows</c> sources (REQ-FN-035)
/// and therefore resolves this implementation; when a Windows save service ships it is registered
/// before <c>AddTechieDeskFileSave</c> and wins via <c>TryAdd</c>.
/// </remarks>
public sealed class UnsupportedFileSaveService : IFileSaveService
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public Task<FileSaveResult> SaveTextAsync(
        string suggestedFileName,
        string contentType,
        string content,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FileSaveResult.Failed(
            "Saving files is not available on this platform yet, so nothing was written."));
}
