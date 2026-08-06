namespace TechieDesk.Services.Branding;

/// <summary>
/// Why an uploaded logo was refused, as a resource KEY plus the values that fill it
/// (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="MessageKey">A key in <c>AppStrings.resx</c>.</param>
/// <param name="Arguments">Format arguments for that key; empty when it carries no placeholder.</param>
/// <remarks>
/// The size arguments are handed over as NUMBERS rather than as pre-formatted text, so the
/// <c>{0:N0}</c> in the resource does the grouping in the reader's culture instead of this file
/// baking in the one it happened to run under.
/// </remarks>
public sealed record BrandingLogoError(string MessageKey, object[] Arguments);

/// <summary>
/// Validates and encodes an uploaded white-label logo (REQ-UI-037 / BRD-89).
/// </summary>
/// <remarks>
/// <para>
/// The logo is stored as a <c>data:</c> URI in the app database rather than as a file beside the
/// executable. Two reasons, both structural: the <c>.app</c> bundle is read-only (REQ-FN-037), and a
/// path into the per-user data directory is not loadable by the BlazorWebView without opening a
/// second file-serving surface into the user's filesystem. Inlining a small image keeps it in the
/// same row-based store as every other setting and travels with the database across an update.
/// </para>
/// <para>
/// SVG is accepted because the mockup's dropzone asks for it, and it is the format a brand actually
/// has. It is accepted ONLY as an <c>&lt;img src&gt;</c> payload: an SVG referenced that way is
/// rendered in a restricted mode where scripts do not run and external references are not fetched,
/// so it is not the injection path that inlining the same markup into the DOM would be. The size cap
/// exists because this is a row in a settings table read on every launch, not a blob store.
/// </para>
/// </remarks>
public static class BrandingLogo
{
    /// <summary>Maximum accepted logo size in bytes.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Resource key for an upload that is neither an SVG nor a PNG.</summary>
    public const string WrongTypeKey = "BrandingLogoErrorWrongType";

    /// <summary>Resource key for an upload with no bytes in it.</summary>
    public const string EmptyKey = "BrandingLogoErrorEmpty";

    /// <summary>Resource key for an upload over the cap. Takes the actual and the allowed KB.</summary>
    public const string TooLargeKey = "BrandingLogoErrorTooLarge";

    /// <summary>The scheme of the URI a logo is stored as. A wire token, never translated.</summary>
    private const string DataUriScheme = "data:";

    /// <summary>The encoding marker separating the media type from the payload. A wire token.</summary>
    private const string Base64Marker = ";base64,";

    /// <summary>MIME types accepted for an uploaded logo.</summary>
    public static IReadOnlyList<string> AllowedContentTypes { get; } =
    [
        "image/svg+xml",
        "image/png"
    ];

    /// <summary>File extensions accepted for an uploaded logo.</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } = [".svg", ".png"];

    /// <summary>
    /// Encodes uploaded bytes as a <c>data:</c> URI after checking the type and size.
    /// </summary>
    /// <param name="fileName">The uploaded file name, used to resolve the type.</param>
    /// <param name="contentType">The browser-reported content type; may be null or wrong.</param>
    /// <param name="content">The file bytes.</param>
    /// <param name="dataUri">The encoded URI when the upload is accepted.</param>
    /// <param name="error">
    /// A resource key and its arguments explaining the refusal when the upload is rejected
    /// (REQ-UI-055): the surface that shows the toast resolves it, because this file cannot see the
    /// reader's language and an English sentence built here would survive into a Hindi window.
    /// </param>
    /// <returns>True when the upload was accepted.</returns>
    /// <remarks>
    /// The extension decides the stored MIME type, not the browser-reported one. A WebView reports
    /// <c>content-type</c> from the OS type database, which on macOS returns an empty string for an
    /// SVG often enough that trusting it would reject valid logos — and trusting it in the other
    /// direction would let a caller label arbitrary bytes <c>image/png</c>. Deriving from a
    /// closed extension allowlist makes the stored type one of exactly two values.
    /// </remarks>
    public static bool TryEncode(
        string fileName,
        string? contentType,
        ReadOnlySpan<byte> content,
        out string? dataUri,
        out BrandingLogoError? error)
    {
        dataUri = null;
        error = null;

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var resolved = extension switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => null
        };

        if (resolved is null)
        {
            error = new BrandingLogoError(WrongTypeKey, []);
            return false;
        }

        if (content.Length == 0)
        {
            error = new BrandingLogoError(EmptyKey, []);
            return false;
        }

        if (content.Length > MaxBytes)
        {
            error = new BrandingLogoError(TooLargeKey, [content.Length / 1024d, MaxBytes / 1024]);
            return false;
        }

        dataUri = DataUriScheme + resolved + Base64Marker + Convert.ToBase64String(content);
        return true;
    }

    /// <summary>
    /// Checks that a stored value is a data URI of an allowed image type.
    /// </summary>
    /// <param name="dataUri">The candidate value; null is treated as "no logo" and accepted.</param>
    /// <returns>True when the value is safe to place in an image source.</returns>
    /// <remarks>
    /// Applied on the way OUT of the database as well as on the way in. The row is plain text that
    /// any future writer could set, and the value ends up in an <c>src</c> attribute — so a
    /// <c>javascript:</c> or remote <c>https:</c> URL must be refused at the point of use, not only
    /// at the point of upload.
    /// </remarks>
    public static bool IsAcceptable(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
        {
            return true;
        }

        foreach (var type in AllowedContentTypes)
        {
            if (dataUri.StartsWith(DataUriScheme + type + Base64Marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
