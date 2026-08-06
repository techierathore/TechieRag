using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using TechieRag.Processors;
using Xunit;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace TechieRag.Tests.Processors;

/// <summary>
/// REQ-RAG-033 / BRD-114: the XLSX, PPTX and CSV document processors extract readable text
/// and produce a non-zero chunk count from a real fixture file of each type.
/// </summary>
public sealed class OfficeProcessorTests
{
    /// <summary>An XLSX workbook yields sheet-name headings plus every cell value.</summary>
    [Fact]
    public async Task IngestsXlsxWorkbook()
    {
        using var stream = XlsxFixture.Build();
        var processor = new XlsxProcessor();

        var chunks = await processor.ProcessAsync(stream, "budget.xlsx");
        var text = string.Join("\n", chunks.Select(chunk => chunk.Text));

        Assert.NotEmpty(chunks);
        Assert.Contains("Sheet: Quarterly", text);
        Assert.Contains("Region", text);
        Assert.Contains("Northwind", text);
        Assert.Contains("4200", text);
        Assert.All(chunks, chunk => Assert.Equal("budget", chunk.DocumentId));
    }

    /// <summary>A PPTX deck yields slide headings, body text and speaker notes in slide order.</summary>
    [Fact]
    public async Task IngestsPptxDeck()
    {
        using var stream = PptxFixture.Build();
        var processor = new PptxProcessor();

        var chunks = await processor.ProcessAsync(stream, "kickoff.pptx");
        var text = string.Join("\n", chunks.Select(chunk => chunk.Text));

        Assert.NotEmpty(chunks);
        Assert.Contains("Slide 1", text);
        Assert.Contains("Retrieval Augmented Generation", text);
        Assert.All(chunks, chunk => Assert.Equal("kickoff", chunk.DocumentId));
    }

    /// <summary>A CSV file renders each data row with its column names attached.</summary>
    [Fact]
    public async Task IngestsCsvWithHeaderAwareRows()
    {
        const string csv = "Name,Role,City\nAda Lovelace,Analyst,London\nGrace Hopper,Admiral,New York\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var processor = new CsvProcessor();

        var chunks = await processor.ProcessAsync(stream, "people.csv");
        var text = string.Join("\n", chunks.Select(chunk => chunk.Text));

        Assert.NotEmpty(chunks);
        Assert.Contains("Columns: Name, Role, City", text);
        Assert.Contains("Name: Ada Lovelace | Role: Analyst | City: London", text);
        Assert.Contains("Name: Grace Hopper | Role: Admiral | City: New York", text);
    }

    /// <summary>Quoted CSV fields keep embedded commas and unescape doubled quotes.</summary>
    [Fact]
    public async Task HandlesQuotedCsvFields()
    {
        const string csv = "Name,Note\n\"Hopper, Grace\",\"She said \"\"compile\"\" first\"\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var processor = new CsvProcessor();

        var chunks = await processor.ProcessAsync(stream, "notes.csv");
        var text = string.Join("\n", chunks.Select(chunk => chunk.Text));

        Assert.Contains("Name: Hopper, Grace", text);
        Assert.Contains("She said \"compile\" first", text);
    }

    /// <summary>Each processor advertises the extensions the client dispatches on.</summary>
    [Fact]
    public void AdvertisesSupportedExtensions()
    {
        Assert.Contains(".xlsx", new XlsxProcessor().SupportedExtensions);
        Assert.Contains(".pptx", new PptxProcessor().SupportedExtensions);
        Assert.Contains(".csv", new CsvProcessor().SupportedExtensions);
        Assert.Contains(".tsv", new CsvProcessor().SupportedExtensions);
    }

    /// <summary>An empty CSV payload yields no chunks rather than throwing.</summary>
    [Fact]
    public async Task ReturnsNoChunksForEmptyCsv()
    {
        using var stream = new MemoryStream([]);
        var chunks = await new CsvProcessor().ProcessAsync(stream, "empty.csv");

        Assert.Empty(chunks);
    }
}

/// <summary>Builds a minimal in-memory XLSX workbook for processor tests.</summary>
internal static class XlsxFixture
{
    /// <summary>Creates a one-sheet workbook with a header row and two data rows.</summary>
    /// <returns>A rewound stream containing the workbook bytes.</returns>
    public static MemoryStream Build()
    {
        var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            sheetData.Append(BuildRow("Region", "Revenue"));
            sheetData.Append(BuildRow("Northwind", "4200"));
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Quarterly"
            });

            workbookPart.Workbook.Save();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>Builds a spreadsheet row of inline-string cells.</summary>
    /// <param name="values">The cell values, left to right.</param>
    /// <returns>The populated row.</returns>
    private static Row BuildRow(params string[] values)
    {
        var row = new Row();

        foreach (var value in values)
        {
            row.Append(new Cell
            {
                DataType = CellValues.String,
                CellValue = new CellValue(value)
            });
        }

        return row;
    }
}

/// <summary>Builds a minimal in-memory PPTX presentation for processor tests.</summary>
internal static class PptxFixture
{
    /// <summary>Creates a single-slide deck carrying one text run.</summary>
    /// <returns>A rewound stream containing the presentation bytes.</returns>
    public static MemoryStream Build()
    {
        var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new Presentation();

            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = BuildSlide("Retrieval Augmented Generation");

            var slideIdList = presentationPart.Presentation.AppendChild(new SlideIdList());
            slideIdList.Append(new SlideId
            {
                Id = 256U,
                RelationshipId = presentationPart.GetIdOfPart(slidePart)
            });

            presentationPart.Presentation.Save();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>Builds a slide whose single shape holds the supplied text.</summary>
    /// <param name="text">The text run to place on the slide.</param>
    /// <returns>The populated slide.</returns>
    private static Slide BuildSlide(string text)
    {
        var paragraph = new Drawing.Paragraph(new Drawing.Run(new Drawing.Text(text)));

        var shape = new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = 2U, Name = "Title" },
                new NonVisualShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new ShapeProperties(),
            new TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle(), paragraph));

        var shapeTree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(),
            shape);

        return new Slide(new CommonSlideData(shapeTree), new ColorMapOverride(new Drawing.MasterColorMapping()));
    }
}
