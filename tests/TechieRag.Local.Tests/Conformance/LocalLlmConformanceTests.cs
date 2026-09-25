using System.Reflection;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// The behaviour every runtime underneath <see cref="LocalLlmProvider"/> must produce identically
/// (REQ-RAG-058 / BRD-97, REQ-RAG-059 / BRD-98, REQ-RAG-063 / BRD-103, REQ-RAG-065 / BRD-105).
/// </summary>
/// <remarks>
/// <para>Runtime-neutral on purpose: each test states something an app can observe through
/// <see cref="ILlmProvider"/> and nothing about how a runtime gets there, so the same suite runs
/// against the scripted runtime (<see cref="FakeRuntimeConformanceTests"/>) and against the real
/// engine with a real model (<see cref="OnnxGenAiConformanceTests"/>, gated live through
/// <see cref="RealRuntimeAttribute"/>) — one subclass per runtime.</para>
/// <para>A subclass supplies the provider and three hooks: an answer to expect for a JSON request,
/// a long answer for the length and cancellation tests, and the exact token count of a text.</para>
/// </remarks>
public abstract class LocalLlmConformanceTests
{
    /// <summary>A prompt long enough to overflow a 64-token context in any tokenizer.</summary>
    private static readonly string OverLongPrompt = string.Join(' ', Enumerable.Repeat("lighthouse keeper", 200));

    /// <summary>The fixed strings token counts are checked on.</summary>
    public static readonly string[] TokenCountSamples =
    [
        "The quick brown fox jumps over the lazy dog.",
        "TechieRag runs a small language model inside the app.",
        "Lighthouses guided ships for more than two thousand years."
    ];

    /// <summary>Creates the provider under test.</summary>
    /// <param name="configure">Optional settings.</param>
    /// <returns>The provider.</returns>
    private protected abstract LocalLlmProvider CreateProvider(Action<LocalLlmOptions>? configure = null);

    /// <summary>Arranges for the next JSON request to be answered with this JSON (scripted runtimes only).</summary>
    /// <param name="json">The answer.</param>
    private protected abstract void PrepareJsonAnswer(string json);

    /// <summary>Arranges for the next request to get a long answer (scripted runtimes only).</summary>
    private protected abstract void PrepareLongAnswer();

    /// <summary>The runtime tokenizer's count for a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The count.</returns>
    private protected abstract int ExpectedTokenCount(string text);

    /// <summary>Asserts that no generation reached the runtime.</summary>
    private protected abstract void AssertNoInference();

    /// <summary>
    /// A chat call answers with text, a finish reason, the model's name and usage from the model's
    /// own tokenizer for both prompt and answer.
    /// </summary>
    [ConformanceFact(DisplayName = "REQ-RAG-057 ChatAnswersWithUsage")]
    public async Task ChatAnswersWithUsage()
    {
        using var provider = CreateProvider();

        var response = await provider.ChatAsync([ChatMessage.User("Say hello.")], new LlmCompletionOptions { Temperature = 0f });

        Assert.False(string.IsNullOrWhiteSpace(response.Content));
        Assert.Equal(provider.ModelName, response.ModelName);
        Assert.True(response.Usage.InputTokens > 0 && response.Usage.OutputTokens > 0);
    }

    /// <summary>
    /// The typed stream is text deltas, then exactly one completed event, last, carrying usage, a
    /// finish reason and the model name (the BRD-110 contract).
    /// </summary>
    [ConformanceFact(DisplayName = "REQ-RAG-059 StreamEventsArriveInContractOrder")]
    public async Task StreamEventsArriveInContractOrder()
    {
        using var provider = CreateProvider();
        var events = new List<LlmStreamEvent>();

        await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User("Say hello.")]))
        {
            events.Add(streamEvent);
        }

        Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
        Assert.Equal(LlmStreamEventKind.Completed, events[^1].Kind);
        Assert.All(events[..^1], e => Assert.Equal(LlmStreamEventKind.TextDelta, e.Kind));
        Assert.NotNull(events[^1].Usage);
        Assert.Equal(provider.ModelName, events[^1].ModelName);
        Assert.Contains(events[^1].FinishReason, new[] { "stop", "length" });
    }

    /// <summary>
    /// The text stream is the text of the typed stream, and both equal the non-streamed answer at
    /// temperature 0.
    /// </summary>
    [ConformanceFact]
    public async Task TextStreamMatchesChatAnswer()
    {
        using var provider = CreateProvider();
        var greedy = new LlmCompletionOptions { Temperature = 0f, Seed = 7, MaxTokens = 24 };
        var messages = new[] { ChatMessage.User("Name one colour.") };

        var chat = await provider.ChatAsync(messages, greedy);
        var streamed = string.Concat(await provider.ChatStreamAsync(messages, greedy).ToListAsync());

        Assert.Equal(chat.Content, streamed);
    }

    /// <summary>The single-prompt methods answer the same way as the chat methods.</summary>
    [ConformanceFact]
    public async Task CompleteStreamMatchesComplete()
    {
        using var provider = CreateProvider();
        var greedy = new LlmCompletionOptions { Temperature = 0f, Seed = 7, MaxTokens = 24 };

        var complete = await provider.CompleteAsync("Name one colour.", greedy);
        var streamed = string.Concat(await provider.CompleteStreamAsync("Name one colour.", greedy).ToListAsync());

        Assert.Equal(complete.Content, streamed);
    }

    /// <summary>
    /// A prompt longer than the model's context throws a clear error naming both lengths, before any
    /// inference runs.
    /// </summary>
    [ConformanceFact(DisplayName = "REQ-RAG-059 OverLongPromptThrowsBeforeInference")]
    public async Task OverLongPromptThrowsBeforeInference()
    {
        using var provider = CreateProvider(o => o.ContextSize = 64);

        var error = await Assert.ThrowsAsync<LocalPromptTooLongException>(
            () => provider.CompleteAsync(OverLongPrompt));

        Assert.Equal(64, error.ContextTokens);
        Assert.True(error.PromptTokens > 64);
        AssertNoInference();
    }

    /// <summary>MaxTokens bounds the answer and the finish reason says the length ran out.</summary>
    [ConformanceFact]
    public async Task MaxTokensLimitsTheAnswer()
    {
        using var provider = CreateProvider();
        PrepareLongAnswer();

        var response = await provider.CompleteAsync("Count from one to two hundred.", new LlmCompletionOptions { MaxTokens = 4, Temperature = 0f });

        Assert.Equal(4, response.Usage.OutputTokens);
        Assert.Equal("length", response.FinishReason);
    }

    /// <summary>Cancelling the token mid-stream stops generation with an OperationCanceledException.</summary>
    [ConformanceFact]
    public async Task CancellationStopsTheStream()
    {
        using var provider = CreateProvider();
        PrepareLongAnswer();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in provider.ChatStreamAsync([ChatMessage.User("Count from one to two hundred.")], null, cancellation.Token))
            {
                await cancellation.CancelAsync();
            }
        });
    }

    /// <summary>CompleteAsync&lt;T&gt; parses a flat object schema into T.</summary>
    [ConformanceFact(DisplayName = "REQ-RAG-063 TypedAnswerParsesFlatSchema")]
    public async Task TypedAnswerParsesFlatSchema()
    {
        using var provider = CreateProvider();
        PrepareJsonAnswer("""{"name":"Ada Lovelace","age":36}""");

        var person = await provider.CompleteAsync<PersonAnswer>("Give the name and age at death of Ada Lovelace.");

        Assert.False(string.IsNullOrWhiteSpace(person.Name));
        Assert.True(person.Age > 0);
    }

    /// <summary>CompleteAsync&lt;T&gt; parses a schema with a nested object into T.</summary>
    [ConformanceFact(DisplayName = "REQ-RAG-063 TypedAnswerParsesNestedSchema")]
    public async Task TypedAnswerParsesNestedSchema()
    {
        using var provider = CreateProvider();
        PrepareJsonAnswer("""{"city":"Paris","location":{"country":"France","continent":"Europe"}}""");

        var place = await provider.CompleteAsync<PlaceAnswer>("Where is Paris? Give the city, country and continent.");

        Assert.False(string.IsNullOrWhiteSpace(place.City));
        Assert.NotNull(place.Location);
        Assert.False(string.IsNullOrWhiteSpace(place.Location.Country));
    }

    /// <summary>CompleteAsync&lt;T&gt; parses a schema with an array and a boolean into T.</summary>
    [ConformanceFact(DisplayName = "REQ-RAG-063 TypedAnswerParsesListSchema")]
    public async Task TypedAnswerParsesListSchema()
    {
        using var provider = CreateProvider();
        PrepareJsonAnswer("""{"colours":["red","green","blue"],"complete":true}""");

        var list = await provider.CompleteAsync<ColourListAnswer>("List the three primary colours of light.");

        Assert.NotEmpty(list.Colours);
    }

    /// <summary>EstimateTokenCount equals the runtime tokenizer's count on the fixed test strings.</summary>
    [ConformanceFact(DisplayName = "REQ-RAG-065 EstimateTokenCountUsesModelTokenizer")]
    public async Task EstimateTokenCountUsesModelTokenizer()
    {
        using var provider = CreateProvider();
        await provider.LoadAsync();

        foreach (var sample in TokenCountSamples)
        {
            Assert.Equal(ExpectedTokenCount(sample), provider.EstimateTokenCount(sample));
        }
    }

    /// <summary>OnCompletionCompleted fires once per call with the call's usage.</summary>
    [ConformanceFact]
    public async Task CompletionEventRaisedOncePerCall()
    {
        using var provider = CreateProvider();
        var raised = new List<LlmCompletionEventArgs>();
        provider.OnCompletionCompleted += (_, args) => raised.Add(args);

        var response = await provider.CompleteAsync("Say hello.");

        var single = Assert.Single(raised);
        Assert.Equal(response.Usage.OutputTokens, single.OutputTokens);
        Assert.Equal("Local", single.ProviderName);
    }

    /// <summary>Tool calling reports false, and a request carrying tools is refused rather than silently ignored.</summary>
    [ConformanceFact(DisplayName = "REQ-RAG-063 ToolsAreRefused")]
    public async Task ToolsAreRefused()
    {
        using var provider = CreateProvider();
        var tools = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition { Name = "lookup", Description = "Look something up.", ParametersSchema = "{}" }]
        };

        Assert.False(provider.SupportsToolCalling);
        await Assert.ThrowsAsync<NotSupportedException>(() => provider.ChatAsync([ChatMessage.User("Hi")], tools));
    }

    /// <summary>
    /// The app cannot observe which runtime is in use: the provider's name is "Local" and no public
    /// member exposes a runtime type or name.
    /// </summary>
    [ConformanceFact(DisplayName = "REQ-RAG-058 RuntimeIsNotObservable")]
    public void RuntimeIsNotObservable()
    {
        using var provider = CreateProvider();
        var publicTypes = typeof(LocalLlmProvider).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SelectMany(TypesOf)
            .ToList();

        Assert.Equal("Local", provider.Name);
        Assert.DoesNotContain(publicTypes, t => t.Namespace == "TechieRag.Local.Runtime");
        Assert.DoesNotContain(publicTypes, t => t.Name.Contains("Llama", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Onnx", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member) => member switch
    {
        PropertyInfo property => [property.PropertyType],
        MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
        FieldInfo field => [field.FieldType],
        EventInfo @event => [@event.EventHandlerType!],
        _ => []
    };

    /// <summary>Flat schema: a name and an age.</summary>
    public sealed class PersonAnswer
    {
        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the age.</summary>
        public int Age { get; set; }
    }

    /// <summary>Nested schema: a city and its location.</summary>
    public sealed class PlaceAnswer
    {
        /// <summary>Gets or sets the city.</summary>
        public string City { get; set; } = string.Empty;

        /// <summary>Gets or sets the location.</summary>
        public LocationAnswer Location { get; set; } = new();
    }

    /// <summary>The nested part of <see cref="PlaceAnswer"/>.</summary>
    public sealed class LocationAnswer
    {
        /// <summary>Gets or sets the country.</summary>
        public string Country { get; set; } = string.Empty;

        /// <summary>Gets or sets the continent.</summary>
        public string Continent { get; set; } = string.Empty;
    }

    /// <summary>Array schema: a list and a flag.</summary>
    public sealed class ColourListAnswer
    {
        /// <summary>Gets or sets the colours.</summary>
        public List<string> Colours { get; set; } = [];

        /// <summary>Gets or sets whether the list is complete.</summary>
        public bool Complete { get; set; }
    }
}
