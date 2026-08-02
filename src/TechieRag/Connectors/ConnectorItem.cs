namespace TechieRag.Connectors;

/// <summary>
/// One item a connector found in a remote source, described cheaply (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Listing is separate from fetching, deliberately.</b> Every source this framework targets
/// charges very differently for "what is there" and "give me the contents": a repository tree is one
/// request for thousands of files while each blob is another, IMAP <c>SEARCH</c> returns identifiers
/// while each message body is a separate <c>FETCH</c>. Listing an item must therefore be possible
/// without paying to download it, because incremental sync's whole point is to decide — from the
/// listing alone — that an item does not need downloading at all.</para>
/// <para><b><see cref="Version"/> is the load-bearing field.</b> It is whatever the source uses to
/// say "this content changed": a blob SHA, a page version number, a message UID. Timestamps are not
/// enough — a file touched by a rebase has a new timestamp and identical content, and a page edited
/// twice in one second has one timestamp and two versions.</para>
/// </remarks>
/// <param name="Id">Stable identifier within the source. Survives across runs; drives incremental sync.</param>
/// <param name="Name">Human-facing name — a path, a page title, a subject line.</param>
/// <param name="SourceUrl">Where a citation should point. Empty when the source has no addressable URL.</param>
/// <param name="Version">Content version: SHA, version number, UID. Null means "cannot tell" and forces a re-fetch.</param>
/// <param name="ModifiedUtc">Last modification time, when the source reports one.</param>
/// <param name="SizeBytes">Size in bytes, when the source reports one. Lets a run skip oversized items before downloading them.</param>
/// <param name="Metadata">Source-specific extras carried through to the ingested document's metadata.</param>
public sealed record ConnectorItem(
    string Id,
    string Name,
    string SourceUrl,
    string? Version = null,
    DateTimeOffset? ModifiedUtc = null,
    long? SizeBytes = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
