using System.Text.RegularExpressions;

namespace TechieRag.Orchestration;

/// <summary>What a <see cref="FlowCondition"/> reads before it compares (REQ-RAG-042).</summary>
public enum FlowConditionSource
{
    /// <summary>The output of the node the edge leaves. The usual choice.</summary>
    LastOutput,

    /// <summary>The value of a named flow variable, written by an earlier node's output.</summary>
    Variable,

    /// <summary>The recorded output of a specific earlier node, named by its id.</summary>
    NodeOutput,

    /// <summary>The flow's original input, unchanged by anything that has run.</summary>
    OriginalInput
}

/// <summary>The comparison a <see cref="FlowCondition"/> applies (REQ-RAG-042).</summary>
/// <remarks>
/// Every operator is deterministic, side-effect free and local: evaluating a condition never calls
/// an LLM, never touches a tool and never opens a socket. Branch selection is therefore free, and a
/// UI can preview which branch a given value would take without running anything.
/// </remarks>
public enum FlowConditionKind
{
    /// <summary>Always true. The explicit default/fallback edge.</summary>
    Always,

    /// <summary>The source contains <see cref="FlowCondition.Operand"/>.</summary>
    Contains,

    /// <summary>The source does not contain <see cref="FlowCondition.Operand"/>.</summary>
    NotContains,

    /// <summary>The source equals <see cref="FlowCondition.Operand"/>.</summary>
    EqualsText,

    /// <summary>The source differs from <see cref="FlowCondition.Operand"/>.</summary>
    NotEqualsText,

    /// <summary>The source starts with <see cref="FlowCondition.Operand"/>.</summary>
    StartsWith,

    /// <summary>The source is null, empty or whitespace.</summary>
    IsEmpty,

    /// <summary>The source has some non-whitespace content.</summary>
    IsNotEmpty,

    /// <summary>The previous node succeeded.</summary>
    LastStepSucceeded,

    /// <summary>The previous node failed.</summary>
    LastStepFailed,

    /// <summary>The source matches the regular expression in <see cref="FlowCondition.Operand"/>.</summary>
    Matches
}

/// <summary>
/// A declarative, serializable predicate on one outgoing edge (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Declarative because it is persisted.</b> A flow is user data saved by the host and
/// reopened by a builder UI. A delegate cannot be stored, shown in an editor, or reasoned about by
/// <see cref="FlowValidator"/>; a small closed operator set can. It also means a stored flow can
/// never smuggle executable code past the host.</para>
/// <para><b>Regular expressions are bounded.</b> <see cref="FlowConditionKind.Matches"/> evaluates
/// with a 250 ms timeout and treats a malformed pattern or a timeout as "did not match", so a bad
/// pattern in stored data cannot hang a run. <see cref="FlowValidator"/> reports the malformed
/// pattern separately, at edit time, where it can still be fixed.</para>
/// </remarks>
public sealed class FlowCondition
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the comparison to apply.</summary>
    public required FlowConditionKind Kind { get; set; }

    /// <summary>Gets or sets what to read. Defaults to the previous node's output.</summary>
    public FlowConditionSource Source { get; set; } = FlowConditionSource.LastOutput;

    /// <summary>
    /// Gets or sets the variable name or node id when <see cref="Source"/> needs one. Ignored for
    /// <see cref="FlowConditionSource.LastOutput"/> and <see cref="FlowConditionSource.OriginalInput"/>.
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>Gets or sets the value compared against, or the pattern for <see cref="FlowConditionKind.Matches"/>.</summary>
    public string? Operand { get; set; }

    /// <summary>Gets or sets whether text comparisons are case sensitive. Defaults to false.</summary>
    public bool IsCaseSensitive { get; set; }

    /// <summary>
    /// Evaluates this condition against a run's current state.
    /// </summary>
    /// <param name="state">The run state to read.</param>
    /// <returns>True when the edge carrying this condition may be taken.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    /// <remarks>
    /// Public so a builder UI can show the author which branch a sample value would take, without
    /// executing the flow. It has no side effects and never leaves the process.
    /// </remarks>
    public bool IsSatisfiedBy(FlowState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (Kind == FlowConditionKind.Always) return true;
        if (Kind == FlowConditionKind.LastStepSucceeded) return state.IsLastStepSuccess;
        if (Kind == FlowConditionKind.LastStepFailed) return !state.IsLastStepSuccess;

        var value = ReadSource(state) ?? string.Empty;

        if (Kind == FlowConditionKind.IsEmpty) return string.IsNullOrWhiteSpace(value);
        if (Kind == FlowConditionKind.IsNotEmpty) return !string.IsNullOrWhiteSpace(value);

        var operand = Operand ?? string.Empty;
        var comparison = IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return Kind switch
        {
            FlowConditionKind.Contains => value.Contains(operand, comparison),
            FlowConditionKind.NotContains => !value.Contains(operand, comparison),
            FlowConditionKind.EqualsText => string.Equals(value, operand, comparison),
            FlowConditionKind.NotEqualsText => !string.Equals(value, operand, comparison),
            FlowConditionKind.StartsWith => value.StartsWith(operand, comparison),
            FlowConditionKind.Matches => IsRegexMatch(value, operand),
            _ => false
        };
    }

    /// <summary>Reads the text this condition compares.</summary>
    /// <param name="state">The run state to read.</param>
    /// <returns>The selected text, or null when the named variable or node has produced nothing.</returns>
    private string? ReadSource(FlowState state) => Source switch
    {
        FlowConditionSource.Variable => SourceKey is not null && state.Variables.TryGetValue(SourceKey, out var variable)
            ? variable
            : null,
        FlowConditionSource.NodeOutput => SourceKey is not null && state.NodeOutputs.TryGetValue(SourceKey, out var output)
            ? output
            : null,
        FlowConditionSource.OriginalInput => state.OriginalInput,
        _ => state.LastOutput
    };

    /// <summary>Applies a bounded regular expression, treating a bad pattern as no match.</summary>
    /// <param name="value">The text to test.</param>
    /// <param name="pattern">The pattern from <see cref="Operand"/>.</param>
    /// <returns>True on a match; false on no match, a timeout, or a malformed pattern.</returns>
    private bool IsRegexMatch(string value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;

        var options = IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        try
        {
            return Regex.IsMatch(value, pattern, options, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
