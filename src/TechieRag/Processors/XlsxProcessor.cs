using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for Microsoft Excel XLSX workbooks using OpenXml.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Extracts cell text from every worksheet in a workbook and splits it
/// into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Opens the workbook from a seekable stream using the OpenXml SDK
/// 2) Walks each sheet in workbook order, emitting the sheet name as a heading
/// 3) Emits one line per row with cells joined by a tab separator
/// 4) Uses TextChunker to split the flattened text into sized chunks
/// </para>
/// <para><b>Dependencies:</b> Requires the DocumentFormat.OpenXml NuGet package (shared with
/// <see cref="DocxProcessor"/> and <see cref="PptxProcessor"/>).</para>
/// <para><b>Limitations:</b> Extracts displayed cell text only; formulas are read as their
/// stored value, and charts, images and pivot caches are ignored.</para>
/// </remarks>
public class XlsxProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".xlsx".</value>
    public IReadOnlyList<string> SupportedExtensions => [".xlsx"];

    /// <summary>
    /// Processes an Excel workbook stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The XLSX workbook content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the workbook.</returns>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new XlsxProcessor();
    /// using var stream = File.OpenRead("book.xlsx");
    /// var chunks = await processor.ProcessAsync(stream, "book.xlsx");
    /// </code>
    /// </example>
    public Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        options ??= new DocumentProcessingOptions();
        var chunks = new List<TextChunk>();

        // OpenXml requires a seekable stream
        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = SpreadsheetDocument.Open(memoryStream, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullText = ExtractWorkbookText(workbookPart, cancellationToken);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        BuildChunks(chunks, fullText, fileName, options, cancellationToken);
        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    /// <summary>
    /// Flattens every worksheet in the workbook into readable text.
    /// </summary>
    /// <param name="workbookPart">The workbook part to read.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Sheet-by-sheet text with the sheet name as a heading.</returns>
    private static string ExtractWorkbookText(WorkbookPart workbookPart, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var sharedStrings = LoadSharedStrings(workbookPart);
        var sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>() ?? [];

        foreach (var sheet in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sheet.Id?.Value is not string relationshipId)
            {
                continue;
            }

            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            AppendSheet(builder, sheet.Name?.Value ?? "Sheet", worksheetPart, sharedStrings);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends a single worksheet's heading and row text to the builder.
    /// </summary>
    /// <param name="builder">The destination text builder.</param>
    /// <param name="sheetName">The worksheet display name.</param>
    /// <param name="worksheetPart">The worksheet part holding the rows.</param>
    /// <param name="sharedStrings">The workbook shared string table.</param>
    private static void AppendSheet(
        StringBuilder builder,
        string sheetName,
        WorksheetPart worksheetPart,
        IReadOnlyList<string> sharedStrings)
    {
        var rowLines = new List<string>();

        var rows = worksheetPart.Worksheet?.Descendants<Row>() ?? [];

        foreach (var row in rows)
        {
            var cells = row.Elements<Cell>()
                .Select(cell => ResolveCellText(cell, sharedStrings))
                .Where(text => !string.IsNullOrWhiteSpace(text));

            var line = string.Join('\t', cells);
            if (!string.IsNullOrWhiteSpace(line))
            {
                rowLines.Add(line);
            }
        }

        if (rowLines.Count == 0)
        {
            return;
        }

        builder.AppendLine($"# Sheet: {sheetName}");
        foreach (var line in rowLines)
        {
            builder.AppendLine(line);
        }
        builder.AppendLine();
    }

    /// <summary>
    /// Resolves a cell's displayed text, dereferencing the shared string table when needed.
    /// </summary>
    /// <param name="cell">The cell to read.</param>
    /// <param name="sharedStrings">The workbook shared string table.</param>
    /// <returns>The cell text, or an empty string when the cell is blank.</returns>
    private static string ResolveCellText(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InnerText.Trim();
        }

        var raw = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(raw, out var index)
            && index >= 0
            && index < sharedStrings.Count)
        {
            return sharedStrings[index].Trim();
        }

        return raw.Trim();
    }

    /// <summary>
    /// Loads the workbook shared string table used by string-typed cells.
    /// </summary>
    /// <param name="workbookPart">The workbook part to read.</param>
    /// <returns>Shared strings in table order; empty when the workbook has none.</returns>
    private static IReadOnlyList<string> LoadSharedStrings(WorkbookPart workbookPart)
    {
        var table = workbookPart.SharedStringTablePart?.SharedStringTable;
        if (table is null)
        {
            return [];
        }

        return table.Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToList();
    }

    /// <summary>
    /// Splits extracted text into chunks and appends them to the result list.
    /// </summary>
    /// <param name="chunks">The destination chunk list.</param>
    /// <param name="fullText">The extracted workbook text.</param>
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
    /// Creates the metadata dictionary attached to every chunk from this workbook.
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
            ["processorType"] = nameof(XlsxProcessor)
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
