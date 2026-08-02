namespace TechieDesk.Services.Branding;

/// <summary>
/// White-label branding for this install (REQ-UI-037 / BRD-89), gated on the <c>WHITE_LABEL</c>
/// feature code.
/// </summary>
/// <param name="ProductName">The name shown in place of "TechieDesk".</param>
/// <param name="WelcomeMessage">The greeting shown on an empty chat thread.</param>
/// <param name="FooterLinks">
/// Pipe-separated footer link labels, exactly as the Branding tab of
/// docs/mockups/admin-settings.html enters them (<c>Docs | Privacy</c>).
/// </param>
/// <param name="LogoDataUri">
/// The uploaded logo as a <c>data:</c> URI, or null to use the built-in lockup. See
/// <c>BrandingLogo</c> for why the image is stored inline rather than as a file path.
/// </param>
public sealed record BrandingSettings(
    string ProductName,
    string WelcomeMessage,
    string FooterLinks,
    string? LogoDataUri)
{
    /// <summary>The product name used when nothing has been branded.</summary>
    public const string DefaultProductName = "TechieDesk";

    /// <summary>The welcome message from docs/mockups/admin-settings.html.</summary>
    /// <remarks>
    /// <b>Deliberately invariant English, REQ-UI-055 / BRD-91.</b> This is not UI chrome: it is the
    /// shipped default branding CONTENT. It is the value <see cref="Defaults"/> carries, so
    /// <see cref="IsCustomised"/> compares against it byte-for-byte, and it is what
    /// <c>BrandingStore</c> falls back to and what "Restore defaults" writes into the settings table.
    /// A value that moved with the reader's language would make <see cref="Defaults"/> — a static
    /// property, resolved once at type initialization — freeze at whatever culture happened to touch
    /// it first, and would silently rewrite a stored greeting when the operator changed language.
    /// The reader-facing half is <see cref="DefaultWelcomeMessageKey"/>.
    /// </remarks>
    public const string DefaultWelcomeMessage = "Welcome! Ask anything about your documents.";

    /// <summary>
    /// Resource key for the shipped welcome message, for surfaces that only DISPLAY it.
    /// </summary>
    /// <remarks>
    /// The Branding tab's textarea placeholder is the case this exists for: a placeholder is a hint
    /// that is never persisted, so it can and must be read in the operator's own language, while
    /// <see cref="DefaultWelcomeMessage"/> stays the one invariant value that is stored and compared.
    /// </remarks>
    public const string DefaultWelcomeMessageKey = "BrandingDefaultWelcomeMessage";

    /// <summary>The footer links from docs/mockups/admin-settings.html.</summary>
    public const string DefaultFooterLinks = "Docs | Privacy";

    /// <summary>Gets the branding applied to an install that has never been white-labelled.</summary>
    public static BrandingSettings Defaults { get; } = new(
        DefaultProductName, DefaultWelcomeMessage, DefaultFooterLinks, null);

    /// <summary>
    /// Gets a value indicating whether anything has actually been customised. Used to tell an
    /// unbranded install from one deliberately branded back to the shipped defaults.
    /// </summary>
    public bool IsCustomised => this != Defaults;

    /// <summary>
    /// Gets the footer link labels, split on the pipe separator and trimmed, with blanks dropped.
    /// </summary>
    /// <returns>The individual labels in the order they were entered.</returns>
    public IReadOnlyList<string> FooterLinkLabels =>
        (FooterLinks ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
