using TechieRag.Connectors;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-032 / BRD-113: what reaches the index — the metadata a citation needs, and the honest
/// accounting of what did not.
/// </summary>
public sealed class ConnectorIngestionTests
{
    /// <summary>Every fetched document is ingested under the item's own name.</summary>
    [Fact]
    public async Task IngestsEveryFetchedDocument()
    {
        var rag = new RecordingRag();
        var connector = new FakeDataConnector().Page(Item("a"), Item("b"));

        var result = await rag.IngestConnectorAsync(connector, options: NoDelay());

        Assert.Equal(2, result.DocumentIds.Count);
        Assert.Equal(["a", "b"], rag.Ingested.Select(i => i.Name));
    }

    /// <summary>A citation needs to know where a document came from, so the source is recorded on it.</summary>
    [Fact]
    public async Task RecordsTheSourceOnEveryDocument()
    {
        var rag = new RecordingRag();
        var connector = new FakeDataConnector().Page(Item("a"));

        await rag.IngestConnectorAsync(connector, options: NoDelay());

        var metadata = rag.Ingested[0].Metadata!;
        Assert.Equal("fake", metadata["SourceType"]);
        Assert.Equal("https://example.test/a", metadata["SourceUrl"]);
        Assert.Equal("a", metadata["ItemId"]);
        Assert.Equal("v1", metadata["Version"]);
    }

    /// <summary>
    /// A connector's own metadata cannot overwrite the framework's keys. One that happened to emit
    /// "SourceType" would otherwise corrupt the field citations depend on.
    /// </summary>
    [Fact]
    public async Task SourceMetadataCannotOverwriteFrameworkKeys()
    {
        var rag = new RecordingRag();
        var item = new ConnectorItem(
            "a",
            "a",
            "https://example.test/a",
            "v1",
            null,
            null,
            new Dictionary<string, string> { ["SourceType"] = "spoofed", ["Branch"] = "main" });

        await rag.IngestConnectorAsync(new FakeDataConnector().Page(item), options: NoDelay());

        Assert.Equal("fake", rag.Ingested[0].Metadata!["SourceType"]);
        Assert.Equal("main", rag.Ingested[0].Metadata!["Branch"]);
    }

    /// <summary>
    /// An item with no readable text is skipped with a reason. Ingesting it would add a document that
    /// can never be retrieved and make the ingested count a lie about what is searchable.
    /// </summary>
    [Fact]
    public async Task SkipsEmptyItemsWithAReason()
    {
        var rag = new RecordingRag();
        var connector = new FakeDataConnector().Page(Item("empty"), Item("real")).WithText("empty", "   ");

        var result = await rag.IngestConnectorAsync(connector, options: NoDelay());

        Assert.Single(result.DocumentIds);
        Assert.Contains(result.Skipped, s => s.ItemId == "empty" && s.Reason.Contains("no readable text", StringComparison.Ordinal));
    }

    /// <summary>Failures from the run reach the caller alongside what was ingested.</summary>
    [Fact]
    public async Task CarriesRunFailuresThrough()
    {
        var rag = new RecordingRag();
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("bad"))
            .WithFailure("bad", new InvalidOperationException("unreadable"));

        var result = await rag.IngestConnectorAsync(connector, options: NoDelay());

        Assert.Single(result.DocumentIds);
        Assert.Contains(result.Skipped, s => s.ItemId == "bad");
    }

    /// <summary>The state for the next run comes back with the result, ready to persist.</summary>
    [Fact]
    public async Task ReturnsSyncStateForTheNextRun()
    {
        var rag = new RecordingRag();

        var result = await rag.IngestConnectorAsync(
            new FakeDataConnector().Page(Item("a")), options: NoDelay());

        Assert.Equal("v1", result.Sync.ItemVersions["a"]);
        Assert.NotNull(result.Sync.LastRunUtc);
    }

    /// <summary>A second run with the returned state ingests nothing that has not changed.</summary>
    [Fact]
    public async Task ASecondRunIngestsOnlyWhatChanged()
    {
        var rag = new RecordingRag();
        var first = await rag.IngestConnectorAsync(
            new FakeDataConnector().Page(Item("a"), Item("b")), options: NoDelay());

        var second = await rag.IngestConnectorAsync(
            new FakeDataConnector().Page(Item("a"), Item("b", "v2")), first.Sync, NoDelay());

        Assert.Single(second.DocumentIds);
        Assert.Equal("b", rag.Ingested[^1].Name);
    }

    private static ConnectorRunOptions NoDelay() => new() { RequestDelay = TimeSpan.Zero };

    /// <summary>
    /// REQ-UI-021 / TR-RAG-038: a source that reports an item's size has that size recorded on the
    /// document, so the library's Size column shows what the SOURCE says rather than the byte count
    /// of the extracted text.
    /// </summary>
    [Fact]
    public async Task RecordsTheSourceReportedSizeWhenThereIsOne()
    {
        var rag = new RecordingRag();
        var item = new ConnectorItem("a", "a", "https://example.test/a", "v1", null, SizeBytes: 4096);

        await rag.IngestConnectorAsync(new FakeDataConnector().Page(item), options: NoDelay());

        Assert.Equal(4096L, rag.Ingested[0].Metadata![TechieRag.Models.DocumentMetadataKeys.FileSize]);
    }

    /// <summary>
    /// A source that reports no size records none here, leaving the size the text ingestion route
    /// computes for itself rather than inventing a zero.
    /// </summary>
    [Fact]
    public async Task RecordsNoSizeWhenTheSourceReportsNone()
    {
        var rag = new RecordingRag();

        await rag.IngestConnectorAsync(new FakeDataConnector().Page(Item("a")), options: NoDelay());

        Assert.False(rag.Ingested[0].Metadata!.ContainsKey(TechieRag.Models.DocumentMetadataKeys.FileSize));
    }

    private static ConnectorItem Item(string id, string version = "v1") =>
        new(id, id, $"https://example.test/{id}", version);
}
