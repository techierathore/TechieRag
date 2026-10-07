using System.Globalization;
using TechieRag.Connectors;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// Interrupted connector runs hand back what they gathered (REQ-RAG-117 / BRD-177, TR-RAG-022), and
/// item results are typed and culture-independent (REQ-RAG-118 / BRD-178, TR-RAG-023).
/// </summary>
public sealed class ConnectorPartialRunTests
{
    /// <summary>
    /// A developer cancels a run after 3 of 10 items: the cancellation still carries the sync state,
    /// and it records exactly those 3 items, so the next run resumes instead of starting over.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-117 CancelledRunReturnsSyncForFinishedItems")]
    public async Task CancelledRunReturnsSyncForFinishedItems()
    {
        var items = Enumerable.Range(1, 10).Select(i => Item($"item-{i}")).ToArray();
        var connector = new FakeDataConnector().Page(items);
        using var cancellation = new CancellationTokenSource();
        var handled = 0;

        var error = await Assert.ThrowsAsync<ConnectorRunCanceledException>(() => new ConnectorRunner().RunAsync(
            connector,
            null,
            null,
            (_, _) =>
            {
                if (++handled == 3)
                {
                    cancellation.Cancel();
                }

                return Task.CompletedTask;
            },
            cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(["item-1", "item-2", "item-3"], error.PartialResult.Sync.ItemVersions.Keys.Order());
        Assert.Null(error.PartialResult.Sync.LastRunUtc);
    }

    /// <summary>
    /// The resumed run skips the three finished items as unchanged and fetches only the other seven.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-117 ResumedRunSkipsFinishedItems")]
    public async Task ResumedRunSkipsFinishedItems()
    {
        var items = Enumerable.Range(1, 10).Select(i => Item($"item-{i}")).ToArray();
        using var cancellation = new CancellationTokenSource();
        var handled = 0;
        var first = await Assert.ThrowsAsync<ConnectorRunCanceledException>(() => new ConnectorRunner().RunAsync(
            new FakeDataConnector().Page(items),
            null,
            null,
            (_, _) =>
            {
                if (++handled == 3) cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token));

        var resumed = new FakeDataConnector().Page(items);
        await new ConnectorRunner().RunAsync(resumed, first.PartialResult.Sync);

        Assert.Equal(7, resumed.Fetched.Count);
        Assert.DoesNotContain("item-1", resumed.Fetched);
    }

    /// <summary>
    /// A run that fails part-way (too many failures in a row) carries the per-item reasons and the
    /// sync state gathered before the failure on the exception, through connector ingestion too.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-117 FailedRunCarriesPartialOutcome")]
    public async Task FailedRunCarriesPartialOutcome()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"), Item("x1"), Item("x2"), Item("x3"), Item("z"))
            .WithFailure("x1", new IOException("one"))
            .WithFailure("x2", new IOException("two"))
            .WithFailure("x3", new IOException("three"));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => new ConnectorRunner().RunAsync(
            connector, null, new ConnectorRunOptions { MaxConsecutiveFailures = 3 }));

        Assert.NotNull(error.PartialResult);
        Assert.Equal(["a", "b"], error.PartialResult!.Sync.ItemVersions.Keys.Order());
        Assert.Equal(["x1", "x2", "x3"], error.PartialResult.Failures.Select(f => f.ItemId));
    }

    /// <summary>
    /// An item is skipped for size on a German-language machine: its outcome reads Skipped as a typed
    /// value and its size has no thousands separator.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-118 SizeSkipIsTypedAndCultureInvariant")]
    public async Task SizeSkipIsTypedAndCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var connector = new FakeDataConnector().Page(Item("big") with { SizeBytes = 2_500_000 });

            var result = await new ConnectorRunner().RunAsync(connector, null, new ConnectorRunOptions { MaxItemBytes = 1_000_000 });

            var skipped = Assert.Single(result.Failures);
            Assert.Equal(ConnectorItemOutcome.Skipped, skipped.Outcome);
            Assert.Contains("2500000 bytes", skipped.Reason, StringComparison.Ordinal);
            Assert.Contains("1000000-byte", skipped.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("2.500.000", skipped.Reason, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>An item whose fetch throws reads Failed, the typed default.</summary>
    [Fact(DisplayName = "REQ-RAG-118 FetchFailureIsTypedFailed")]
    public async Task FetchFailureIsTypedFailed()
    {
        var connector = new FakeDataConnector().Page(Item("a"), Item("bad")).WithFailure("bad", new IOException("boom"));

        var result = await new ConnectorRunner().RunAsync(connector);

        Assert.Equal(ConnectorItemOutcome.Failed, Assert.Single(result.Failures).Outcome);
    }

    /// <summary>
    /// The metadata builder connector ingestion uses is public, so a host ingesting connector documents
    /// its own way stores the same keys.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-118 MetadataBuilderIsPublic")]
    public void MetadataBuilderIsPublic()
    {
        var connector = new FakeDataConnector();

        var metadata = ConnectorIngestionExtensions.BuildMetadata(connector, Item("doc-7") with { SizeBytes = 42 });

        Assert.Equal("doc-7", metadata["ItemId"]);
        Assert.Equal("fake", metadata["SourceType"]);
        Assert.Equal(42L, metadata[TechieRag.Models.DocumentMetadataKeys.FileSize]);
        Assert.True(typeof(ConnectorIngestionExtensions).GetMethod(nameof(ConnectorIngestionExtensions.BuildMetadata))!.IsPublic);
    }

    private static ConnectorItem Item(string id) => new(id, id, $"https://example.test/{id}", Version: "v1");
}
