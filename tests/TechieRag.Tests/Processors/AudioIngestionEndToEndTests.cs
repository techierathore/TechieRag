using TechieRag.Speech;
using TechieRag.Tests.Speech;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Processors;

/// <summary>
/// REQ-RAG-073 / BRD-117 acceptance end to end: an audio file ingested through the client with an
/// OpenAI-compatible speech endpoint configured is transcribed, chunked, embedded and stored.
/// </summary>
/// <remarks>
/// The speech endpoint is a stubbed <see cref="HttpMessageHandler"/> answering the real
/// <c>verbose_json</c> wire shape, and the store is a real temporary SQLite database, so the whole
/// path from <c>IngestAsync</c> to a searchable chunk runs with no network.
/// </remarks>
public sealed class AudioIngestionEndToEndTests : IDisposable
{
    private readonly string workFolder = Path.Combine(Path.GetTempPath(), $"traudio-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary folder holding the audio file and the vector database.</summary>
    public AudioIngestionEndToEndTests() => Directory.CreateDirectory(workFolder);

    /// <summary>
    /// When a developer ingests an .mp3 with a speech endpoint configured, the endpoint is called,
    /// and the transcript comes back from a search as stored, embedded chunks of the document.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-073 AnAudioFileIsTranscribedChunkedAndEmbedded")]
    public async Task AnAudioFileIsTranscribedChunkedAndEmbedded()
    {
        var speech = new SpeechStubHandler(TranscriptJson, "application/json");
        var rag = new TechieRagBuilder()
            .UseSpeechToText(new OpenAICompatibleSpeechToText(
                new HttpClient(speech) { BaseAddress = new Uri("http://localhost:1234") }, "whisper-1"))
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(workFolder, "vectors.db"))
            .Build();
        await rag.InitializeAsync();

        var audioPath = Path.Combine(workFolder, "standup.mp3");
        await File.WriteAllBytesAsync(audioPath, "fake-audio-bytes"u8.ToArray());

        var documentId = await rag.IngestAsync(audioPath);
        var found = await rag.SearchAsync("what is the deploy status?", 10);

        Assert.EndsWith("/audio/transcriptions", speech.Path, StringComparison.Ordinal);
        Assert.NotEmpty(found);
        Assert.All(found, result => Assert.Equal(documentId, result.Chunk.DocumentId));
        Assert.Contains(found, result => result.Chunk.Text.Contains("Deploy is green.", StringComparison.Ordinal));
        Assert.Contains(await rag.ListDocumentsAsync(), document => document.Id == documentId && document.ChunkCount > 0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true);
    }

    private const string TranscriptJson =
        """
        {
          "task": "transcribe",
          "language": "en",
          "duration": 7.5,
          "text": "Deploy is green. Ship it.",
          "segments": [
            { "id": 0, "start": 0.5, "end": 3.25, "text": " Deploy is green." },
            { "id": 1, "start": 3.25, "end": 7.5, "text": " Ship it." }
          ]
        }
        """;
}
