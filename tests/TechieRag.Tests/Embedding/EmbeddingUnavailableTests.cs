using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;
using TechieRag.Embedding;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// REQ-RAG-170 (TrStudio TR-RAG-004): an embedding service that cannot be reached is reported as
/// <see cref="EmbeddingUnavailableException"/>, still an <see cref="HttpRequestException"/>.
/// </summary>
/// <remarks>
/// TrStudio's repro: stop the default Ollama (<c>localhost:11434</c> refused) and call <c>InitializeAsync</c>,
/// <c>SearchAsync</c> or <c>IngestTextAsync</c>; on 1.1.5 the raw <c>HttpRequestException</c> /
/// <c>SocketException</c> reached the caller, the same type as any other HTTP failure. Here a port nothing listens
/// on stands in for the stopped Ollama.
/// </remarks>
public sealed class EmbeddingUnavailableTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trunavail-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="EmbeddingUnavailableTests"/> class.</summary>
    public EmbeddingUnavailableTests() => Directory.CreateDirectory(folder);

    /// <summary>
    /// TrStudio's repro through the client: with Ollama stopped, ingesting and searching throw the typed exception,
    /// naming Ollama, and an existing <c>catch (HttpRequestException)</c> still catches it.
    /// </summary>
    /// <returns>A task that completes when both calls have failed.</returns>
    [Fact(DisplayName = "REQ-RAG-170 StoppedOllamaIsReportedAsUnavailable")]
    public async Task StoppedOllamaIsReportedAsUnavailable()
    {
        var rag = new TechieRagBuilder()
            .UseOllama($"http://127.0.0.1:{ClosedPort()}", "bge-m3")
            .UseSqliteVec(Path.Combine(folder, "rag.db"))
            .Build();
        await rag.InitializeAsync();

        var onIngest = await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => rag.IngestTextAsync("A note about Saturn.", "note"));
        var onSearch = await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => rag.SearchAsync("Saturn"));

        Assert.Equal("Ollama", onIngest.ProviderName);
        Assert.IsAssignableFrom<HttpRequestException>(onSearch);
        Assert.Null(onSearch.StatusCode);
    }

    /// <summary>
    /// An OpenAI-compatible service that cannot be reached is unavailable; one that answers with an error status
    /// was reached, so it stays a plain <see cref="HttpRequestException"/> with that status.
    /// </summary>
    /// <returns>A task that completes when both calls have failed.</returns>
    [Fact(DisplayName = "REQ-RAG-170 AnsweredErrorIsNotUnavailable")]
    public async Task AnsweredErrorIsNotUnavailable()
    {
        using var down = new HttpClient(new FailingHandler(new HttpRequestException("Connection refused (127.0.0.1:1)"))) { BaseAddress = new Uri("http://127.0.0.1:1/v1/") };
        using var refusing = new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized)) { BaseAddress = new Uri("https://api.example.test/v1/") };
        using var downProvider = new OpenAICompatibleEmbeddingProvider(down, "Example", "text-embed", 3);
        using var refusingProvider = new OpenAICompatibleEmbeddingProvider(refusing, "Example", "text-embed", 3);

        var unavailable = await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => downProvider.EmbedAsync("hello"));
        var refused = await Assert.ThrowsAnyAsync<HttpRequestException>(() => refusingProvider.EmbedAsync("hello"));

        Assert.Contains("http://127.0.0.1:1", unavailable.Message);
        Assert.IsNotType<EmbeddingUnavailableException>(refused);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>A service that does not answer in time is unavailable; a cancellation the caller asked for stays a cancellation.</summary>
    /// <returns>A task that completes when both calls have ended.</returns>
    [Fact(DisplayName = "REQ-RAG-170 TimeoutIsUnavailableButCancellationIsNot")]
    public async Task TimeoutIsUnavailableButCancellationIsNot()
    {
        using var slow = new HttpClient(new FailingHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."))) { BaseAddress = new Uri("http://127.0.0.1:1/") };
        using var provider = new OllamaEmbeddingProvider(slow, "bge-m3", 3);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => provider.EmbedAsync("hello"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedAsync("hello", cancelled.Token));
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

    /// <summary>A loopback port that nothing listens on.</summary>
    private static int ClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Fails every request the way the network stack does.</summary>
    private sealed class FailingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<HttpResponseMessage>(failure);
        }
    }

    /// <summary>Answers every request with one status.</summary>
    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"error":{"message":"bad key"}}""", Encoding.UTF8, "application/json") });
    }
}
