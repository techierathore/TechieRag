namespace TechieRag.Orchestration;

/// <summary>
/// The mutable state carried through one flow run — what has been produced so far, and what the
/// conditions read (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Strings, not objects.</b> Every value is text because everything here has to survive
/// being shown in a builder UI, compared by a serialized <see cref="FlowCondition"/>, and rendered
/// into a prompt. A typed object graph would be richer and none of those three consumers could use
/// it.</para>
/// <para><b>Flow state is not agent context.</b> Nothing in <see cref="Variables"/> reaches a model
/// unless a node's <see cref="FlowNode.Instruction"/> or a handoff's
/// <see cref="FlowHandoff.CarryVariables"/> puts it there. The run can therefore remember things the
/// agents are never told.</para>
/// </remarks>
public sealed class FlowState
{
    /// <summary>Creates a state seeded with the flow's input.</summary>
    /// <param name="originalInput">The text the run was started with.</param>
    /// <param name="variables">Initial variables, or null for none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalInput"/> is null.</exception>
    public FlowState(string originalInput, IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(originalInput);

        OriginalInput = originalInput;
        LastOutput = originalInput;

        if (variables is null) return;

        foreach (var pair in variables)
        {
            Variables[pair.Key] = pair.Value;
        }
    }

    /// <summary>Gets the text the run was started with, unchanged by anything that has run since.</summary>
    public string OriginalInput { get; }

    /// <summary>Gets or sets the output of the most recently completed node.</summary>
    public string LastOutput { get; set; }

    /// <summary>Gets or sets whether the most recently completed node succeeded.</summary>
    public bool IsLastStepSuccess { get; set; } = true;

    /// <summary>Gets the named variables written by nodes that declare an output variable.</summary>
    public Dictionary<string, string> Variables { get; } = [];

    /// <summary>Gets the output of every node that has completed, keyed by node id.</summary>
    public Dictionary<string, string> NodeOutputs { get; } = [];

    /// <summary>
    /// Expands the <c>{{input}}</c> and <c>{{var:name}}</c> placeholders in a template.
    /// </summary>
    /// <param name="template">The text to expand; null returns null.</param>
    /// <returns>The expanded text. An unknown variable expands to the empty string.</returns>
    /// <remarks>
    /// Used for a tool node's argument JSON. Substitution is textual and JSON-escaped for the value,
    /// so a value containing a quote cannot break out of the surrounding JSON string and change the
    /// shape of the call.
    /// </remarks>
    public string? Expand(string? template)
    {
        if (string.IsNullOrEmpty(template)) return template;

        var expanded = template.Replace("{{input}}", JsonEscape(LastOutput), StringComparison.Ordinal);

        foreach (var pair in Variables)
        {
            expanded = expanded.Replace("{{var:" + pair.Key + "}}", JsonEscape(pair.Value), StringComparison.Ordinal);
        }

        return expanded;
    }

    /// <summary>Escapes a value so it is safe inside a JSON string literal.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The escaped value, without surrounding quotes.</returns>
    private static string JsonEscape(string value)
    {
        var encoded = System.Text.Json.JsonSerializer.Serialize(value);
        return encoded.Length >= 2 ? encoded[1..^1] : encoded;
    }
}
