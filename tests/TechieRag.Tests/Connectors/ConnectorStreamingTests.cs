using TechieRag.Connectors;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-083 / BRD-127 (TR-RAG-020/022): <see cref="ConnectorRunner"/> hands each fetched
/// document to ingestion as it arrives, so a run stopped part-way keeps what it fetched.
/// </summary>
public sealed class ConnectorStreamingTests
{
    /// <summary>
    /// A connector ingestion cancelled while fetching the third item has already ingested the first
    /// two. Before this change the run collected every document and ingested after the walk, so the
    /// same cancellation ingested nothing.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-083 ACancelledRunHasAlreadyIngestedWhatItFetched")]
    public async Task ACancelledRunHasAlreadyIngestedWhatItFetched()
    {
        using var stop = new CancellationTokenSource();
        var connector = new StopAtConnector(
            new FakeDataConnector().Page(Item("a"), Item("b"), Item("c"), Item("d")), "c", stop);
        var rag = new RecordingRag();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rag.IngestConnectorAsync(connector, options: NoDelay(), cancellationToken: stop.Token));

        Assert.Equal(new[] { "a", "b" }, rag.Ingested.Select(entry => entry.Name));
    }

    /// <summary>
    /// Each document is delivered before the next item is fetched — the handler sees a document the
    /// moment it exists, not at the end of the walk.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-083 EachDocumentIsHandedOverBeforeTheNextFetch")]
    public async Task EachDocumentIsHandedOverBeforeTheNextFetch()
    {
        var events = new List<string>();
        var connector = new StopAtConnector(new FakeDataConnector().Page(Item("a"), Item("b"), Item("c")), null, null, events);

        var result = await new ConnectorRunner().RunAsync(
            connector,
            null,
            NoDelay(),
            (document, _) =>
            {
                events.Add($"deliver {document.Item.Id}");
                return Task.CompletedTask;
            });

        Assert.Equal(
            new[] { "fetch a", "deliver a", "fetch b", "deliver b", "fetch c", "deliver c" },
            events);
        Assert.Empty(result.Documents);
        Assert.Equal(3, result.Sync.ItemVersions.Count);
    }

    /// <summary>
    /// A handler that fails ends the run with its own exception rather than being recorded as the
    /// source's fault, and nothing after the failed document is fetched.
    /// </summary>
    [Fact]
    public async Task AFailingHandlerEndsTheRun()
    {
        var connector = new FakeDataConnector().Page(Item("a"), Item("b"), Item("c"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConnectorRunner().RunAsync(
            connector,
            null,
            NoDelay(),
            (document, _) => document.Item.Id == "b"
                ? throw new InvalidOperationException("the embedding service is down")
                : Task.CompletedTask));

        Assert.Equal(new[] { "a", "b" }, connector.Fetched);
    }

    /// <summary>
    /// The item budget still holds when documents are streamed rather than collected.
    /// </summary>
    [Fact]
    public async Task StreamingStillStopsAtMaxItems()
    {
        var delivered = new List<string>();
        var connector = new FakeDataConnector().Page(Item("a"), Item("b"), Item("c"));
        var options = NoDelay();
        options.MaxItems = 2;

        var result = await new ConnectorRunner().RunAsync(
            connector, null, options, (document, _) =>
            {
                delivered.Add(document.Item.Id);
                return Task.CompletedTask;
            });

        Assert.Equal(new[] { "a", "b" }, delivered);
        Assert.True(result.ReachedLimit);
    }

    private static ConnectorRunOptions NoDelay() => new() { RequestDelay = TimeSpan.Zero };

    private static ConnectorItem Item(string id) => new(id, id, $"https://example.test/{id}", "v1");

    /// <summary>
    /// Wraps a connector to record each fetch and, optionally, to stop the run the way a user
    /// pressing Stop does: the token is cancelled while an item is being fetched.
    /// </summary>
    /// <param name="inner">The scripted connector.</param>
    /// <param name="stopAtId">The item whose fetch cancels the run, or null never to stop.</param>
    /// <param name="stop">The run's token source.</param>
    /// <param name="events">Where fetches are recorded, or null.</param>
    private sealed class StopAtConnector(
        FakeDataConnector inner, string? stopAtId, CancellationTokenSource? stop, List<string>? events = null) : IDataConnector
    {
        public string SourceType => inner.SourceType;

        public string SourceName => inner.SourceName;

        public bool ListsEntireSource => inner.ListsEntireSource;

        public Task<ConnectorPage> ListAsync(ConnectorListRequest request, CancellationToken cancellationToken = default) =>
            inner.ListAsync(request, cancellationToken);

        public async Task<ConnectorDocument> FetchAsync(ConnectorItem item, CancellationToken cancellationToken = default)
        {
            events?.Add($"fetch {item.Id}");

            if (item.Id == stopAtId && stop is not null)
            {
                await stop.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await inner.FetchAsync(item, cancellationToken);
        }
    }
}
