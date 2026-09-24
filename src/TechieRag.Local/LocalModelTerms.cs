namespace TechieRag.Local;

/// <summary>
/// The terms a user accepts before a local model downloads, handed to the host so it can show them
/// in a dialog (REQ-RAG-062 / BRD-101).
/// </summary>
/// <param name="ModelId">The model's id.</param>
/// <param name="DisplayName">The model's name for a user.</param>
/// <param name="LicenceName">The licence the weights are published under, for example <c>Apache-2.0</c>.</param>
/// <param name="TermsUrl">Where the licence text is; null for a host-supplied model.</param>
public sealed record LocalModelTerms(string ModelId, string DisplayName, string LicenceName, Uri? TermsUrl)
{
    /// <summary>
    /// Gets the bytes the download will transfer, known before the first byte; 0 until the provider
    /// has worked out which file set its platform loads.
    /// </summary>
    public long DownloadBytes { get; init; }
}
