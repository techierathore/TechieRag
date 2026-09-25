namespace TechieRag.Llm;

/// <summary>
/// A vendor's stated terms for using a consumer subscription from a third-party app, as checked on a
/// date (REQ-RAG-070 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Facts, not rules.</b> The terms are recorded text a host can show the user before sign-in.
/// They are the vendor's words on <see cref="CheckedOn"/>, and vendors change them; the research behind
/// each row is in <c>DECISIONS.md</c> (2026-09-24, REQ-FN-062).</para>
/// <para><b>What <see cref="Permitted"/> changes:</b> only whether the library offers a sign-in for the
/// vendor. A vendor that is not permitted has a catalog row and no builder method, and the factory
/// refuses to create a provider for it.</para>
/// </remarks>
public sealed record SubscriptionTerms
{
    /// <summary>Gets whether the vendor permits a third-party app to use the subscription through its sign-in.</summary>
    public required bool Permitted { get; init; }

    /// <summary>Gets the vendor's terms as text, in the vendor's own words where it has them.</summary>
    public required string Terms { get; init; }

    /// <summary>Gets who the terms cover, e.g. "individual ChatGPT plans".</summary>
    public required string AppliesTo { get; init; }

    /// <summary>Gets the date the terms were last checked against the vendor's live documentation.</summary>
    public required DateOnly CheckedOn { get; init; }

    /// <summary>Gets the pages the terms were read from.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>Gets the builder method that signs in to this vendor, or null when there is none.</summary>
    public string? BuilderMethod { get; init; }
}
