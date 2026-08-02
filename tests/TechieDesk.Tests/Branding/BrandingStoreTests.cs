using System.Text;
using TechieDesk.Services.Branding;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Branding;

/// <summary>
/// REQ-UI-037 (BRD-89): white-label branding — what is stored, what is refused, and what a corrupt
/// row is allowed to do to an image source.
/// </summary>
public sealed class BrandingStoreTests
{
    /// <summary>With nothing stored, the shipped product identity applies.</summary>
    [Fact]
    public async Task FallsBackToTheProductIdentity()
    {
        var store = new BrandingStore(new FakeInstanceSettings());

        var branding = await store.LoadAsync();

        Assert.Equal("TechieDesk", branding.ProductName);
        Assert.Equal(BrandingSettings.DefaultWelcomeMessage, branding.WelcomeMessage);
        Assert.Equal(BrandingSettings.DefaultFooterLinks, branding.FooterLinks);
        Assert.Null(branding.LogoDataUri);
    }

    /// <summary>Every branded field round-trips.</summary>
    [Fact]
    public async Task RoundTripsEveryField()
    {
        var settings = new FakeInstanceSettings();
        var store = new BrandingStore(settings);
        var logo = EncodePng([1, 2, 3, 4]);

        await store.SaveAsync(new BrandingSettings("Acme Docs", "Ask us anything.", "Help | Legal", logo));
        var reloaded = await store.LoadAsync();

        Assert.Equal("Acme Docs", reloaded.ProductName);
        Assert.Equal("Ask us anything.", reloaded.WelcomeMessage);
        Assert.Equal("Help | Legal", reloaded.FooterLinks);
        Assert.Equal(logo, reloaded.LogoDataUri);
    }

    /// <summary>
    /// A blank product name falls back rather than being stored. The name is the shell's lockup, and
    /// an install whose window says nothing at all is worse than one that says TechieDesk.
    /// </summary>
    [Fact]
    public async Task RefusesToStoreABlankProductName()
    {
        var settings = new FakeInstanceSettings();
        var store = new BrandingStore(settings);

        await store.SaveAsync(new BrandingSettings("   ", "hello", "Docs", null));
        var reloaded = await store.LoadAsync();

        Assert.Equal("TechieDesk", reloaded.ProductName);
    }

    /// <summary>Reading does not freeze the defaults into the database.</summary>
    [Fact]
    public async Task ReadingDoesNotPersistTheDefaults()
    {
        var settings = new FakeInstanceSettings();
        var store = new BrandingStore(settings);

        await store.LoadAsync();

        Assert.Empty(settings.Written);
    }

    /// <summary>
    /// A logo that is not an allowed data URI is refused at the store, not only at the upload
    /// control. The store is the last thing between a caller and a persisted image source.
    /// </summary>
    [Fact]
    public async Task RefusesALogoThatIsNotAnAllowedDataUri()
    {
        var store = new BrandingStore(new FakeInstanceSettings());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(new BrandingSettings("Acme", "hi", "Docs", "https://acme.test/logo.png")));
    }

    /// <summary>
    /// A row containing something other than an allowed data URI is dropped ON READ. The setting
    /// table is plain text and its value ends up in an &lt;img src&gt;, so the check has to be at
    /// the point of use as well as at the point of write.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://acme.test/logo.png")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public async Task DropsAnUnsafeStoredLogo(string stored)
    {
        var settings = new FakeInstanceSettings();
        settings.Seed(BrandingStore.LogoKey, stored);
        var store = new BrandingStore(settings);

        var branding = await store.LoadAsync();

        Assert.Null(branding.LogoDataUri);
    }

    /// <summary>Footer links split on the pipe separator, trimmed, with blanks dropped.</summary>
    [Fact]
    public void SplitsTheFooterLinks()
    {
        var branding = BrandingSettings.Defaults with { FooterLinks = " Docs | Privacy |  | Terms " };

        Assert.Equal(["Docs", "Privacy", "Terms"], branding.FooterLinkLabels);
    }

    /// <summary>
    /// A fresh install is not "customised", and a single edited field makes it so.
    /// </summary>
    /// <remarks>
    /// <c>IsCustomised</c> is what tells an unbranded install from one deliberately branded BACK to
    /// the shipped words, which is the difference between the shell drawing the built-in lockup and
    /// drawing an operator's identical-looking one. Restoring defaults in the Branding panel must
    /// therefore land back on false, not on "customised with the default text".
    /// </remarks>
    [Fact]
    public void ReportsWhetherAnythingHasBeenBranded()
    {
        Assert.False(BrandingSettings.Defaults.IsCustomised);
        Assert.True((BrandingSettings.Defaults with { ProductName = "Acme Docs" }).IsCustomised);
        Assert.True((BrandingSettings.Defaults with { WelcomeMessage = "Hello." }).IsCustomised);
        Assert.True((BrandingSettings.Defaults with { FooterLinks = "Help" }).IsCustomised);
        Assert.True((BrandingSettings.Defaults with { LogoDataUri = EncodePng([1]) }).IsCustomised);

        // The panel's "Restore defaults" button rebuilds the record field by field rather than
        // reusing the static, so value equality — not reference equality — has to carry this.
        var restored = new BrandingSettings(
            BrandingSettings.DefaultProductName,
            BrandingSettings.DefaultWelcomeMessage,
            BrandingSettings.DefaultFooterLinks,
            null);

        Assert.False(restored.IsCustomised);
    }

    /// <summary>
    /// The shipped footer links parse into the labels the Branding preview draws. The default is a
    /// pipe-separated string in a resource-free constant, so a stray separator would ship a preview
    /// with an empty chip in it.
    /// </summary>
    [Fact]
    public void ShipsFooterLinksThatParse()
    {
        Assert.Equal(["Docs", "Privacy"], BrandingSettings.Defaults.FooterLinkLabels);
        Assert.All(
            BrandingSettings.Defaults.FooterLinkLabels,
            label => Assert.False(string.IsNullOrWhiteSpace(label)));
    }

    /// <summary>An SVG and a PNG are both accepted, and the stored type comes from the extension.</summary>
    [Theory]
    [InlineData("mark.svg", "image/svg+xml")]
    [InlineData("mark.SVG", "image/svg+xml")]
    [InlineData("mark.png", "image/png")]
    public void AcceptsTheAllowedImageTypes(string fileName, string expectedType)
    {
        var accepted = BrandingLogo.TryEncode(
            fileName, contentType: null, Encoding.UTF8.GetBytes("<svg/>"), out var dataUri, out var error);

        Assert.True(accepted);
        Assert.Null(error);
        Assert.StartsWith($"data:{expectedType};base64,", dataUri);
    }

    /// <summary>
    /// The extension decides the type, not the browser's claim. A WebView reports an empty or wrong
    /// content type often enough that trusting it would reject valid logos — and trusting it the
    /// other way would let arbitrary bytes be labelled an image.
    /// </summary>
    [Fact]
    public void IgnoresTheBrowserReportedContentType()
    {
        var accepted = BrandingLogo.TryEncode(
            "mark.png", contentType: "application/x-msdownload", [0x89, 0x50], out var dataUri, out _);

        Assert.True(accepted);
        Assert.StartsWith("data:image/png;base64,", dataUri);
    }

    /// <summary>A disallowed extension is refused with a message, not an exception.</summary>
    [Fact]
    public void RefusesADisallowedExtension()
    {
        var accepted = BrandingLogo.TryEncode(
            "logo.exe", "image/png", [1, 2, 3], out var dataUri, out var error);

        Assert.False(accepted);
        Assert.Null(dataUri);
        Assert.NotNull(error);
    }

    /// <summary>An oversized logo is refused. This is a settings row read on every launch.</summary>
    [Fact]
    public void RefusesAnOversizedLogo()
    {
        var oversized = new byte[BrandingLogo.MaxBytes + 1];

        var accepted = BrandingLogo.TryEncode("mark.png", "image/png", oversized, out _, out var error);

        Assert.False(accepted);
        Assert.NotNull(error);
    }

    /// <summary>An empty file is refused rather than stored as a zero-byte image.</summary>
    [Fact]
    public void RefusesAnEmptyFile()
    {
        var accepted = BrandingLogo.TryEncode("mark.png", "image/png", [], out _, out var error);

        Assert.False(accepted);
        Assert.NotNull(error);
    }

    private static string EncodePng(byte[] content)
    {
        Assert.True(BrandingLogo.TryEncode("mark.png", "image/png", content, out var dataUri, out _));
        return dataUri!;
    }
}
