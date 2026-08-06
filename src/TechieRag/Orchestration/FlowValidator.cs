using System.Text.RegularExpressions;

namespace TechieRag.Orchestration;

/// <summary>How seriously a <see cref="FlowValidationIssue"/> should be taken (REQ-RAG-042).</summary>
public enum FlowValidationSeverity
{
    /// <summary>Worth telling the author about; the flow still runs.</summary>
    Warning,

    /// <summary>The flow is not runnable. <see cref="FlowRunner"/> refuses to start it.</summary>
    Error
}

/// <summary>
/// One thing wrong with, or worth saying about, a flow (REQ-RAG-042).
/// </summary>
/// <param name="Severity">Whether this stops the flow running.</param>
/// <param name="Code">A stable machine-readable code from <see cref="FlowValidationCodes"/>, for localization and for tests.</param>
/// <param name="Message">A plain-English explanation for the author.</param>
/// <param name="NodeId">The node the issue is about, when it is about one.</param>
/// <param name="EdgeId">The edge the issue is about, when it is about one.</param>
/// <remarks>
/// The code, not the message, is the contract. A builder UI highlights by
/// <paramref name="NodeId"/> / <paramref name="EdgeId"/> and translates by <paramref name="Code"/>;
/// the English message is a fallback, not a parsing target.
/// </remarks>
public sealed record FlowValidationIssue(
    FlowValidationSeverity Severity,
    string Code,
    string Message,
    string? NodeId = null,
    string? EdgeId = null);

/// <summary>The stable issue codes <see cref="FlowValidator"/> reports (REQ-RAG-042).</summary>
public static class FlowValidationCodes
{
    /// <summary>The flow has no nodes.</summary>
    public const string EmptyFlow = "EmptyFlow";

    /// <summary>The flow's id or name is blank.</summary>
    public const string MissingIdentity = "MissingIdentity";

    /// <summary>Two nodes share an id.</summary>
    public const string DuplicateNodeId = "DuplicateNodeId";

    /// <summary>Two edges share an id.</summary>
    public const string DuplicateEdgeId = "DuplicateEdgeId";

    /// <summary>A node's id is blank.</summary>
    public const string BlankNodeId = "BlankNodeId";

    /// <summary>The declared start node does not exist.</summary>
    public const string UnknownStartNode = "UnknownStartNode";

    /// <summary>No start node is declared, so the entry point depends on list order.</summary>
    public const string ImplicitStartNode = "ImplicitStartNode";

    /// <summary>An edge references a node that does not exist.</summary>
    public const string DanglingEdge = "DanglingEdge";

    /// <summary>A non-terminal node has no outgoing edge and no handoff, so the flow ends there.</summary>
    public const string DeadEndNode = "DeadEndNode";

    /// <summary>A node cannot be reached from the start node.</summary>
    public const string UnreachableNode = "UnreachableNode";

    /// <summary>An agent node names no agent.</summary>
    public const string MissingAgentId = "MissingAgentId";

    /// <summary>A tool node names no tool.</summary>
    public const string MissingToolName = "MissingToolName";

    /// <summary>A handoff node has no handoff, or names no target.</summary>
    public const string MissingHandoffTarget = "MissingHandoffTarget";

    /// <summary>A handoff targets a node that does not exist.</summary>
    public const string UnknownHandoffTarget = "UnknownHandoffTarget";

    /// <summary>A handoff targets a node that is not an agent node.</summary>
    public const string HandoffTargetNotAgent = "HandoffTargetNotAgent";

    /// <summary>A property was set that this node kind ignores.</summary>
    public const string IrrelevantProperty = "IrrelevantProperty";

    /// <summary>The graph contains a cycle.</summary>
    public const string CycleDetected = "CycleDetected";

    /// <summary>The step budget is zero or negative, so nothing could run.</summary>
    public const string InvalidStepBudget = "InvalidStepBudget";

    /// <summary>A branching node has no unconditional edge, so an unmatched value ends the run.</summary>
    public const string NoDefaultBranch = "NoDefaultBranch";

    /// <summary>An unconditional edge is evaluated before a conditional one, making it unreachable.</summary>
    public const string UnreachableBranch = "UnreachableBranch";

    /// <summary>A condition's regular expression will not compile.</summary>
    public const string InvalidPattern = "InvalidPattern";

    /// <summary>A condition needs a source key and has none.</summary>
    public const string MissingConditionSourceKey = "MissingConditionSourceKey";

    /// <summary>The agent a node names cannot be resolved by this runtime.</summary>
    public const string UnresolvableAgent = "UnresolvableAgent";

    /// <summary>A guardrail a node names cannot be resolved by this runtime, so the node would be blocked.</summary>
    public const string UnresolvableGuardrail = "UnresolvableGuardrail";

    /// <summary>A tool node's tool is not offered by the runtime's tool handler.</summary>
    public const string UnresolvableTool = "UnresolvableTool";

    /// <summary>The runtime has no tool handler but the flow contains tool nodes.</summary>
    public const string NoToolHandler = "NoToolHandler";
}

/// <summary>
/// The result of validating a flow (REQ-RAG-042).
/// </summary>
public sealed class FlowValidationResult
{
    /// <summary>Creates a result over a set of issues.</summary>
    /// <param name="issues">Everything found, errors and warnings together.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="issues"/> is null.</exception>
    public FlowValidationResult(IEnumerable<FlowValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToList();
    }

    /// <summary>Gets everything found, in the order it was found.</summary>
    public IReadOnlyList<FlowValidationIssue> Issues { get; }

    /// <summary>Gets only the issues that stop the flow running.</summary>
    public IReadOnlyList<FlowValidationIssue> Errors =>
        Issues.Where(issue => issue.Severity == FlowValidationSeverity.Error).ToList();

    /// <summary>Gets only the advisory issues.</summary>
    public IReadOnlyList<FlowValidationIssue> Warnings =>
        Issues.Where(issue => issue.Severity == FlowValidationSeverity.Warning).ToList();

    /// <summary>Gets whether the flow is runnable — no errors, warnings permitted.</summary>
    public bool IsValid => !Issues.Any(issue => issue.Severity == FlowValidationSeverity.Error);
}

/// <summary>
/// Checks a flow before it runs — and, for a builder UI, on every edit (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Two passes, deliberately separate.</b> <see cref="Validate"/> is pure, synchronous and
/// offline: shape, references, routing, cycles. It is what a builder calls after each change,
/// hundreds of times, and it never touches a resolver or a network. <see cref="ValidateAsync"/> adds
/// the bindings — do the agents, guardrails and tools this flow names actually exist on THIS host —
/// which is a different question with a different lifetime, since a flow can be structurally perfect
/// and unrunnable on a machine that lacks one of its agents.</para>
/// <para><b>Cycles: refused at validation, and bounded at run time anyway.</b> With
/// <see cref="FlowDefinition.AllowCycles"/> false (the default) a cycle is an ERROR, so a builder
/// refuses to save one and <see cref="FlowRunner"/> refuses to start it. Setting the flag makes it a
/// warning, for the genuinely useful retry-and-refine loop. Either way
/// <see cref="FlowDefinition.MaxSteps"/> bounds the run, because validation only protects flows that
/// went through it — a row edited by hand, or written by an older version, did not.</para>
/// </remarks>
public static class FlowValidator
{
    /// <summary>
    /// Validates a flow's structure, without touching the host's bindings.
    /// </summary>
    /// <param name="flow">The flow to check.</param>
    /// <returns>Every issue found; <see cref="FlowValidationResult.IsValid"/> says whether it can run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flow"/> is null.</exception>
    /// <remarks>Pure and side-effect free: safe to call on every keystroke in an editor.</remarks>
    public static FlowValidationResult Validate(FlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var issues = new List<FlowValidationIssue>();

        ValidateIdentity(flow, issues);
        ValidateNodeIdentity(flow, issues);
        ValidateEdgeIdentity(flow, issues);
        ValidateStartNode(flow, issues);

        foreach (var node in flow.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Id)))
        {
            ValidateNode(flow, node, issues);
        }

        ValidateBranching(flow, issues);
        ValidateReachability(flow, issues);
        ValidateCycles(flow, issues);

        return new FlowValidationResult(issues);
    }

    /// <summary>
    /// Validates a flow's structure and then whether this host can actually bind it.
    /// </summary>
    /// <param name="flow">The flow to check.</param>
    /// <param name="runtime">The runtime the flow would run on.</param>
    /// <param name="cancellationToken">Token cancelled when the caller gives up.</param>
    /// <returns>The structural issues plus any unresolvable agent, guardrail or tool.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <remarks>
    /// An unresolvable guardrail is an ERROR rather than a warning: the node would be blocked at run
    /// time by the deny-by-default rule, so reporting it as advisory would describe a flow that
    /// cannot get past its first guarded node as merely imperfect.
    /// </remarks>
    public static async Task<FlowValidationResult> ValidateAsync(
        FlowDefinition flow, FlowRuntime runtime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(runtime);

        var issues = new List<FlowValidationIssue>(Validate(flow).Issues);

        foreach (var node in flow.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ValidateAgentBindingAsync(runtime, node, issues, cancellationToken).ConfigureAwait(false);
            await ValidateGuardrailBindingsAsync(runtime, node, issues, cancellationToken).ConfigureAwait(false);
            ValidateToolBinding(runtime, node, issues);
        }

        return new FlowValidationResult(issues);
    }

    /// <summary>Checks the flow's own identity and budget.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateIdentity(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(flow.Id) || string.IsNullOrWhiteSpace(flow.Name))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.MissingIdentity,
                "A flow needs both an id and a name."));
        }

        if (flow.Nodes.Count == 0)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.EmptyFlow,
                "A flow needs at least one node."));
        }

        if (flow.MaxSteps <= 0)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.InvalidStepBudget,
                $"MaxSteps is {flow.MaxSteps}; a run needs a budget of at least one node."));
        }
    }

    /// <summary>Checks that node ids exist and are unique.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateNodeIdentity(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        foreach (var node in flow.Nodes.Where(node => string.IsNullOrWhiteSpace(node.Id)))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.BlankNodeId,
                $"A {node.Kind} node has a blank id, so no edge can reference it."));
        }

        foreach (var duplicate in flow.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.DuplicateNodeId,
                $"Node id '{duplicate.Key}' is used {duplicate.Count()} times; ids must be unique.",
                duplicate.Key));
        }
    }

    /// <summary>Checks that edge ids are unique and that both endpoints exist.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateEdgeIdentity(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        foreach (var duplicate in flow.Edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
            .GroupBy(edge => edge.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.DuplicateEdgeId,
                $"Edge id '{duplicate.Key}' is used {duplicate.Count()} times; ids must be unique.",
                null, duplicate.Key));
        }

        foreach (var edge in flow.Edges)
        {
            if (flow.FindNode(edge.FromNodeId) is null)
            {
                issues.Add(new FlowValidationIssue(
                    FlowValidationSeverity.Error, FlowValidationCodes.DanglingEdge,
                    $"Edge '{edge.Id}' leaves '{edge.FromNodeId}', which is not a node in this flow.",
                    edge.FromNodeId, edge.Id));
            }

            if (flow.FindNode(edge.ToNodeId) is null)
            {
                issues.Add(new FlowValidationIssue(
                    FlowValidationSeverity.Error, FlowValidationCodes.DanglingEdge,
                    $"Edge '{edge.Id}' enters '{edge.ToNodeId}', which is not a node in this flow.",
                    edge.ToNodeId, edge.Id));
            }

            ValidateCondition(edge, issues);
        }
    }

    /// <summary>Checks one edge's condition for a workable source key and pattern.</summary>
    /// <param name="edge">The edge under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateCondition(FlowEdge edge, List<FlowValidationIssue> issues)
    {
        var condition = edge.Condition;
        if (condition is null) return;

        var needsKey = condition.Source is FlowConditionSource.Variable or FlowConditionSource.NodeOutput;
        if (needsKey && string.IsNullOrWhiteSpace(condition.SourceKey))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.MissingConditionSourceKey,
                $"Edge '{edge.Id}' reads {condition.Source} but names no source key.",
                edge.FromNodeId, edge.Id));
        }

        if (condition.Kind != FlowConditionKind.Matches) return;

        try
        {
            _ = Regex.Match(string.Empty, condition.Operand ?? string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException ex)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.InvalidPattern,
                $"Edge '{edge.Id}' has a pattern that will not compile: {ex.Message}",
                edge.FromNodeId, edge.Id));
        }
    }

    /// <summary>Checks that the entry point is explicit and real.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateStartNode(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        if (flow.Nodes.Count == 0) return;

        if (string.IsNullOrWhiteSpace(flow.StartNodeId))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Warning, FlowValidationCodes.ImplicitStartNode,
                $"No start node is declared, so the run begins at '{flow.Nodes[0].Id}' purely because it is first in the list."));
            return;
        }

        if (flow.FindNode(flow.StartNodeId) is null)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.UnknownStartNode,
                $"The start node '{flow.StartNodeId}' is not a node in this flow.",
                flow.StartNodeId));
        }
    }

    /// <summary>Checks one node's kind-specific properties.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="node">The node under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateNode(FlowDefinition flow, FlowNode node, List<FlowValidationIssue> issues)
    {
        switch (node.Kind)
        {
            case FlowNodeKind.Agent when string.IsNullOrWhiteSpace(node.AgentId):
                issues.Add(new FlowValidationIssue(
                    FlowValidationSeverity.Error, FlowValidationCodes.MissingAgentId,
                    $"Agent node '{node.DisplayName}' names no agent.", node.Id));
                break;

            case FlowNodeKind.Tool when string.IsNullOrWhiteSpace(node.ToolName):
                issues.Add(new FlowValidationIssue(
                    FlowValidationSeverity.Error, FlowValidationCodes.MissingToolName,
                    $"Tool node '{node.DisplayName}' names no tool.", node.Id));
                break;

            case FlowNodeKind.Handoff:
                ValidateHandoff(flow, node, issues);
                break;
        }

        if (node.Kind != FlowNodeKind.Agent && !string.IsNullOrWhiteSpace(node.AgentId))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Warning, FlowValidationCodes.IrrelevantProperty,
                $"Node '{node.DisplayName}' is a {node.Kind} node, so its AgentId is ignored.", node.Id));
        }

        if (node.Kind != FlowNodeKind.Tool && !string.IsNullOrWhiteSpace(node.ToolName))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Warning, FlowValidationCodes.IrrelevantProperty,
                $"Node '{node.DisplayName}' is a {node.Kind} node, so its ToolName is ignored.", node.Id));
        }

        if (node.Kind == FlowNodeKind.Terminal) return;
        if (node.Kind == FlowNodeKind.Handoff) return;

        if (flow.EdgesFrom(node.Id).Count == 0)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Warning, FlowValidationCodes.DeadEndNode,
                $"Node '{node.DisplayName}' is not terminal but has no outgoing edge, so the run ends there.",
                node.Id));
        }
    }

    /// <summary>Checks a handoff node's target.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="node">The handoff node.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateHandoff(FlowDefinition flow, FlowNode node, List<FlowValidationIssue> issues)
    {
        if (node.Handoff is null || string.IsNullOrWhiteSpace(node.Handoff.TargetNodeId))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.MissingHandoffTarget,
                $"Handoff node '{node.DisplayName}' names no target node.", node.Id));
            return;
        }

        var target = flow.FindNode(node.Handoff.TargetNodeId);
        if (target is null)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.UnknownHandoffTarget,
                $"Handoff node '{node.DisplayName}' targets '{node.Handoff.TargetNodeId}', which is not a node in this flow.",
                node.Id));
            return;
        }

        if (target.Kind != FlowNodeKind.Agent)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.HandoffTargetNotAgent,
                $"Handoff node '{node.DisplayName}' targets '{target.DisplayName}', which is a {target.Kind} node. Control can only be handed to an agent.",
                node.Id));
        }
    }

    /// <summary>Checks each branching node for a default edge and for edges made unreachable by ordering.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateBranching(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        foreach (var node in flow.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Id)))
        {
            var outgoing = flow.EdgesFrom(node.Id);
            if (outgoing.Count < 2) continue;

            if (!outgoing.Any(edge => edge.IsDefault))
            {
                issues.Add(new FlowValidationIssue(
                    FlowValidationSeverity.Warning, FlowValidationCodes.NoDefaultBranch,
                    $"Node '{node.DisplayName}' branches but has no unconditional edge; a value matching none of its conditions ends the run.",
                    node.Id));
            }

            for (var index = 0; index < outgoing.Count - 1; index++)
            {
                if (!outgoing[index].IsDefault) continue;

                issues.Add(new FlowValidationIssue(
                    FlowValidationSeverity.Warning, FlowValidationCodes.UnreachableBranch,
                    $"Edge '{outgoing[index].Id}' is unconditional and evaluated before {outgoing.Count - index - 1} later edge(s) on '{node.DisplayName}', which can therefore never be taken.",
                    node.Id, outgoing[index].Id));
                break;
            }
        }
    }

    /// <summary>Checks that every node can be reached from the entry point.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateReachability(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        var start = flow.ResolveStartNode();
        if (start is null) return;

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(start.Id);
        reached.Add(start.Id);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            foreach (var next in Successors(flow, current))
            {
                if (reached.Add(next)) pending.Enqueue(next);
            }
        }

        foreach (var node in flow.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Id) && !reached.Contains(node.Id)))
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Warning, FlowValidationCodes.UnreachableNode,
                $"Node '{node.DisplayName}' cannot be reached from the start node.", node.Id));
        }
    }

    /// <summary>Finds cycles and reports them at the severity the flow's own setting asks for.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    /// <remarks>
    /// Iterative depth-first search with an explicit stack — a recursive walk over a
    /// hand-editable graph is itself an unbounded-recursion risk, which would be an odd way to
    /// implement a termination check.
    /// </remarks>
    private static void ValidateCycles(FlowDefinition flow, List<FlowValidationIssue> issues)
    {
        var severity = flow.AllowCycles ? FlowValidationSeverity.Warning : FlowValidationSeverity.Error;
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in flow.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Id)))
        {
            if (state.ContainsKey(node.Id)) continue;

            var stack = new Stack<(string NodeId, IEnumerator<string> Successors)>();
            state[node.Id] = 1;
            stack.Push((node.Id, Successors(flow, node.Id).GetEnumerator()));

            while (stack.Count > 0)
            {
                var (currentId, successors) = stack.Peek();

                if (!successors.MoveNext())
                {
                    state[currentId] = 2;
                    stack.Pop();
                    continue;
                }

                var next = successors.Current;
                var visitState = state.GetValueOrDefault(next, 0);

                if (visitState == 1)
                {
                    if (reported.Add(next))
                    {
                        issues.Add(new FlowValidationIssue(
                            severity, FlowValidationCodes.CycleDetected,
                            $"The flow can return to '{next}' from '{currentId}'. "
                            + (flow.AllowCycles
                                ? $"Cycles are allowed on this flow, so the run is bounded by MaxSteps ({flow.MaxSteps}) instead."
                                : "Set AllowCycles to permit loops; the run is bounded by MaxSteps either way."),
                            next));
                    }

                    continue;
                }

                if (visitState != 0) continue;

                state[next] = 1;
                stack.Push((next, Successors(flow, next).GetEnumerator()));
            }
        }
    }

    /// <summary>Gets the nodes control can move to from a node — its edges plus any handoff target.</summary>
    /// <param name="flow">The flow under validation.</param>
    /// <param name="nodeId">The node to leave.</param>
    /// <returns>The successor node ids, deduplicated, in evaluation order.</returns>
    private static IEnumerable<string> Successors(FlowDefinition flow, string nodeId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var node = flow.FindNode(nodeId);

        if (node?.Kind == FlowNodeKind.Handoff && node.Handoff is not null
            && !string.IsNullOrWhiteSpace(node.Handoff.TargetNodeId)
            && flow.FindNode(node.Handoff.TargetNodeId) is not null
            && seen.Add(node.Handoff.TargetNodeId))
        {
            yield return node.Handoff.TargetNodeId;
        }

        foreach (var edge in flow.EdgesFrom(nodeId))
        {
            if (flow.FindNode(edge.ToNodeId) is null) continue;
            if (seen.Add(edge.ToNodeId)) yield return edge.ToNodeId;
        }
    }

    /// <summary>Checks that an agent node's agent exists on this host.</summary>
    /// <param name="runtime">The runtime that would run the flow.</param>
    /// <param name="node">The node under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    /// <param name="cancellationToken">Token cancelled when the caller gives up.</param>
    /// <returns>A task that completes when the check is done.</returns>
    private static async Task ValidateAgentBindingAsync(
        FlowRuntime runtime, FlowNode node, List<FlowValidationIssue> issues, CancellationToken cancellationToken)
    {
        if (node.Kind != FlowNodeKind.Agent || string.IsNullOrWhiteSpace(node.AgentId)) return;

        var agent = await runtime.Agents.ResolveAgentAsync(node.AgentId, cancellationToken).ConfigureAwait(false);
        if (agent is not null) return;

        issues.Add(new FlowValidationIssue(
            FlowValidationSeverity.Error, FlowValidationCodes.UnresolvableAgent,
            $"Node '{node.DisplayName}' names agent '{node.AgentId}', which this host cannot resolve.",
            node.Id));
    }

    /// <summary>Checks that every guardrail a node names exists on this host.</summary>
    /// <param name="runtime">The runtime that would run the flow.</param>
    /// <param name="node">The node under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    /// <param name="cancellationToken">Token cancelled when the caller gives up.</param>
    /// <returns>A task that completes when the checks are done.</returns>
    private static async Task ValidateGuardrailBindingsAsync(
        FlowRuntime runtime, FlowNode node, List<FlowValidationIssue> issues, CancellationToken cancellationToken)
    {
        foreach (var id in node.GuardrailIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            var guardrail = runtime.Guardrails is null
                ? null
                : await runtime.Guardrails.ResolveGuardrailAsync(id, cancellationToken).ConfigureAwait(false);

            if (guardrail is not null) continue;

            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.UnresolvableGuardrail,
                $"Node '{node.DisplayName}' requires guardrail '{id}', which this host cannot resolve. "
                + "The node would be blocked at run time, because a guardrail that cannot run denies.",
                node.Id));
        }
    }

    /// <summary>Checks that a tool node's tool is offered by this runtime.</summary>
    /// <param name="runtime">The runtime that would run the flow.</param>
    /// <param name="node">The node under validation.</param>
    /// <param name="issues">The list issues are appended to.</param>
    private static void ValidateToolBinding(FlowRuntime runtime, FlowNode node, List<FlowValidationIssue> issues)
    {
        if (node.Kind != FlowNodeKind.Tool || string.IsNullOrWhiteSpace(node.ToolName)) return;

        if (runtime.Tools is null)
        {
            issues.Add(new FlowValidationIssue(
                FlowValidationSeverity.Error, FlowValidationCodes.NoToolHandler,
                $"Node '{node.DisplayName}' calls tool '{node.ToolName}' but this runtime has no tool handler.",
                node.Id));
            return;
        }

        var isKnown = runtime.Tools.ToolDefinitions
            .Any(definition => string.Equals(definition.Name, node.ToolName, StringComparison.OrdinalIgnoreCase));

        if (isKnown) return;

        issues.Add(new FlowValidationIssue(
            FlowValidationSeverity.Error, FlowValidationCodes.UnresolvableTool,
            $"Node '{node.DisplayName}' calls tool '{node.ToolName}', which this runtime does not offer.",
            node.Id));
    }
}
