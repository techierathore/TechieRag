namespace TechieRag.Agentic.HostData;

/// <summary>Options for <see cref="HostDataSkill"/> (REQ-RAG-153 / BRD-211, REQ-RAG-154 / BRD-212).</summary>
public sealed class HostDataSkillOptions
{
    /// <summary>Gets or sets the tool name prefix; the tools are <c>{prefix}_list</c>, <c>_read</c>,
    /// <c>_create</c>, <c>_update</c> and <c>_delete</c>. Default <c>host_data</c>.</summary>
    public string ToolPrefix { get; set; } = "host_data";

    /// <summary>Gets or sets the most records a list returns, whatever the model asks. Default 50.</summary>
    public int MaxListResults { get; set; } = 50;

    /// <summary>Gets or sets whether the delete tool is marked as needing the user's confirmation. Default true.</summary>
    public bool DeleteRequiresConfirmation { get; set; } = true;

    /// <summary>
    /// Gets or sets a callback awaited for every change before the tool result goes back to the model, so the
    /// host can store the record (for example to show a card with Undo) before the turn goes on. Null for none.
    /// </summary>
    public Func<HostDataChange, CancellationToken, Task>? OnChangeAsync { get; set; }
}
