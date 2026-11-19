using Microsoft.Data.Sqlite;
using TechieRag.Abstractions;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// REQ-RAG-172 (MyDiary TR-RAG-001): the library gives an exact count with its source entries, and abstains when
/// no entry matches, without depending on the model to count or to refuse.
/// </summary>
/// <remarks>
/// <para><b>The report.</b> <c>AskAsync</c> returned a generated answer and the top-K retrieved sources; nothing
/// promised that the sources were every matching entry, so a count read from them was not a count, and nothing made
/// the answer stop when no entry matched. The local model also cannot call tools, so a counting tool was no way out.</para>
/// <para><b>The diary.</b> 100 entries: 40 about the gym, 60 about other things; a three-word embedder makes "gym"
/// score 0.99 against a gym entry and 0.1 against the rest. 40 is more than the first batch of 32 the search reads, so
/// the count proves the batches widen.</para>
/// </remarks>
public sealed class EvidenceAnswerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trevidence-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="EvidenceAnswerTests"/> class.</summary>
    public EvidenceAnswerTests() => Directory.CreateDirectory(folder);

    /// <summary>
    /// The count is every gym entry, 40, each named once, no other entry, though a top-5 search would show five.
    /// </summary>
    /// <returns>A task that completes when the matches are counted.</returns>
    [Fact(DisplayName = "REQ-RAG-172 CountIsEveryMatchingEntry")]
    public async Task CountIsEveryMatchingEntry()
    {
        var (rag, _) = await DiaryAsync();

        var report = await rag.FindMatchesAsync("gym", new MatchOptions { MinScore = 0.8f });

        Assert.Equal(40, report.DocumentCount);
        Assert.True(report.IsComplete);
        Assert.Equal(40, report.Documents.Select(document => document.DocumentName).Distinct().Count());
        Assert.All(report.Documents, document => Assert.StartsWith("gym-", document.DocumentName));
        Assert.Equal(5, (await rag.SearchAsync("gym", topK: 5)).Count);
    }

    /// <summary>
    /// With matches, the model is told the exact count and the names, answers from matching entries only, and the
    /// response carries the library's count and sources.
    /// </summary>
    /// <returns>A task that completes when the answer is back.</returns>
    [Fact(DisplayName = "REQ-RAG-172 AnswerCarriesTheLibrarysCountAndSources")]
    public async Task AnswerCarriesTheLibrarysCountAndSources()
    {
        var (rag, llm) = await DiaryAsync();

        var response = await rag.AskWithEvidenceAsync("How many times did I go to the gym?", new EvidenceAskOptions { Match = new MatchOptions { MinScore = 0.8f } });

        Assert.False(response.Abstained);
        Assert.Equal(40, response.Matches!.DocumentCount);
        Assert.Equal(8, response.Sources.Count);
        Assert.All(response.Sources, source => Assert.StartsWith("gym", source.Chunk.Text));
        var system = Assert.Single(llm.Calls).First(message => message.Role == "system").Content;
        Assert.Contains("found exactly 40 matching entries", system);
        Assert.Contains("the answer is 40", system);
        Assert.Contains("begin your reply with the number 40", system);
    }

    /// <summary>
    /// With no matching entry, the answer is withheld: no model call, an empty answer and no sources, and the report
    /// says nothing matched.
    /// </summary>
    /// <returns>A task that completes when the call returns.</returns>
    [Fact(DisplayName = "REQ-RAG-172 NoMatchAbstainsWithoutCallingTheModel")]
    public async Task NoMatchAbstainsWithoutCallingTheModel()
    {
        var (rag, llm) = await DiaryAsync();

        var response = await rag.AskWithEvidenceAsync("When did I go swimming?", new EvidenceAskOptions { Match = new MatchOptions { MinScore = 0.8f } });

        Assert.True(response.Abstained);
        Assert.True(response.Matches!.IsEmpty);
        Assert.Equal(string.Empty, response.Answer);
        Assert.Empty(response.Sources);
        Assert.Empty(llm.Calls);
    }

    /// <summary>A metadata scope narrows the count to one month's gym entries.</summary>
    /// <returns>A task that completes when the matches are counted.</returns>
    [Fact(DisplayName = "REQ-RAG-172 ScopeNarrowsTheCount")]
    public async Task ScopeNarrowsTheCount()
    {
        var (rag, _) = await DiaryAsync();

        var october = await rag.FindMatchesAsync("gym", new MatchOptions { MinScore = 0.8f, MetadataFilter = new Dictionary<string, string> { ["month"] = "2026-10" } });

        Assert.Equal(20, october.DocumentCount);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held is left in the temp folder.
        }
    }

    private async Task<(ITechieRag Rag, RecordingLlm Llm)> DiaryAsync()
    {
        var llm = new RecordingLlm();
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new DiaryEmbedder())
            .UseCustomLlmProvider(() => llm)
            .UseSqliteVec(Path.Combine(folder, "diary.db"))
            .WithChunkSize(2000, 0)
            .Build();
        await rag.InitializeAsync();
        for (var day = 0; day < 100; day++)
        {
            var gym = day % 5 < 2;
            var month = day < 50 ? "2026-10" : "2026-11";
            await rag.IngestTextAsync(
                gym ? $"gym session on day {day}, legs and back" : $"quiet day {day}, read a book",
                (gym ? "gym-" : "day-") + day,
                new Dictionary<string, object> { ["month"] = month });
        }

        return (rag, llm);
    }

    /// <summary>Gym, swimming and a constant, so "gym" matches gym entries only and "swimming" matches none.</summary>
    private sealed class DiaryEmbedder : IEmbeddingProvider
    {
        public string Name => "Diary";

        public string ModelName => "diary-3";

        public int Dimensions => 3;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Vector(text));

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());

        private static float[] Vector(string text) =>
        [
            text.Contains("gym", StringComparison.OrdinalIgnoreCase) ? 1f : 0f,
            text.Contains("swimming", StringComparison.OrdinalIgnoreCase) ? 1f : 0f,
            0.1f
        ];
    }

    /// <summary>Records each chat call and answers "ok".</summary>
    private sealed class RecordingLlm : ILlmProvider
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public string Name => "Recording";

        public string ModelName => "recording";

        public string? Route => null;

        public bool SupportsToolCalling => false;

        public bool SupportsStreaming => false;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

        public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(messages);
            return Task.FromResult(new LlmResponse { Content = "ok", Usage = new TokenUsage(), ModelName = ModelName });
        }

        public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class => throw new NotSupportedException();

        public int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);
    }
}
