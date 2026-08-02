using System.Diagnostics;
using TechieDesk.Services.Localization;
using TechieRag.Models;
using TechieRag.Orchestration;

namespace TechieDesk.Services.Agents;

/// <summary>
/// One rendered line of an agent execution trace (BRD-85 / REQ-UI-034).
/// </summary>
/// <param name="Step">The 1-based position in the trace.</param>
/// <param name="Iteration">The agent-loop iteration the step belongs to.</param>
/// <param name="DepthPrefix">The nesting marker shown before the title; empty for the outer run.</param>
/// <param name="TitleKey">
/// Resource key naming what happened, or null when the title IS the first title argument — a tool
/// name, which is wire vocabulary and is never translated.
/// </param>
/// <param name="TitleArguments">
/// The invariant runtime values substituted into <paramref name="TitleKey"/>: a tool name, a node
/// id, an edge id. Never prose.
/// </param>
/// <param name="ArgumentsJson">The tool-call arguments, when the step was a tool execution.</param>
/// <param name="DetailKey">Resource key for a fixed explanation, or null when there is none.</param>
/// <param name="Detail">
/// Runtime text shown under the title — a tool result, or an error the library reported. Null when
/// <paramref name="DetailKey"/> carries the explanation instead.
/// </param>
/// <param name="ElapsedMilliseconds">How long this step took, measured from the previous one.</param>
/// <param name="IsSuccess">False when the step failed or the loop was cut short.</param>
/// <remarks>
/// <para>
/// <b>REQ-UI-051 / BRD-91: why this is split into keys and arguments.</b> The trace panel used to
/// render <c>Title</c> and <c>Detail</c> straight out of this record, and both were built here in
/// English — "Tool-call limit reached", "Routed to …", "Final answer". A trace is the one surface
/// that has to be readable when something has gone wrong, so it is a poor place to be reading a
/// second language.
/// </para>
/// <para>
/// The split is not cosmetic. Every title is a TEMPLATE plus RUNTIME DATA, and only the template
/// is translatable: the node id, the edge id and the tool name are the flow author's own words and
/// the model's own vocabulary, and translating them would name something that does not exist. Two
/// separate fields is what makes that distinction impossible to get wrong.
/// </para>
/// </remarks>
public sealed record AgentTraceEntry(
    int Step,
    int Iteration,
    string DepthPrefix,
    string? TitleKey,
    IReadOnlyList<string> TitleArguments,
    string? ArgumentsJson,
    string? DetailKey,
    string? Detail,
    long ElapsedMilliseconds,
    bool IsSuccess)
{
    /// <summary>Gets the timing rendered the way the trace panel shows it.</summary>
    /// <remarks>
    /// <c>ms</c> and <c>s</c> are SI symbols rather than words and are left as they are, the same
    /// choice the rest of the app makes for <c>KB</c>/<c>MB</c>/<c>GB</c>. Flagged in the REQ-UI-051
    /// handback as a judgement a Hindi reviewer may want to overturn.
    /// </remarks>
    public string ElapsedLabel => ElapsedMilliseconds < 1000
        ? $"{ElapsedMilliseconds} ms"
        : $"{ElapsedMilliseconds / 1000d:0.0} s";

    /// <summary>Renders the title in the reader's language.</summary>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The depth marker followed by the resolved title.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="localize"/> is null.</exception>
    public string Title(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        // A null key means the title is a tool name reported by the model. There is nothing to
        // translate and nothing to look up — showing it verbatim is the whole point.
        var text = TitleKey is null
            ? TitleArguments.FirstOrDefault() ?? string.Empty
            : localize(TitleKey, [.. TitleArguments]);

        return DepthPrefix + text;
    }

    /// <summary>Renders the detail line in the reader's language.</summary>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The resolved explanation, the runtime text, or an empty string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="localize"/> is null.</exception>
    public string DetailText(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);
        return DetailKey is null ? Detail ?? string.Empty : localize(DetailKey);
    }
}

/// <summary>
/// Collects the library agent loop's <see cref="AgentStep"/> reports into a renderable execution
/// trace — what the agent did, with what arguments, and what came back (BRD-85 / REQ-UI-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>AgentLoopRunner</c> already reports steps through
/// <c>IProgress&lt;AgentStep&gt;</c>; what was missing was a product-side rendering of them. This
/// type owns the mapping and the timing so the chat surface only has to paint rows, and so the
/// mapping is unit-testable without rendering a page.</para>
/// <para><b>Timing is measured, not modelled.</b> <see cref="AgentStep"/> carries no duration, so
/// each entry records the wall-clock gap since the previous report. The clock is injectable purely
/// so the tests can assert on it.</para>
/// <para><b>Truncation:</b> tool results are frequently whole document chunks. The trace shows the
/// leading <see cref="MaxDetailLength"/> characters, because a trace that pushes the answer off the
/// screen stops being a trace.</para>
/// </remarks>
public sealed class AgentTrace
{
    /// <summary>The number of result characters a trace entry shows before eliding.</summary>
    public const int MaxDetailLength = 400;

    /// <summary>Resource key shown when a tool returned nothing at all.</summary>
    public const string NoContentDetailKey = "TraceDetailNoContent";

    /// <summary>Resource key for the fallback row: the model answered rather than calling a tool.</summary>
    public const string FinalAnswerTitleKey = "TraceTitleFinalAnswer";

    private readonly List<AgentTraceEntry> entries = new();
    private readonly Func<long> clock;
    private long lastTick;

    /// <summary>Creates a trace timed by a monotonic wall clock.</summary>
    public AgentTrace() : this(null)
    {
    }

    /// <summary>Creates a trace with an injectable millisecond clock.</summary>
    /// <param name="millisecondClock">
    /// A monotonically increasing millisecond source; null uses <see cref="Stopwatch"/>.
    /// </param>
    public AgentTrace(Func<long>? millisecondClock)
    {
        if (millisecondClock is null)
        {
            var stopwatch = Stopwatch.StartNew();
            clock = () => stopwatch.ElapsedMilliseconds;
        }
        else
        {
            clock = millisecondClock;
        }

        lastTick = clock();
    }

    /// <summary>Gets the trace entries, oldest first.</summary>
    public IReadOnlyList<AgentTraceEntry> Entries => entries;

    /// <summary>Gets whether nothing has been reported yet.</summary>
    public bool IsEmpty => entries.Count == 0;

    /// <summary>Gets how many individual tool executions the trace recorded.</summary>
    public int ToolCallCount => entries.Count(e => e.ArgumentsJson is not null);

    /// <summary>Gets the total measured duration of the run, in milliseconds.</summary>
    public long TotalMilliseconds => entries.Sum(e => e.ElapsedMilliseconds);

    /// <summary>
    /// Records one reported agent-loop step.
    /// </summary>
    /// <param name="step">The step reported by the library agent loop.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="step"/> is null.</exception>
    public void Add(AgentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        var now = clock();
        var elapsed = Math.Max(0, now - lastTick);
        lastTick = now;

        entries.Add(step.Kind switch
        {
            AgentStepKind.ToolCallRequested => Entry(
                step, elapsed,
                "TraceTitleToolRequested", [step.ToolName ?? string.Empty],
                argumentsJson: null,
                detail: ("TraceDetailToolRequested", null),
                isSuccess: true),

            // The title IS the tool name, so there is no key: a tool name is wire vocabulary handed
            // to the model, and a translated one would name a tool that does not exist.
            AgentStepKind.ToolExecuted => ToolExecuted(step, elapsed),

            AgentStepKind.MaxIterationsReached => Entry(
                step, elapsed,
                "TraceTitleToolLimitReached", [],
                argumentsJson: null,
                detail: ("TraceDetailIterationCeiling", null),
                isSuccess: false),

            // REQ-UI-040 / REQ-RAG-042: the seven flow step kinds. Each has its OWN arm, because the
            // fallback below renders anything it does not recognise as the final answer — so without
            // these, every node, every branch and every guardrail refusal in a flow run would be
            // labelled as the agent's answer. That is a trace that lies about what ran, which is the
            // one thing a trace must never do (REQ-FN-010, REQ-NFR-013 and REQ-FN-052 were each this
            // defect). The arms come BEFORE the fallback and there is a test that fails if any of the
            // seven falls through to it. REQ-UI-051 changed only the SPELLING of each label; which
            // kind maps to which label is untouched.
            AgentStepKind.NodeStarted => Node(
                step, elapsed, "TraceTitleNodeStarted", "TraceTitleStepStarted", isSuccess: true),

            AgentStepKind.NodeCompleted => Node(
                step, elapsed, "TraceTitleNodeFinished", "TraceTitleStepFinished", step.IsSuccess),

            AgentStepKind.RouteTaken => Routed(
                step, elapsed,
                "TraceTitleRouted", "TraceTitleRoutedVia", "TraceTitleRoutedOnward"),

            AgentStepKind.HandoffPerformed => Routed(
                step, elapsed,
                "TraceTitleHandedOff", "TraceTitleHandedOffVia", "TraceTitleHandedOffOnward"),

            // A guardrail refusal is a SUCCESSFUL check, but it is rendered as a failed row on
            // purpose: something the flow asked for did not happen, and a green row would read as
            // though it had.
            AgentStepKind.GuardrailBlocked => Blocked(step, elapsed),

            AgentStepKind.StepBudgetExhausted => Entry(
                step, elapsed,
                "TraceTitleStepBudgetExhausted", [],
                argumentsJson: null,
                DetailFor(step.ErrorMessage ?? step.Content),
                isSuccess: false),

            AgentStepKind.FlowCompleted => Entry(
                step, elapsed,
                "TraceTitleFlowCompleted", [],
                argumentsJson: null,
                DetailFor(step.Content),
                step.IsSuccess),

            _ => Entry(
                step, elapsed,
                FinalAnswerTitleKey, [],
                argumentsJson: null,
                DetailFor(step.Content),
                isSuccess: true)
        });
    }

    /// <summary>Builds the row for an executed tool.</summary>
    /// <param name="step">The reported step.</param>
    /// <param name="elapsed">Milliseconds since the previous report.</param>
    /// <returns>The trace entry.</returns>
    private AgentTraceEntry ToolExecuted(AgentStep step, long elapsed)
    {
        var named = !string.IsNullOrWhiteSpace(step.ToolName);
        var detail = step.IsSuccess
            ? DetailFor(step.Content)
            : step.ErrorMessage is { Length: > 0 } error
                ? (Key: (string?)null, Text: (string?)error)
                : (Key: "TraceDetailToolFailed", Text: (string?)null);

        return Entry(
            step, elapsed,
            named ? null : "TraceTitleUnnamedTool",
            named ? [step.ToolName!] : [],
            step.ToolArgumentsJson ?? "{}",
            detail,
            step.IsSuccess);
    }

    /// <summary>Builds the row for a node starting or finishing.</summary>
    /// <param name="step">The reported step.</param>
    /// <param name="elapsed">Milliseconds since the previous report.</param>
    /// <param name="namedKey">Key used when the step names the node; takes the name as <c>{0}</c>.</param>
    /// <param name="anonymousKey">Key used when it does not.</param>
    /// <param name="isSuccess">Whether the row reads as a success.</param>
    /// <returns>The trace entry.</returns>
    /// <remarks>
    /// The two keys exist because the neutral word that used to stand in for a missing node name —
    /// "step" — was itself English, concatenated into the title. A separate key keeps that sentence
    /// whole, which is also the only way a translator can word it naturally.
    /// </remarks>
    private AgentTraceEntry Node(
        AgentStep step, long elapsed, string namedKey, string anonymousKey, bool isSuccess)
    {
        var node = NodeName(step);
        var detail = isSuccess
            ? DetailFor(step.Content)
            : step.ErrorMessage is { Length: > 0 } error
                ? (Key: (string?)null, Text: (string?)error)
                : (Key: "TraceDetailStepFailed", Text: (string?)null);

        return Entry(
            step, elapsed,
            node is null ? anonymousKey : namedKey,
            node is null ? [] : [node],
            argumentsJson: null,
            detail,
            isSuccess);
    }

    /// <summary>Builds the row for a route or a handoff.</summary>
    /// <param name="step">The reported step.</param>
    /// <param name="elapsed">Milliseconds since the previous report.</param>
    /// <param name="targetKey">Key naming the destination, which is <c>{0}</c>.</param>
    /// <param name="viaKey">Key naming the destination and the edge, <c>{0}</c> and <c>{1}</c>.</param>
    /// <param name="onwardKey">Key used when the step names no destination at all.</param>
    /// <returns>The trace entry.</returns>
    private AgentTraceEntry Routed(
        AgentStep step, long elapsed, string targetKey, string viaKey, string onwardKey)
    {
        var flow = step as FlowStep;
        var target = FirstNonBlank(flow?.ToNodeId, flow?.NodeName);
        var edge = string.IsNullOrWhiteSpace(flow?.EdgeId) ? null : flow.EdgeId;

        var (key, arguments) = (target, edge) switch
        {
            (null, _) => (onwardKey, (IReadOnlyList<string>)[]),
            (_, null) => (targetKey, [target]),
            _ => (viaKey, [target, edge])
        };

        return Entry(step, elapsed, key, arguments, argumentsJson: null, DetailFor(step.Content), true);
    }

    /// <summary>Builds the row for a guardrail refusal.</summary>
    /// <param name="step">The reported step.</param>
    /// <param name="elapsed">Milliseconds since the previous report.</param>
    /// <returns>The trace entry.</returns>
    private AgentTraceEntry Blocked(AgentStep step, long elapsed)
    {
        var guardrail = step is FlowStep flow ? FirstNonBlank(flow.GuardrailId) : null;
        var detail = step.ErrorMessage is { Length: > 0 } error
            ? (Key: (string?)null, Text: (string?)error)
            : DetailFor(step.Content);

        return Entry(
            step, elapsed,
            guardrail is null ? "TraceTitleBlockedByGuardrail" : "TraceTitleBlocked",
            guardrail is null ? [] : [guardrail],
            argumentsJson: null,
            detail,
            isSuccess: false);
    }

    /// <summary>Builds one entry, numbering it and marking its nesting depth.</summary>
    /// <param name="step">The reported step.</param>
    /// <param name="elapsed">Milliseconds since the previous report.</param>
    /// <param name="titleKey">The title's resource key, or null to show the first argument raw.</param>
    /// <param name="titleArguments">Invariant runtime values for the title.</param>
    /// <param name="argumentsJson">The tool-call arguments, for a tool execution.</param>
    /// <param name="detail">The detail key and runtime text; exactly one is non-null.</param>
    /// <param name="isSuccess">Whether the row reads as a success.</param>
    /// <returns>The trace entry.</returns>
    private AgentTraceEntry Entry(
        AgentStep step,
        long elapsed,
        string? titleKey,
        IReadOnlyList<string> titleArguments,
        string? argumentsJson,
        (string? Key, string? Text) detail,
        bool isSuccess) =>
        new(entries.Count + 1,
            step.Iteration,
            DepthPrefix(step),
            titleKey,
            titleArguments,
            argumentsJson,
            detail.Key,
            detail.Text,
            elapsed,
            isSuccess);

    /// <summary>
    /// Marks a nested flow's row with its depth, so an inner run is visibly inner.
    /// </summary>
    /// <param name="step">The reported step.</param>
    /// <returns>A depth marker, or an empty string for the outer run.</returns>
    /// <remarks>
    /// A flow invoked as a tool by another agent reports at <c>Depth</c> 1 or more. Rendering its
    /// steps identically to the outer run's would read as one flat sequence that never happened in
    /// that order. The marker is a punctuation character, so it is carried outside the resource key
    /// rather than being baked into every translated title.
    /// </remarks>
    private static string DepthPrefix(AgentStep step) =>
        step is FlowStep { Depth: > 0 } nested
            ? new string('›', nested.Depth) + " "
            : string.Empty;

    /// <summary>Names the node a flow step happened in.</summary>
    /// <param name="step">The reported step.</param>
    /// <returns>The node's display name, its id, or null when the step names neither.</returns>
    private static string? NodeName(AgentStep step) => step is FlowStep flow
        ? FirstNonBlank(flow.NodeName, flow.NodeId)
        : null;

    /// <summary>Returns the first candidate that carries text.</summary>
    /// <param name="candidates">The candidates, in preference order.</param>
    /// <returns>The first non-blank candidate, or null when none carries text.</returns>
    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    /// <summary>Creates an <see cref="IProgress{T}"/> sink that feeds this trace.</summary>
    /// <param name="onStep">Optional callback raised after each step, for re-rendering.</param>
    /// <returns>A progress sink to hand to the library agent loop.</returns>
    /// <remarks>
    /// Deliberately NOT <see cref="Progress{T}"/>. That type posts to the captured synchronization
    /// context, so reports are applied asynchronously and — with no context, as on a thread-pool
    /// continuation — in no guaranteed order. A trace whose steps can arrive out of order is worse
    /// than no trace, because it reads as a true account of a run that did not happen that way.
    /// This sink appends inline on the reporting thread, so the recorded order is the run order and
    /// the timing gaps are real. The <paramref name="onStep"/> callback is where a UI marshals
    /// itself back to its own thread.
    /// </remarks>
    public IProgress<AgentStep> AsProgress(Action? onStep = null) =>
        new InlineProgress(step =>
        {
            Add(step);
            onStep?.Invoke();
        });

    /// <summary>Renders the trace as plain text, for the copy-trace affordance.</summary>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>One block per entry, in order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="localize"/> is null.</exception>
    /// <remarks>
    /// REQ-UI-051: this takes a localizer because what it produces is READ, not parsed — the user
    /// copies it into a support ticket or a chat message. Emitting the resource keys here would
    /// hand somebody a block of <c>TraceTitleRoutedVia</c> and call it a trace.
    /// </remarks>
    public string ToPlainText(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        return string.Join(Environment.NewLine, entries.Select(entry =>
        {
            var arguments = entry.ArgumentsJson is null ? string.Empty : $" {entry.ArgumentsJson}";
            var detail = entry.DetailText(localize);
            return $"{entry.Step}. [{entry.ElapsedLabel}] {entry.Title(localize)}{arguments}"
                + (detail.Length == 0 ? string.Empty : Environment.NewLine + "   " + detail);
        }));
    }

    /// <summary>
    /// Shortens a tool result for display, and says plainly when a tool returned nothing rather
    /// than rendering an empty box that reads like a rendering bug.
    /// </summary>
    /// <param name="content">The raw step content.</param>
    /// <returns>
    /// A resource key when there was no content, or the truncated runtime text. Exactly one of the
    /// two is non-null, which is what keeps "nothing came back" translatable while leaving the
    /// tool's own output — which nobody can translate — exactly as the tool produced it.
    /// </returns>
    private static (string? Key, string? Text) DetailFor(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (NoContentDetailKey, null);
        }

        var collapsed = content.Trim();
        return (null, collapsed.Length <= MaxDetailLength
            ? collapsed
            : collapsed[..MaxDetailLength] + "…");
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that runs its handler inline, preserving report order.
    /// </summary>
    /// <param name="handler">The handler invoked for each report.</param>
    private sealed class InlineProgress(Action<AgentStep> handler) : IProgress<AgentStep>
    {
        /// <inheritdoc />
        public void Report(AgentStep value) => handler(value);
    }
}
