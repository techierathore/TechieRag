using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Scheduling;
using TechieDesk.Services.Scheduling.Authoring;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// Natural-language authoring (REQ-UI-046 / BRD-140, ADR-010).
/// </summary>
/// <remarks>
/// ⚠ Every one of these runs against a FAKE model. No LLM provider is configured on the development
/// host, so the real interpretation loop has never been exercised end to end — what is proven here is
/// the validation and confirmation behaviour around it, which is the part that has to hold when the
/// model is wrong.
/// </remarks>
public sealed class ScheduleInterpreterTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>With no model configured the failure names the fix, not the instruction.</summary>
    [Fact]
    public async Task NoModelConfiguredSaysWhereToConfigureOne()
    {
        var interpreter = Build(provider: null, out _);

        var result = await interpreter.InterpretAsync("every weekday at 7, sync the mailbox");

        Assert.False(result.Succeeded);
        Assert.Contains("LLM settings", result.Error);
    }

    /// <summary>A well-formed interpretation becomes a savable draft.</summary>
    [Fact]
    public async Task AWellFormedInterpretationBecomesADraft()
    {
        var interpreter = Build(Payload(cron: "0 7 * * 1-5", summary: "Every weekday at 07:00"), out _);

        var result = await interpreter.InterpretAsync("every weekday at 7, sync the mailbox");

        Assert.True(result.Succeeded);
        Assert.Equal("0 7 * * 1-5", result.Draft!.CronExpression);
        Assert.Equal(DraftConfidence.High, result.Draft.Confidence);
        Assert.True(result.Draft.IsSavable);
    }

    /// <summary>
    /// The displayed sentence is computed from the expression, never quoted from the model — so a
    /// model that describes its own cron wrongly cannot get that wording confirmed.
    /// </summary>
    [Fact]
    public async Task TheDisplayedSentenceComesFromTheExpressionNotTheModel()
    {
        var interpreter = Build(Payload(cron: "0 7 * * 1-5", summary: "every morning"), out _);

        var result = await interpreter.InterpretAsync("every morning, sync the mailbox");

        Assert.Equal("Every weekday at 07:00", result.Draft!.ScheduleText);
        Assert.Contains(result.Draft.Warnings, warning => warning.Contains("every morning"));
        Assert.Equal(DraftConfidence.Medium, result.Draft.Confidence);
    }

    /// <summary>An unparseable expression produces no draft at all, with the parse error quoted.</summary>
    [Fact]
    public async Task AnUnparseableExpressionProducesNoDraft()
    {
        var interpreter = Build(Payload(cron: "every other tuesday"), out _);

        var result = await interpreter.InterpretAsync("every other Tuesday, sync the mailbox");

        Assert.False(result.Succeeded);
        Assert.Null(result.Draft);
        Assert.Contains("5 fields", result.Error);
    }

    /// <summary>
    /// An action the app does not expose is refused, and the refusal lists what it can do — the model
    /// selects, it never invents.
    /// </summary>
    [Fact]
    public async Task AnUnknownActionIsRefusedAndTheOptionsAreListed()
    {
        var interpreter = Build(Payload(jobKind: "SendSlackMessage"), out _);

        var result = await interpreter.InterpretAsync("post to Slack every hour");

        Assert.False(result.Succeeded);
        Assert.Contains("Test action", result.Error);
    }

    /// <summary>
    /// A payload the action rejects makes the draft unsavable at authoring time, not at first run
    /// (BRD-136).
    /// </summary>
    [Fact]
    public async Task AnInvalidPayloadIsCaughtBeforeSavingNotAtFirstRun()
    {
        var interpreter = Build(Payload(payload: """{"workspace":"Nope"}"""), out var handler);
        handler.PayloadError = "There is no workspace called 'Nope'.";

        var result = await interpreter.InterpretAsync("sync Nope every hour");

        Assert.True(result.Succeeded);
        Assert.Equal(DraftConfidence.Low, result.Draft!.Confidence);
        Assert.False(result.Draft.IsSavable);
        Assert.Contains(result.Draft.Warnings, warning => warning.Contains("no workspace called"));
    }

    /// <summary>
    /// The confirm panel gets the full understood result — the trigger, every step and the delivery
    /// line — never a one-line summary.
    /// </summary>
    [Fact]
    public async Task TheDraftCarriesTheFullUnderstoodResult()
    {
        var interpreter = Build(
            Payload(steps: ["Sync the Email connector into Contracts", "Flag renewals within 90 days"],
                delivery: "Notify me only if something is found"),
            out _);

        var result = await interpreter.InterpretAsync("weekdays at 7, sync and flag renewals");

        var labels = result.Draft!.Steps.Select(step => step.Label).ToList();
        Assert.Equal(["Runs", "Step 1", "Step 2", "Then"], labels);
        Assert.Equal("Notify me only if something is found", result.Draft.Steps[^1].Text);
    }

    /// <summary>The preview shows the next three runs, so a misread schedule is visible as dates.</summary>
    [Fact]
    public async Task TheDraftPreviewsTheNextThreeRuns()
    {
        var interpreter = Build(Payload(cron: "0 7 * * 1-5"), out _);

        var result = await interpreter.InterpretAsync("weekdays at 7");

        Assert.Equal(3, result.Draft!.NextRunsUtc.Count);
        Assert.All(result.Draft.NextRunsUtc, instant => Assert.True(instant > Now));
    }

    /// <summary>
    /// Editing the expression in the Advanced disclosure rewrites the displayed sentence and the
    /// preview, so the user re-confirms what will actually run.
    /// </summary>
    [Fact]
    public async Task EditingTheExpressionRewritesTheSentenceAndThePreview()
    {
        var interpreter = Build(Payload(cron: "0 7 * * 1-5"), out _);
        var original = (await interpreter.InterpretAsync("weekdays at 7")).Draft!;

        var rebuilt = interpreter.Rebuild(original, "0 3 * * *");

        Assert.True(rebuilt.Succeeded);
        Assert.Equal("Every day at 03:00", rebuilt.Draft!.ScheduleText);
        Assert.Equal("Every day at 03:00", rebuilt.Draft.Steps[0].Text);
        Assert.NotEqual(original.NextRunsUtc, rebuilt.Draft.NextRunsUtc);
    }

    /// <summary>An invalid expression typed into Advanced is refused with the parse error.</summary>
    [Fact]
    public async Task AnInvalidExpressionTypedIntoAdvancedIsRefused()
    {
        var interpreter = Build(Payload(), out _);
        var original = (await interpreter.InterpretAsync("weekdays at 7")).Draft!;

        var rebuilt = interpreter.Rebuild(original, "0 99 * * *");

        Assert.False(rebuilt.Succeeded);
        Assert.Contains("0-23", rebuilt.Error);
    }

    /// <summary>A model that throws is reported as a failed interpretation, not an unhandled error.</summary>
    [Fact]
    public async Task AModelThatThrowsIsReportedNotPropagated()
    {
        var provider = new FakeLlmProvider(_ => throw new HttpRequestException("connection refused"));
        var interpreter = Build(provider, out _);

        var result = await interpreter.InterpretAsync("weekdays at 7");

        Assert.False(result.Succeeded);
        Assert.Contains("connection refused", result.Error);
    }

    private static ScheduleInterpretationPayload Payload(
        string cron = "0 7 * * 1-5",
        string? summary = null,
        string jobKind = "Test",
        string? payload = null,
        List<string>? steps = null,
        string? delivery = null) => new()
    {
        Name = "Sync legal mailbox",
        Cron = cron,
        Summary = summary,
        JobKind = jobKind,
        Payload = payload,
        Steps = steps,
        Delivery = delivery
    };

    private static ScheduleInterpreter Build(
        ScheduleInterpretationPayload payload, out FakeJobHandler handler) =>
        Build(new FakeLlmProvider(_ => payload), out handler);

    private static ScheduleInterpreter Build(ILlmProvider? provider, out FakeJobHandler handler)
    {
        handler = new FakeJobHandler();
        var runner = new JobRunner(
            new FakeScheduleRunRepository(), [handler], new TestClock(Now), NullLogger<JobRunner>.Instance);
        return new ScheduleInterpreter(
            new FakeTechieRag(provider),
            runner,
            new TestClock(Now),
            NullLogger<ScheduleInterpreter>.Instance,
            SchedulingText.Localize);
    }

    /// <summary>An <see cref="ILlmProvider"/> that returns whatever the test decided.</summary>
    private sealed class FakeLlmProvider : ILlmProvider
    {
        private readonly Func<string, object> respond;

        public FakeLlmProvider(Func<string, object> respond) => this.respond = respond;

        public string Name => "Fake local model";

        public string ModelName => "fake";

        public bool SupportsToolCalling => false;

        public bool SupportsStreaming => false;

        public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

        public Task<T> CompleteAsync<T>(
            string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
            where T : class
        {
            OnCompletionCompleted?.Invoke(this, null!);
            return Task.FromResult((T)respond(prompt));
        }

        public Task<LlmResponse> CompleteAsync(
            string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> CompleteStreamAsync(
            string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            Empty(cancellationToken);

        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<ChatMessage> messages,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> ChatStreamAsync(
            IReadOnlyList<ChatMessage> messages,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => Empty(cancellationToken);

        public int EstimateTokenCount(string text) => text.Length / 4;

        private static async IAsyncEnumerable<string> Empty(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>An <see cref="ITechieRag"/> that only answers the one question the interpreter asks.</summary>
    private sealed class FakeTechieRag : ITechieRag
    {
        private readonly ILlmProvider? provider;

        public FakeTechieRag(ILlmProvider? provider) => this.provider = provider;

        public ILlmProvider? GetLlmProvider() => provider;

        public Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> IngestTextAsync(
            string text,
            string documentName,
            Dictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> IngestDirectoryAsync(
            string directoryPath,
            string searchPattern = "*.*",
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int topK = 5,
            string? documentFilter = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<RagResponse> AskAsync(
            string question,
            int topK = 5,
            string? systemPrompt = null,
            string? documentFilter = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> AskStreamAsync(
            string question,
            int topK = 5,
            string? systemPrompt = null,
            string? documentFilter = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RagResponse> ChatWithRagAsync(
            string userMessage,
            IReadOnlyList<ChatMessage>? conversationHistory = null,
            int topK = 5,
            string? systemPrompt = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> ChatWithRagStreamAsync(
            string userMessage,
            IReadOnlyList<ChatMessage>? conversationHistory = null,
            int topK = 5,
            string? systemPrompt = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ITokenTracker GetTokenTracker() => throw new NotSupportedException();

        public IConversationMemory? GetConversationMemory() => null;
    }
}
