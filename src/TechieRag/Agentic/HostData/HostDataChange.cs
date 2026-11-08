namespace TechieRag.Agentic.HostData;

/// <summary>
/// A typed record of one change the host-data skill made, so the host can show it and undo it
/// (REQ-RAG-154 / BRD-212).
/// </summary>
/// <remarks>
/// Returned through <see cref="HostDataSkill.ChangeRecorded"/>, <see cref="HostDataSkillOptions.OnChangeAsync"/> and
/// <see cref="HostDataSkill.TurnChanges"/>; pass it back to <see cref="HostDataSkill.UndoAsync"/> to reverse it
/// through the host's own handlers. Every property is plain data, so a host can store the record (as JSON) and
/// undo it later, in another process.
/// </remarks>
public sealed record HostDataChange
{
    /// <summary>Gets this change's id.</summary>
    public string ChangeId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets the entity name, for example <c>task</c>.</summary>
    public required string Entity { get; init; }

    /// <summary>Gets the host's id of the record that changed.</summary>
    public required string EntityId { get; init; }

    /// <summary>Gets what the change did.</summary>
    public required HostDataChangeKind Kind { get; init; }

    /// <summary>Gets the record as it was before the change; null for <see cref="HostDataChangeKind.Created"/>.</summary>
    public HostEntityRecord? Before { get; init; }

    /// <summary>Gets the record as it is after the change; null for <see cref="HostDataChangeKind.Deleted"/>.</summary>
    public HostEntityRecord? After { get; init; }

    /// <summary>Gets the turn that caused the change (<see cref="HostDataSkill.BeginTurn"/>); null for an undo.</summary>
    public string? TurnId { get; init; }

    /// <summary>Gets the model's tool call that made the change; null for an undo.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Gets the change this one reverses, when it was made by <see cref="HostDataSkill.UndoAsync"/>.</summary>
    public string? UndoOf { get; init; }

    /// <summary>Gets when the change was made.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}
