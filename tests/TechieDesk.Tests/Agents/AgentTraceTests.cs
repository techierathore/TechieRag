using TechieDesk.Services.Agents;
using TechieDesk.Tests.Support;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-UI-034 (BRD-85) — the agent execution trace rendered in chat. The library agent loop already
/// reports <see cref="AgentStep"/>s; what is pinned here is the product-side mapping of them into
/// renderable rows, including the case the Agents screen design calls out specifically: the step
/// where a tool came back empty and the agent moved on rather than inventing an answer.
/// </summary>
public class AgentTraceTests
{
    /// <summary>A trace that has seen nothing renders nothing — no empty panel in chat.</summary>
    [Fact]
    public void NewTraceIsEmpty()
    {
        var trace = new AgentTrace();

        Assert.True(trace.IsEmpty);
        Assert.Empty(trace.Entries);
        Assert.Equal(0, trace.ToolCallCount);
    }

    /// <summary>
    /// A tool execution renders the tool name, the arguments the model produced, and the result —
    /// the three things that make a trace auditable rather than decorative.
    /// </summary>
    [Fact]
    public void ToolExecutionRecordsNameArgumentsAndResult()
    {
        var trace = new AgentTrace();

        trace.Add(new AgentStep
        {
            Iteration = 1,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = SkillCatalog.RagSearch,
            ToolArgumentsJson = """{"query":"Acme liability cap"}""",
            Content = "5 chunks · top score 0.83"
        });

        using var resources = new ResourceHarness("en");
        var entry = Assert.Single(trace.Entries);
        Assert.Equal(1, entry.Step);

        // REQ-UI-051: the tool name IS the title and carries no key, because it is wire vocabulary.
        Assert.Null(entry.TitleKey);
        Assert.Equal(SkillCatalog.RagSearch, entry.Title(resources.Localize));
        Assert.Equal("""{"query":"Acme liability cap"}""", entry.ArgumentsJson);
        Assert.Equal("5 chunks · top score 0.83", entry.Detail);
        Assert.True(entry.IsSuccess);
        Assert.Equal(1, trace.ToolCallCount);
    }

    /// <summary>
    /// A tool that returns nothing says so in words. Rendering an empty box would read as a broken
    /// panel; the design's point is that "0 chunks above the threshold" is a real, honest step where
    /// the agent moved on rather than inventing an answer.
    /// </summary>
    [Fact]
    public void EmptyToolResultIsStatedNotBlank()
    {
        var trace = new AgentTrace();

        trace.Add(new AgentStep
        {
            Iteration = 1,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = SkillCatalog.RagSearch,
            ToolArgumentsJson = "{}",
            Content = "   "
        });

        using var resources = new ResourceHarness("en");

        Assert.Equal(AgentTrace.NoContentDetailKey, trace.Entries[0].DetailKey);
        Assert.Equal("(no content returned)", trace.Entries[0].DetailText(resources.Localize));
    }

    /// <summary>A failed tool renders its error and is marked unsuccessful, not silently dropped.</summary>
    [Fact]
    public void FailedToolIsRenderedAsAFailure()
    {
        var trace = new AgentTrace();

        trace.Add(new AgentStep
        {
            Iteration = 2,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = SkillCatalog.RagSearch,
            ToolArgumentsJson = "{}",
            IsSuccess = false,
            ErrorMessage = "The vector store is unreachable."
        });

        var entry = Assert.Single(trace.Entries);
        Assert.False(entry.IsSuccess);
        Assert.Equal("The vector store is unreachable.", entry.Detail);
    }

    /// <summary>
    /// Hitting the per-turn tool-call ceiling is shown as an unsuccessful terminal step, so an
    /// answer produced under a truncated search is never presented as a complete one.
    /// </summary>
    [Fact]
    public void ToolCallLimitIsReportedAsUnsuccessful()
    {
        var trace = new AgentTrace();

        trace.Add(new AgentStep
        {
            Iteration = 5,
            Kind = AgentStepKind.MaxIterationsReached,
            Content = "Partial answer"
        });

        var entry = Assert.Single(trace.Entries);
        Assert.False(entry.IsSuccess);
        using var resources = new ResourceHarness("en");
        Assert.Contains(
            "ceiling", entry.DetailText(resources.Localize), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A tool result the size of a document chunk is elided, because a trace that pushes the answer
    /// off the screen has stopped being a trace.
    /// </summary>
    [Fact]
    public void LongToolResultIsTruncated()
    {
        var trace = new AgentTrace();

        trace.Add(new AgentStep
        {
            Iteration = 1,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = SkillCatalog.RagSearch,
            ToolArgumentsJson = "{}",
            Content = new string('x', AgentTrace.MaxDetailLength + 500)
        });

        var detail = trace.Entries[0].Detail!;
        Assert.Equal(AgentTrace.MaxDetailLength + 1, detail.Length);
        Assert.EndsWith("…", detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Steps are numbered in arrival order and timed against the clock, so the panel can show the
    /// per-step duration the design asks for.
    /// </summary>
    [Fact]
    public void StepsAreNumberedAndTimedInOrder()
    {
        var ticks = new Queue<long>([0, 1200, 2100, 2400]);
        var trace = new AgentTrace(() => ticks.Dequeue());

        trace.Add(Executed(SkillCatalog.RagSearch));
        trace.Add(Executed(SkillCatalog.RagSearch));
        trace.Add(new AgentStep { Iteration = 2, Kind = AgentStepKind.FinalAnswer, Content = "Done" });

        Assert.Equal([1, 2, 3], trace.Entries.Select(e => e.Step));
        Assert.Equal([1200, 900, 300], trace.Entries.Select(e => e.ElapsedMilliseconds));
        Assert.Equal(2400, trace.TotalMilliseconds);
        Assert.Equal("1.2 s", trace.Entries[0].ElapsedLabel);
        Assert.Equal("300 ms", trace.Entries[2].ElapsedLabel);
    }

    /// <summary>
    /// The final answer terminates the trace as its own step, so a reader can see the loop actually
    /// finished rather than being cut off mid-way.
    /// </summary>
    [Fact]
    public void FinalAnswerTerminatesTheTrace()
    {
        var trace = new AgentTrace();

        trace.Add(new AgentStep { Iteration = 1, Kind = AgentStepKind.FinalAnswer, Content = "42" });

        using var resources = new ResourceHarness("en");
        var entry = Assert.Single(trace.Entries);
        Assert.Equal(AgentTrace.FinalAnswerTitleKey, entry.TitleKey);
        Assert.Equal("Final answer", entry.Title(resources.Localize));
        Assert.Null(entry.ArgumentsJson);
        Assert.Equal(0, trace.ToolCallCount);
    }

    /// <summary>
    /// The trace fills from an <see cref="IProgress{T}"/> sink — the seam the library agent loop
    /// reports through — SYNCHRONOUSLY and in report order. <see cref="Progress{T}"/> would post to
    /// a synchronization context and, on a thread-pool continuation, apply reports in no guaranteed
    /// order; a trace that reorders its own steps is worse than none, because it reads as a true
    /// account of a run that did not happen that way.
    /// </summary>
    [Fact]
    public void ProgressSinkFeedsTheTraceInOrder()
    {
        var trace = new AgentTrace();
        var renders = 0;
        var progress = trace.AsProgress(() => renders++);

        progress.Report(Executed("first"));
        progress.Report(Executed("second"));
        progress.Report(new AgentStep { Iteration = 2, Kind = AgentStepKind.FinalAnswer, Content = "done" });

        Assert.Equal(3, trace.Entries.Count);
        Assert.Equal(3, renders);
        using var resources = new ResourceHarness("en");
        Assert.Equal(
            ["first", "second", "Final answer"],
            trace.Entries.Select(entry => entry.Title(resources.Localize)));
    }

    /// <summary>The copy-trace text carries the step, the timing, the tool and its arguments.</summary>
    [Fact]
    public void PlainTextCarriesTheWholeStep()
    {
        var ticks = new Queue<long>([0, 500]);
        var trace = new AgentTrace(() => ticks.Dequeue());
        trace.Add(Executed(SkillCatalog.RagSearch));

        using var resources = new ResourceHarness("en");
        var text = trace.ToPlainText(resources.Localize);

        Assert.Contains("1.", text, StringComparison.Ordinal);
        Assert.Contains("500 ms", text, StringComparison.Ordinal);
        Assert.Contains(SkillCatalog.RagSearch, text, StringComparison.Ordinal);
        Assert.Contains("\"query\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-UI-051: every title and detail the trace produces resolves through the resources, in
    /// both languages, and no row carries an English sentence of its own.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The trace was rendered from <c>entry.Title</c> and <c>entry.Detail</c> straight out of the
    /// service, and both were English literals — invisible to both razor counters, because a
    /// service is not markup. This drives every step kind the mapping knows about, so a kind added
    /// later without a resource key fails here rather than on a Hindi user's screen.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryStepKindResolvesThroughTheResources(string culture)
    {
        using var resources = new ResourceHarness(culture);
        var trace = new AgentTrace(() => 0);

        foreach (var kind in Enum.GetValues<AgentStepKind>())
        {
            trace.Add(new AgentStep
            {
                Iteration = 1,
                Kind = kind,
                ToolName = SkillCatalog.RagSearch,
                Content = "result"
            });
        }

        Assert.Equal(Enum.GetValues<AgentStepKind>().Length, trace.Entries.Count);

        foreach (var entry in trace.Entries)
        {
            if (entry.TitleKey is not null)
            {
                Assert.DoesNotContain(' ', entry.TitleKey);
                Assert.NotEqual(
                    entry.TitleKey,
                    resources.Require(entry.TitleKey, [.. entry.TitleArguments]));
            }

            if (entry.DetailKey is not null)
            {
                Assert.DoesNotContain(' ', entry.DetailKey);
                Assert.NotEqual(entry.DetailKey, resources.Require(entry.DetailKey));
            }

            Assert.False(string.IsNullOrWhiteSpace(entry.Title(resources.Localize)));
        }
    }

    /// <summary>
    /// REQ-UI-051: the tool NAME never changes with the culture. It is what the model was handed
    /// and what the toggle tables store; a translated one names a tool that does not exist.
    /// </summary>
    [Fact]
    public void ToolNamesAreTheSameInEveryCulture()
    {
        string english;
        using (var resources = new ResourceHarness("en"))
        {
            var trace = new AgentTrace(() => 0);
            trace.Add(Executed(SkillCatalog.WebSearch));
            english = trace.Entries[0].Title(resources.Localize);
        }

        using (var resources = new ResourceHarness("hi"))
        {
            var trace = new AgentTrace(() => 0);
            trace.Add(Executed(SkillCatalog.WebSearch));

            Assert.Equal(SkillCatalog.WebSearch, english);
            Assert.Equal(english, trace.Entries[0].Title(resources.Localize));
        }
    }

    /// <summary>Builds a successful tool-execution step.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The step.</returns>
    private static AgentStep Executed(string tool) => new()
    {
        Iteration = 1,
        Kind = AgentStepKind.ToolExecuted,
        ToolName = tool,
        ToolArgumentsJson = """{"query":"caps"}""",
        Content = "ok"
    };
}
