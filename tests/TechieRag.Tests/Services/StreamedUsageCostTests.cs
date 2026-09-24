using TechieRag.Llm;
using TechieRag.Tests.Llm;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// REQ-RAG-104 / BRD-152: a streamed completion on an instance with a pricing table set records the
/// tokens the stream reported and the cost that table gives for them.
/// </summary>
/// <remarks>
/// The provider is the real <see cref="OpenAICompatibleLlmProvider"/> over a stub handler that answers
/// OpenAI's recorded stream shape, whose final chunk reports 30 prompt and 4 completion tokens. The
/// usage reaches the tracker the way it does in an application: through the instance built by
/// <see cref="TechieRagBuilder"/>, not by calling the tracker directly.
/// </remarks>
public sealed class StreamedUsageCostTests : IDisposable
{
    private readonly string vectorPath = Path.Combine(Path.GetTempPath(), $"trstreamcost-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Streaming an answer with <c>WithModelPricing</c> set to 1,000,000 USD per million input tokens
    /// and 2,000,000 per million output tokens records one operation of 30 in and 4 out, costing
    /// 30 + 8 = 38 USD.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-104 StreamedTokensAreRecordedAndPriced")]
    public async Task StreamedTokensAreRecordedAndPriced()
    {
        var handler = new CapturingHandler(TypedStreamingTests.OpenAIAnswerStream);
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec(vectorPath)
            .UseCustomLlmProvider(() => new OpenAICompatibleLlmProvider(
                new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") }, "stream-priced-model"))
            .WithUsageTracking()
            .WithModelPricing("stream-priced-model", 1_000_000m, 2_000_000m)
            .Build();
        await rag.InitializeAsync();

        var answer = new System.Text.StringBuilder();
        await foreach (var token in rag.AskStreamAsync("What is the weather?"))
        {
            answer.Append(token);
        }

        var usage = rag.GetTokenTracker().GetSessionUsage();
        Assert.Equal("Sunny, 22 C.", answer.ToString());
        Assert.Equal(1, usage.OperationCount);
        Assert.Equal(30, usage.TotalInputTokens);
        Assert.Equal(4, usage.TotalOutputTokens);
        Assert.Equal(38m, usage.TotalEstimatedCostUsd);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(vectorPath)) File.Delete(vectorPath);
    }
}
