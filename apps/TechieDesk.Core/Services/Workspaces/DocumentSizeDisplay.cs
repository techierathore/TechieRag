using TechieRag.Models;

namespace TechieDesk.Services.Workspaces;

/// <summary>
/// Renders the <b>Size</b> column of the workspace document library (REQ-UI-021, BRD-46).
/// </summary>
/// <remarks>
/// <para>Extracted from <c>DocumentLibrary.razor</c> so the column can be asserted against a
/// document that came back from a REAL vector store round trip. The razor page is in the MAUI head,
/// which a <c>net10.0</c> test project cannot reference, and the version of this logic that lived
/// there rendered an em-dash on every row for months without a single test noticing — the probe was
/// correct and nothing in the ingestion pipeline ever wrote the key it probed for.</para>
/// <para><b>REQ-UI-055 / BRD-91:</b> audited and found to build no English. <see cref="Unknown"/> is
/// an em dash, the metadata keys are wire spellings matched against what the ingestion pipeline
/// wrote, and <c>B</c>/<c>KB</c>/<c>MB</c>/<c>GB</c> are unit SYMBOLS, which Hindi UIs use as-is
/// exactly as they use the digits. The numbers themselves already format in the reader's culture,
/// because interpolation uses <c>CurrentCulture</c> and nothing here pins it to the invariant one.
/// </para>
/// <para>The probe deliberately accepts several spellings of the key. <see cref="DocumentMetadataKeys.FileSize"/>
/// is what the library writes now; the alternatives cost nothing and keep a document ingested by a
/// consumer that chose a different spelling readable.</para>
/// </remarks>
public static class DocumentSizeDisplay
{
    /// <summary>Shown when a document carries no usable byte size.</summary>
    /// <remarks>
    /// The correct rendering for every document ingested before the pipeline recorded a size, and
    /// for routes that genuinely have none. It is not an error state and must never be backfilled
    /// with a guess.
    /// </remarks>
    public const string Unknown = "—";

    /// <summary>Metadata keys probed for a byte size, in precedence order.</summary>
    private static readonly string[] SizeKeys =
        [DocumentMetadataKeys.FileSize, "Size", "fileSize", "size", "ByteSize"];

    /// <summary>
    /// Formats a document's recorded byte size for display.
    /// </summary>
    /// <param name="document">A document from the catalogue, or null when the row has no catalogue entry.</param>
    /// <returns>A human-readable size such as <c>1.4 KB</c>, or <see cref="Unknown"/>.</returns>
    /// <remarks>
    /// Total: a null document, absent metadata, a non-numeric value and a non-positive size all
    /// resolve to <see cref="Unknown"/> rather than throwing, because one unreadable row must not
    /// take the whole library table down.
    /// </remarks>
    public static string FromMetadata(Document? document)
    {
        if (document is null)
        {
            return Unknown;
        }

        foreach (var key in SizeKeys)
        {
            if (document.Metadata.TryGetValue(key, out var value)
                && TryToLong(value, out var bytes)
                && bytes > 0)
            {
                return Format(bytes);
            }
        }

        return Unknown;
    }

    /// <summary>
    /// Formats a byte count using binary multiples.
    /// </summary>
    /// <param name="bytes">A non-negative byte count.</param>
    /// <returns>The count rendered in B, KB, MB or GB.</returns>
    public static string Format(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (double)(1024 * 1024 * 1024):F1} GB",
        >= 1024 * 1024 => $"{bytes / (double)(1024 * 1024):F1} MB",
        >= 1024 => $"{bytes / (double)1024:F1} KB",
        _ => $"{bytes} B"
    };

    /// <summary>Converts a metadata value of unknown runtime type to a byte count.</summary>
    /// <param name="value">The stored value.</param>
    /// <param name="result">Receives the byte count, or zero when the value is not numeric.</param>
    /// <returns>True when the value converted; otherwise false.</returns>
    private static bool TryToLong(object? value, out long result)
    {
        if (value is null)
        {
            result = 0;
            return false;
        }

        try
        {
            result = Convert.ToInt64(value);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            result = 0;
            return false;
        }
    }
}
