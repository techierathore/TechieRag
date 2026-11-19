using Microsoft.Data.Sqlite;
using TechieRag.Abstractions;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// REQ-RAG-172 (MyDiary TR-RAG-001) on the built-in model: a counted, sourced answer and a no-match abstention need
/// no tool call, so they work with <c>TechieRag.Local</c>.
/// </summary>
/// <remarks>
/// Seven diary entries, three about the gym; a three-word embedder so the matches are certain and the test is about
/// the answer path, not about retrieval quality. Runs where the platform's local model is downloaded.
/// </remarks>
[Collection(LiveLocalLlmCollection.Name)]
public sealed class LiveEvidenceTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trlocalevidence-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="LiveEvidenceTests"/> class.</summary>
    public LiveEvidenceTests() => Directory.CreateDirectory(folder);

    /// <summary>
    /// The local model, told the library's count, states it: the answer names 3 (or "three"), the report counts 3,
    /// and a question with no matching entry is withheld before the model.
    /// </summary>
    /// <returns>A task that completes when both answers are back.</returns>
    [LiveLocalLlmFact(DisplayName = "REQ-RAG-172 LiveLocalModelStatesTheLibrarysCount")]
    public async Task LiveLocalModelStatesTheLibrarysCount()
    {
        using var llm = new LocalLlmProvider(new LocalLlmOptions { Model = LiveLocalLlmFactAttribute.ModelToTest, Temperature = 0f });
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new DiaryEmbedder())
            .UseCustomLlmProvider(() => llm)
            .UseSqliteVec(Path.Combine(folder, "diary.db"))
            .Build();
        await rag.InitializeAsync();
        string[] diary =
        [
            "Monday: gym, legs.", "Tuesday: read a novel.", "Wednesday: gym, back and arms.", "Thursday: dinner with Anu.",
            "Friday: gym, a short run.", "Saturday: cleaned the flat.", "Sunday: visited my parents."
        ];
        for (var i = 0; i < diary.Length; i++)
        {
            await rag.IngestTextAsync(diary[i], $"entry-{i + 1}");
        }

        var options = new EvidenceAskOptions
        {
            Match = new MatchOptions { MinScore = 0.8f },
            CompletionOptions = new LlmCompletionOptions { MaxTokens = 60, Temperature = 0f }
        };
        var counted = await rag.AskWithEvidenceAsync("How many times did I go to the gym this week?", options);
        var none = await rag.AskWithEvidenceAsync("When did I go swimming?", options);

        Assert.Equal(3, counted.Matches!.DocumentCount);
        Assert.False(counted.Abstained);
        Assert.True(
            counted.Answer.Contains('3') || counted.Answer.Contains("three", StringComparison.OrdinalIgnoreCase),
            $"The local model did not state the count: {counted.Answer}");
        Assert.True(none.Abstained);
        Assert.Empty(none.Answer);
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

    /// <summary>Gym, swimming and a constant.</summary>
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
}
