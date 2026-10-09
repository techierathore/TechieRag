using System.Runtime.CompilerServices;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// The small-model chooser picks one candidate model for a request and says why (REQ-RAG-128,
/// Chatur TR-RAG-006).
/// </summary>
public sealed class ModelChooserTests
{
    private static readonly ModelCandidate Fast = new() { ModelId = "ollama/llama3.2", Tier = "fast", Cost = "free, local", Notes = "short answers" };
    private static readonly ModelCandidate Deep = new() { ModelId = "anthropic/claude-sonnet-5-5", Tier = "deep", Cost = "paid", Notes = "long reasoning and code" };

    /// <summary>
    /// The small model is asked once, at temperature 0 in JSON mode, with every candidate's id, tier,
    /// cost and notes and the request; the candidate it names comes back with its reason and usage.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-128 SmallModelChoiceIsReturnedWithReason")]
    public async Task SmallModelChoiceIsReturnedWithReason()
    {
        var model = new ReplyingLlm("""{"model":"anthropic/claude-sonnet-5-5","reason":"The request asks for a code review."}""");
        var chooser = new ModelChooser(model);

        var choice = await chooser.ChooseAsync("Review this C# class for bugs.", [Fast, Deep]);

        Assert.Same(Deep, choice.Candidate);
        Assert.Equal("anthropic/claude-sonnet-5-5", choice.ModelId);
        Assert.Equal("The request asks for a code review.", choice.Reason);
        Assert.Equal(ModelChoiceSource.SmallModel, choice.Source);
        Assert.Equal(12, choice.Usage!.InputTokens);

        var call = Assert.Single(model.Calls);
        Assert.Equal(0f, call.Options!.Temperature);
        Assert.True(call.Options.JsonMode);
        var prompt = call.Messages[^1].Content!;
        Assert.Contains("id: ollama/llama3.2; tier: fast; cost: free, local; notes: short answers", prompt, StringComparison.Ordinal);
        Assert.Contains("id: anthropic/claude-sonnet-5-5; tier: deep", prompt, StringComparison.Ordinal);
        Assert.Contains("Review this C# class for bugs.", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reply wrapped in prose or a code fence, with the id in another case, still matches its
    /// candidate, and the host's guidance reaches the small model's instructions.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-128 WrappedReplyMatchesCandidateIgnoringCase")]
    public async Task WrappedReplyMatchesCandidateIgnoringCase()
    {
        var model = new ReplyingLlm("Sure.\n```json\n{\"modelId\": \"OLLAMA/Llama3.2\", \"reason\": \"A greeting.\"}\n```");
        var chooser = new ModelChooser(model, new ModelChooserOptions { Guidance = "Prefer free models." });

        var choice = await chooser.ChooseAsync("hello", [Deep, Fast]);

        Assert.Same(Fast, choice.Candidate);
        Assert.Equal(ModelChoiceSource.SmallModel, choice.Source);
        Assert.Contains("Prefer free models.", Assert.Single(model.Calls).Messages[0].Content!, StringComparison.Ordinal);
    }

    /// <summary>
    /// An id without its connector prefix (as a 0.5B model answered in the live smoke) matches the one
    /// candidate ending with it; when two candidates end with it, the fallback is used instead of a guess.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-128 IdWithoutConnectorMatchesOnlyWhenUnambiguous")]
    public async Task IdWithoutConnectorMatchesOnlyWhenUnambiguous()
    {
        var reply = """{"model":"llama3.2","reason":"A greeting."}""";

        var single = await new ModelChooser(new ReplyingLlm(reply)).ChooseAsync("hi", [Deep, Fast]);
        Assert.Same(Fast, single.Candidate);
        Assert.Equal(ModelChoiceSource.SmallModel, single.Source);

        var groq = new ModelCandidate { ModelId = "groq/llama3.2" };
        var ambiguous = await new ModelChooser(new ReplyingLlm(reply)).ChooseAsync("hi", [Deep, Fast, groq]);
        Assert.Same(Deep, ambiguous.Candidate);
        Assert.Equal(ModelChoiceSource.Fallback, ambiguous.Source);
    }

    /// <summary>
    /// A reply naming a model that was not offered, or not JSON at all, gives the fallback candidate
    /// (the configured one, else the first) and a reason saying so; the answer is always a candidate.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-128 UnknownOrBrokenReplyUsesFallback")]
    public async Task UnknownOrBrokenReplyUsesFallback()
    {
        var unknown = await new ModelChooser(new ReplyingLlm("""{"model":"openai/gpt-9","reason":"x"}"""), new ModelChooserOptions { FallbackModelId = "anthropic/claude-sonnet-5-5" })
            .ChooseAsync("plan a trip", [Fast, Deep]);
        Assert.Same(Deep, unknown.Candidate);
        Assert.Equal(ModelChoiceSource.Fallback, unknown.Source);
        Assert.Contains("'openai/gpt-9'", unknown.Reason, StringComparison.Ordinal);

        var broken = await new ModelChooser(new ReplyingLlm("I would pick the fast one."))
            .ChooseAsync("plan a trip", [Fast, Deep]);
        Assert.Same(Fast, broken.Candidate);
        Assert.Equal(ModelChoiceSource.Fallback, broken.Source);
    }

    /// <summary>
    /// One candidate is returned without asking any model; a long request is cut to the configured
    /// length before it reaches the small model.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-128 SingleCandidateSkipsModelAndLongRequestIsCut")]
    public async Task SingleCandidateSkipsModelAndLongRequestIsCut()
    {
        var model = new ReplyingLlm("""{"model":"ollama/llama3.2","reason":"Short."}""");
        var chooser = new ModelChooser(model, new ModelChooserOptions { MaxRequestCharacters = 10 });

        var only = await chooser.ChooseAsync("anything", [Fast]);
        Assert.Equal(ModelChoiceSource.OnlyCandidate, only.Source);
        Assert.Null(only.Usage);
        Assert.Empty(model.Calls);

        await chooser.ChooseAsync(new string('a', 50) + "TAIL", [Fast, Deep]);
        var prompt = Assert.Single(model.Calls).Messages[^1].Content!;
        Assert.DoesNotContain("TAIL", prompt, StringComparison.Ordinal);
        Assert.Contains("[cut: 44 more characters]", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bad input is refused before any call: a blank request, no candidates, a blank id, a repeated
    /// id. A failure of the small model itself is thrown, never turned into a choice.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-128 BadInputAndModelFailureThrow")]
    public async Task BadInputAndModelFailureThrow()
    {
        var model = new ReplyingLlm("{}");
        IModelChooser chooser = new ModelChooser(model);

        await Assert.ThrowsAsync<ArgumentException>(() => chooser.ChooseAsync(" ", [Fast]));
        await Assert.ThrowsAsync<ArgumentException>(() => chooser.ChooseAsync("x", []));
        await Assert.ThrowsAsync<ArgumentException>(() => chooser.ChooseAsync("x", [Fast, new ModelCandidate { ModelId = " " }]));
        await Assert.ThrowsAsync<ArgumentException>(() => chooser.ChooseAsync("x", [Fast, Fast with { Tier = "other" }]));
        Assert.Empty(model.Calls);

        var failing = new ModelChooser(new ReplyingLlm(new HttpRequestException("down")));
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.ChooseAsync("x", [Fast, Deep]));
    }

    /// <summary>A chat-only fake that answers every call with one reply (or throws) and records the calls.</summary>
    private sealed class ReplyingLlm : ILlmProvider
    {
        private readonly string? reply;
        private readonly Exception? failure;

        /// <summary>Answers with the given text.</summary>
        /// <param name="reply">The reply text.</param>
        public ReplyingLlm(string reply) => this.reply = reply;

        /// <summary>Throws the given exception on every call.</summary>
        /// <param name="failure">The exception.</param>
        public ReplyingLlm(Exception failure) => this.failure = failure;

        /// <inheritdoc/>
        public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted { add { } remove { } }

        /// <summary>Gets the chat calls seen.</summary>
        public List<(IReadOnlyList<ChatMessage> Messages, LlmCompletionOptions? Options)> Calls { get; } = [];

        /// <inheritdoc/>
        public string Name => "fake";

        /// <inheritdoc/>
        public string ModelName => "small";

        /// <inheritdoc/>
        public bool SupportsToolCalling => false;

        /// <inheritdoc/>
        public bool SupportsStreaming => false;

        /// <inheritdoc/>
        public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((messages, options));
            if (failure is not null) throw failure;
            return Task.FromResult(new LlmResponse { Content = reply, Usage = new TokenUsage { InputTokens = 12, OutputTokens = 9 } });
        }

        /// <inheritdoc/>
        public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new NotSupportedException();
#pragma warning disable CS0162 // An iterator needs a yield.
            yield break;
#pragma warning restore CS0162
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new NotSupportedException();
#pragma warning disable CS0162 // An iterator needs a yield.
            yield break;
#pragma warning restore CS0162
        }

        /// <inheritdoc/>
        public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        public int EstimateTokenCount(string text) => text.Length / 4;
    }
}
