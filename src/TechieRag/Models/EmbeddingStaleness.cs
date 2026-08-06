namespace TechieRag.Models;

/// <summary>
/// Why one document's vectors cannot be compared with the ones the current provider produces
/// (REQ-RAG-052).
/// </summary>
public enum EmbeddingStalenessReason
{
    /// <summary>The document carries a signature, and it is not the current one.</summary>
    /// <remarks>A different provider, a different model, or the same model encoded differently.</remarks>
    DifferentSignature,

    /// <summary>The document carries no signature at all.</summary>
    /// <remarks>
    /// It was ingested before stamping existed, so what produced it is unknowable. Treated as stale
    /// rather than as "probably fine": the corpus this was built for is exactly the unstamped one.
    /// </remarks>
    Unstamped
}

/// <summary>One document whose vectors were produced by something other than the current provider.</summary>
/// <param name="DocumentId">The document's id.</param>
/// <param name="Name">Its display name, so a report can name it without a second lookup.</param>
/// <param name="Signature">The signature it carries, or null when it carries none.</param>
/// <param name="Reason">Why it is stale.</param>
public sealed record StaleDocument(
    string DocumentId,
    string Name,
    string? Signature,
    EmbeddingStalenessReason Reason);

/// <summary>
/// What a corpus was embedded with, compared against what the current provider produces
/// (REQ-RAG-052).
/// </summary>
/// <param name="CurrentSignature">The signature the current provider stamps on new vectors.</param>
/// <param name="TotalDocuments">How many documents were examined.</param>
/// <param name="StaleDocuments">The ones that do not match, with the reason for each.</param>
/// <param name="IsDeterminable">
/// False when the current provider does not publish a signature, so nothing can be concluded.
/// </param>
/// <remarks>
/// <para><b>Read <see cref="IsDeterminable"/> before <see cref="IsStale"/>.</b> A provider that does
/// not publish a signature yields an empty stale list, and reporting that as "everything is fine"
/// would be the same class of lie this requirement exists to remove — a clean result that was never
/// actually checked.</para>
/// </remarks>
public sealed record EmbeddingStalenessReport(
    string CurrentSignature,
    int TotalDocuments,
    IReadOnlyList<StaleDocument> StaleDocuments,
    bool IsDeterminable)
{
    /// <summary>Gets whether any document needs re-ingesting.</summary>
    public bool IsStale => StaleDocuments.Count > 0;

    /// <summary>Gets how many documents need re-ingesting.</summary>
    public int StaleCount => StaleDocuments.Count;

    /// <summary>Gets whether EVERY examined document is stale.</summary>
    /// <remarks>
    /// The difference matters to whoever acts on this. All-stale is the ordinary case after an
    /// encoding change and calls for a full re-ingest; a mixed corpus means someone already
    /// re-ingested part of it and the store now holds vectors from two incomparable spaces, which is
    /// the worse state and the one nobody can see without this report.
    /// </remarks>
    public bool IsEntirelyStale => TotalDocuments > 0 && StaleDocuments.Count == TotalDocuments;

    /// <summary>Gets whether the store mixes current and stale vectors.</summary>
    public bool IsMixed => IsStale && !IsEntirelyStale;
}

/// <summary>
/// Decides which stored documents were embedded by something other than the current provider
/// (REQ-RAG-052 / TR-RAG-044).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Correcting the BGE-M3 tokenizer on 2026-08-04 changed every vector
/// the embedded provider produces. Vectors written before and after are in different spaces and the
/// cosine similarity between them is meaningless — but nothing in the product could TELL, so a
/// half-re-ingested store degraded silently with nothing in the logs. A stamp plus this comparison is
/// what turns that into something a person can be shown.</para>
/// <para><b>Pure, and separate from any store.</b> It reads <see cref="Document.Metadata"/>, which
/// every <c>IVectorStore</c> already returns from <c>ListDocumentsAsync</c>, so no store interface
/// changed and all three backends are covered by the same code.</para>
/// </remarks>
public static class EmbeddingStaleness
{
    /// <summary>The signature reported by a provider that does not publish one.</summary>
    public const string UnknownSignature = "unknown";

    /// <summary>
    /// Builds the signature for a provider whose encoding has not been revised.
    /// </summary>
    /// <param name="providerName">The provider's <see cref="Abstractions.IEmbeddingProvider.Name"/>.</param>
    /// <param name="modelName">The model it runs.</param>
    /// <param name="revision">The encoding revision; bump it when the SAME model starts producing different vectors.</param>
    /// <returns>The signature to stamp on new vectors.</returns>
    /// <remarks>
    /// <b>The revision is the part that earns this its keep.</b> Provider and model alone would not
    /// have caught what happened here: the provider and the model were unchanged, and only the
    /// tokenization was corrected. Any change that alters the vectors for identical input — encoding,
    /// pooling, normalisation, a different export of the same weights — must bump it.
    /// </remarks>
    public static string Signature(string providerName, string modelName, int revision = 1) =>
        $"{providerName}/{modelName}/r{revision}";

    /// <summary>
    /// Compares a corpus against the signature the current provider stamps.
    /// </summary>
    /// <param name="documents">The stored documents, from <c>ListDocumentsAsync</c>.</param>
    /// <param name="currentSignature">What the current provider stamps on new vectors.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documents"/> is null.</exception>
    public static EmbeddingStalenessReport Analyze(
        IReadOnlyList<Document> documents, string? currentSignature)
    {
        ArgumentNullException.ThrowIfNull(documents);

        // Nothing to compare against. Report that honestly rather than returning an empty stale list
        // a caller would read as a clean bill of health.
        if (string.IsNullOrWhiteSpace(currentSignature) || currentSignature == UnknownSignature)
        {
            return new EmbeddingStalenessReport(
                UnknownSignature, documents.Count, [], IsDeterminable: false);
        }

        var stale = new List<StaleDocument>();

        foreach (var document in documents)
        {
            var signature = SignatureOf(document);

            if (signature is null)
            {
                stale.Add(new StaleDocument(
                    document.Id, document.Name, null, EmbeddingStalenessReason.Unstamped));
            }
            else if (!string.Equals(signature, currentSignature, StringComparison.Ordinal))
            {
                stale.Add(new StaleDocument(
                    document.Id, document.Name, signature, EmbeddingStalenessReason.DifferentSignature));
            }
        }

        return new EmbeddingStalenessReport(
            currentSignature, documents.Count, stale, IsDeterminable: true);
    }

    /// <summary>Reads the signature a document was stamped with.</summary>
    /// <param name="document">The stored document.</param>
    /// <returns>The signature, or null when it carries none.</returns>
    /// <remarks>
    /// A blank stamp counts as no stamp: a key present with no value says nothing, and treating it as
    /// a match would let an empty string pass as current.
    /// </remarks>
    private static string? SignatureOf(Document document)
    {
        if (document.Metadata is null
            || !document.Metadata.TryGetValue(DocumentMetadataKeys.EmbeddingSignature, out var value))
        {
            return null;
        }

        var signature = value?.ToString();
        return string.IsNullOrWhiteSpace(signature) ? null : signature;
    }
}
