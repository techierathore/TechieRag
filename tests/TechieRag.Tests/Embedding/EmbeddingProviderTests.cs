using System.Net;
using System.Text;
using System.Text.Json;
using TechieRag.Embedding;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Unit tests for the embedding providers added by REQ-RAG-035 (Cohere, Voyage, Mistral, Gemini),
/// driven through a stubbed <see cref="HttpMessageHandler"/> so no API is called.
/// </summary>
public class EmbeddingProviderTests
{
    /// <summary>Cohere's grouped embeddings.float response is read into vectors.</summary>
    [Fact]
    public async Task CohereReadsGroupedFloatEmbeddings()
    {
        var handler = new StubHandler("""{"embeddings":{"float":[[0.1,0.2],[0.3,0.4]]}}""");
        var provider = new CohereEmbeddingProvider(Client(handler), dimensions: 2);

        var vectors = await provider.EmbedBatchAsync(["alpha", "beta"]);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(0.3f, vectors[1][0], 5);
    }

    /// <summary>Cohere embeds documents with search_document and queries with search_query.</summary>
    [Fact]
    public async Task CohereUsesTheCorrectInputTypePerCall()
    {
        var handler = new StubHandler("""{"embeddings":{"float":[[0.1]]}}""");
        var provider = new CohereEmbeddingProvider(Client(handler), dimensions: 1);

        await provider.EmbedAsync("a document");
        Assert.Equal("search_document", ReadProperty(handler.LastBody!, "input_type"));

        await provider.EmbedQueryAsync("a query");
        Assert.Equal("search_query", ReadProperty(handler.LastBody!, "input_type"));
    }

    /// <summary>A short Cohere response fails loudly rather than misaligning chunks and vectors.</summary>
    [Fact]
    public async Task CohereRejectsAShortResponse()
    {
        var handler = new StubHandler("""{"embeddings":{"float":[[0.1,0.2]]}}""");
        var provider = new CohereEmbeddingProvider(Client(handler), dimensions: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedBatchAsync(["alpha", "beta"]));
    }

    /// <summary>Voyage sends its asymmetric input types and reads the OpenAI-shaped response.</summary>
    [Fact]
    public async Task VoyageUsesDocumentAndQueryInputTypes()
    {
        var handler = new StubHandler("""{"data":[{"index":0,"embedding":[0.5,0.6]}]}""");
        var provider = new OpenAICompatibleEmbeddingProvider(
            Client(handler), "Voyage", "voyage-3.5", 2, documentInputType: "document", queryInputType: "query");

        var document = await provider.EmbedAsync("a document");
        Assert.Equal("document", ReadProperty(handler.LastBody!, "input_type"));
        Assert.Equal(0.5f, document[0], 5);

        await provider.EmbedQueryAsync("a query");
        Assert.Equal("query", ReadProperty(handler.LastBody!, "input_type"));
    }

    /// <summary>Mistral is symmetric and sends no input_type at all.</summary>
    [Fact]
    public async Task MistralSendsNoInputType()
    {
        var handler = new StubHandler("""{"data":[{"index":0,"embedding":[0.9]}]}""");
        var provider = new OpenAICompatibleEmbeddingProvider(Client(handler), "Mistral", "mistral-embed", 1);

        await provider.EmbedAsync("text");

        using var document = JsonDocument.Parse(handler.LastBody!);
        Assert.False(document.RootElement.TryGetProperty("input_type", out _));
    }

    /// <summary>Out-of-order batch results are reordered by the index the service reported.</summary>
    [Fact]
    public async Task OutOfOrderBatchResultsAreReordered()
    {
        var handler = new StubHandler("""{"data":[{"index":1,"embedding":[2.0]},{"index":0,"embedding":[1.0]}]}""");
        var provider = new OpenAICompatibleEmbeddingProvider(Client(handler), "Voyage", "voyage-3.5", 1);

        var vectors = await provider.EmbedBatchAsync(["first", "second"]);

        Assert.Equal(1.0f, vectors[0][0], 5);
        Assert.Equal(2.0f, vectors[1][0], 5);
    }

    /// <summary>A batch that comes back the wrong length fails rather than silently misaligning.</summary>
    [Fact]
    public async Task ShortOpenAiShapedBatchIsRejected()
    {
        var handler = new StubHandler("""{"data":[{"index":0,"embedding":[1.0]}]}""");
        var provider = new OpenAICompatibleEmbeddingProvider(Client(handler), "Voyage", "voyage-3.5", 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedBatchAsync(["first", "second"]));
    }

    /// <summary>Gemini's batchEmbedContents response is read into vectors.</summary>
    [Fact]
    public async Task GeminiReadsBatchEmbedContentsResponse()
    {
        var handler = new StubHandler("""{"embeddings":[{"values":[0.1,0.2]},{"values":[0.3,0.4]}]}""");
        var provider = new GoogleGeminiEmbeddingProvider(Client(handler), dimensions: 2);

        var vectors = await provider.EmbedBatchAsync(["alpha", "beta"]);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(0.4f, vectors[1][1], 5);
    }

    /// <summary>Gemini qualifies the model name and selects a retrieval task type per call.</summary>
    [Fact]
    public async Task GeminiQualifiesTheModelAndSetsTheTaskType()
    {
        var handler = new StubHandler("""{"embeddings":[{"values":[0.1]}]}""");
        var provider = new GoogleGeminiEmbeddingProvider(Client(handler), dimensions: 1);

        await provider.EmbedAsync("a document");
        using (var body = JsonDocument.Parse(handler.LastBody!))
        {
            var request = body.RootElement.GetProperty("requests")[0];
            Assert.Equal("models/gemini-embedding-001", request.GetProperty("model").GetString());
            Assert.Equal("RETRIEVAL_DOCUMENT", request.GetProperty("taskType").GetString());
        }

        await provider.EmbedQueryAsync("a query");
        using (var body = JsonDocument.Parse(handler.LastBody!))
        {
            Assert.Equal("RETRIEVAL_QUERY", body.RootElement.GetProperty("requests")[0].GetProperty("taskType").GetString());
        }
    }

    /// <summary>The Gemini API key travels in a header, not in the request URI.</summary>
    [Fact]
    public async Task GeminiSendsTheApiKeyInAHeaderNotTheUri()
    {
        var handler = new StubHandler("""{"embeddings":[{"values":[0.1]}]}""");
        using var provider = new GoogleGeminiEmbeddingProvider("secret-key", dimensions: 1);

        // The public constructor owns its HttpClient, so assert on the header the provider sets.
        var client = Client(handler);
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", "secret-key");
        using var headerProvider = new GoogleGeminiEmbeddingProvider(client, dimensions: 1);

        await headerProvider.EmbedAsync("text");

        Assert.Equal("secret-key", handler.LastApiKeyHeader);
        Assert.DoesNotContain("secret-key", handler.LastRequestUri!.ToString());
    }

    /// <summary>An empty batch short-circuits without an HTTP call.</summary>
    [Fact]
    public async Task EmptyBatchMakesNoCall()
    {
        var handler = new StubHandler("""{"data":[]}""");
        var provider = new OpenAICompatibleEmbeddingProvider(Client(handler), "Voyage", "voyage-3.5", 1);

        Assert.Empty(await provider.EmbedBatchAsync([]));
        Assert.Equal(0, handler.CallCount);
    }

    private static HttpClient Client(StubHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.example.com/v1/") };

    private static string? ReadProperty(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string responseJson;

        public StubHandler(string responseJson) => this.responseJson = responseJson;

        public int CallCount { get; private set; }

        public string? LastBody { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastApiKeyHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.FirstOrDefault() : null;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
