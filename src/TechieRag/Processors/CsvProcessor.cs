using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Header-aware document processor for delimited text files (CSV and TSV).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Turns tabular rows into self-describing sentences so that retrieval
/// hits carry their column names rather than bare, context-free values.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads the whole stream as text and splits it into records (quote-aware)
/// 2) Treats the first non-empty record as the header row
/// 3) Renders every data row as "Column: value | Column: value"
/// 4) Uses TextChunker to split the rendered text into sized chunks
/// </para>
/// <para><b>Dependencies:</b> None beyond the base class library.</para>
/// <para><b>Limitations:</b> Assumes a single header row; files without one are still processed,
/// with the first row's values acting as column labels.</para>
/// </remarks>
public class CsvProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".csv" and ".tsv".</value>
    public IReadOnlyList<string> SupportedExtensions => [".csv", ".tsv"];

    /// <summary>
    /// Processes a delimited text stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The CSV or TSV content stream.</param>
    /// <param name="fileName">The original file name (used for metadata and delimiter detection).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the delimited file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new CsvProcessor();
    /// using var stream = File.OpenRead("people.csv");
    /// var chunks = await processor.ProcessAsync(stream, "people.csv");
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        options ??= new DocumentProcessingOptions();
        var chunks = new List<TextChunk>();

        using var reader = new StreamReader(content);
        var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return chunks;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var delimiter = Path.GetExtension(fileName).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var fullText = RenderRecords(raw, delimiter);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return chunks;
        }

        BuildChunks(chunks, fullText, fileName, options, cancellationToken);
        return chunks;
    }

    /// <summary>
    /// Renders the delimited payload as header-labelled row text.
    /// </summary>
    /// <param name="raw">The full file text.</param>
    /// <param name="delimiter">The field delimiter in use.</param>
    /// <returns>One line per data row, prefixed by a columns summary.</returns>
    private static string RenderRecords(string raw, char delimiter)
    {
        var records = SplitRecords(raw, delimiter);
        if (records.Count == 0)
        {
            return string.Empty;
        }

        var header = records[0];
        var builder = new StringBuilder();
        builder.AppendLine($"Columns: {string.Join(", ", header)}");

        for (var i = 1; i < records.Count; i++)
        {
            var line = RenderRow(header, records[i]);
            if (!string.IsNullOrWhiteSpace(line))
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders a single data row as "Column: value" pairs.
    /// </summary>
    /// <param name="header">The header field names.</param>
    /// <param name="row">The data row fields.</param>
    /// <returns>The rendered row text, or an empty string when the row has no values.</returns>
    private static string RenderRow(IReadOnlyList<string> header, IReadOnlyList<string> row)
    {
        var parts = new List<string>(row.Count);

        for (var i = 0; i < row.Count; i++)
        {
            var value = row[i].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var column = i < header.Count && header[i].Trim().Length > 0 ? header[i].Trim() : $"Column{i + 1}";
            parts.Add($"{column}: {value}");
        }

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Splits delimited text into records, honouring quoted fields and embedded newlines.
    /// </summary>
    /// <param name="raw">The full file text.</param>
    /// <param name="delimiter">The field delimiter in use.</param>
    /// <returns>Records as lists of field values; blank records are dropped.</returns>
    private static List<List<string>> SplitRecords(string raw, char delimiter)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (inQuotes)
            {
                inQuotes = ConsumeQuoted(raw, ref i, field);
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                CloseRecord(records, fields, field);
                SkipCrLf(raw, ref i);
            }
            else
            {
                field.Append(c);
            }
        }

        CloseRecord(records, fields, field);
        return records;
    }

    /// <summary>
    /// Consumes one character inside a quoted field, handling the "" escape.
    /// </summary>
    /// <param name="raw">The full file text.</param>
    /// <param name="index">The current read position; advanced past an escape pair.</param>
    /// <param name="field">The field buffer being filled.</param>
    /// <returns>True while still inside the quoted field; false once the closing quote is seen.</returns>
    private static bool ConsumeQuoted(string raw, ref int index, StringBuilder field)
    {
        var c = raw[index];

        if (c != '"')
        {
            field.Append(c);
            return true;
        }

        if (index + 1 < raw.Length && raw[index + 1] == '"')
        {
            field.Append('"');
            index++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Advances the read position past a CRLF pair so it counts as one record break.
    /// </summary>
    /// <param name="raw">The full file text.</param>
    /// <param name="index">The current read position.</param>
    private static void SkipCrLf(string raw, ref int index)
    {
        if (raw[index] == '\r' && index + 1 < raw.Length && raw[index + 1] == '\n')
        {
            index++;
        }
    }

    /// <summary>
    /// Finalises the in-progress record and appends it when it carries any value.
    /// </summary>
    /// <param name="records">The accumulated records.</param>
    /// <param name="fields">The in-progress field list; cleared on return.</param>
    /// <param name="field">The in-progress field buffer; cleared on return.</param>
    private static void CloseRecord(List<List<string>> records, List<string> fields, StringBuilder field)
    {
        fields.Add(field.ToString());
        field.Clear();

        if (fields.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            records.Add([.. fields]);
        }

        fields.Clear();
    }

    /// <summary>
    /// Splits rendered text into chunks and appends them to the result list.
    /// </summary>
    /// <param name="chunks">The destination chunk list.</param>
    /// <param name="fullText">The rendered row text.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="options">The processing options in effect.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static void BuildChunks(
        List<TextChunk> chunks,
        string fullText,
        string fileName,
        DocumentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var documentId = Path.GetFileNameWithoutExtension(fileName);
        var textChunks = TextChunker.ChunkText(
            fullText,
            options.MaxChunkSize,
            options.ChunkOverlap,
            options.Chunker);

        var chunkIndex = 0;
        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            chunks.Add(new TextChunk
            {
                DocumentId = documentId,
                Text = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = CreateMetadata(fileName, options.Metadata)
            });
        }
    }

    /// <summary>
    /// Creates the metadata dictionary attached to every chunk from this file.
    /// </summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="additionalMetadata">Additional metadata from processing options.</param>
    /// <returns>Dictionary containing chunk metadata.</returns>
    private static Dictionary<string, object> CreateMetadata(
        string fileName,
        Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = fileName,
            ["processorType"] = nameof(CsvProcessor)
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }
}
