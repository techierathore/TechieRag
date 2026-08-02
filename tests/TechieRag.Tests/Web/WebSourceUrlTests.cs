using TechieRag.Models;
using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web;

/// <summary>
/// REQ-RAG-016/017/018: reading back where an ingested web document came from.
/// </summary>
/// <remarks>
/// <para>The regression this pins was found by driving the real stack: web ingestion recorded the
/// URL in the document's metadata, the SQLite-vec store wrote the document row's <c>Metadata</c>
/// column as a literal <c>{}</c>, and every "Add from web" result therefore came back with a blank
/// source. The ingestion looked entirely successful — the count was right and the documents were
/// searchable — and the column the user reads was empty.</para>
/// <para>No fake fetcher was capable of catching that. The value only disappears on the way through
/// a real vector store, which every hermetic test replaces.</para>
/// </remarks>
public sealed class WebSourceUrlTests
{
    /// <summary>Metadata is used when the store round-tripped it.</summary>
    [Fact]
    public void SourceUrlIsReadFromMetadataWhenPresent()
    {
        var document = Document("https://example.com/page", sourcePath: "text-input");

        Assert.Equal("https://example.com/page", document.WebSourceUrl());
    }

    /// <summary>
    /// The document row's source path is used when the store dropped the metadata.
    /// </summary>
    /// <remarks>
    /// This is the shape a document actually has after a round trip through the desktop app's
    /// default vector store, so it is the case that decides whether the screen shows a link.
    /// </remarks>
    [Fact]
    public void SourceUrlFallsBackToSourcePathWhenMetadataWasDropped()
    {
        var document = Document(metadataUrl: null, sourcePath: "https://example.com/page");

        Assert.Equal("https://example.com/page", document.WebSourceUrl());
    }

    /// <summary>
    /// A document that did not come from the web reports no source URL rather than a file path.
    /// </summary>
    /// <remarks>
    /// Returning "text-input" or <c>C:\reports\q3.pdf</c> as a source URL would put a value in a
    /// column that is rendered as a link, which is worse than leaving it blank.
    /// </remarks>
    [Theory]
    [InlineData("text-input")]
    [InlineData("/Users/someone/reports/q3.pdf")]
    [InlineData("C:\\reports\\q3.pdf")]
    [InlineData("")]
    public void NonWebDocumentsReportNoSourceUrl(string sourcePath)
    {
        var document = Document(metadataUrl: null, sourcePath);

        Assert.Equal(string.Empty, document.WebSourceUrl());
    }

    /// <summary>
    /// Metadata wins over the source path, so a redirect's final URL is not overridden by a stale
    /// row value.
    /// </summary>
    [Fact]
    public void MetadataIsPreferredOverTheSourcePath()
    {
        var document = Document("https://example.com/final", sourcePath: "https://example.com/old");

        Assert.Equal("https://example.com/final", document.WebSourceUrl());
    }

    private static Document Document(string? metadataUrl, string sourcePath)
    {
        var metadata = new Dictionary<string, object>();
        if (metadataUrl is not null)
        {
            metadata["SourceUrl"] = metadataUrl;
        }

        return new Document
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Example",
            SourcePath = sourcePath,
            ChunkCount = 1,
            IngestedAt = DateTime.UtcNow,
            Metadata = metadata,
        };
    }

}
