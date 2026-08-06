namespace TechieDesk.Services.Scheduling.Authoring;

/// <summary>
/// The JSON shape the model is asked to return (REQ-UI-046).
/// </summary>
/// <remarks>
/// <para>Deliberately flat and small. Every field is either checked against something the app already
/// knows (<see cref="JobKind"/> against the registered handlers, <see cref="Cron"/> against the cron
/// parser) or shown to the user verbatim for confirmation. Nothing here is trusted.</para>
/// <para><see cref="Summary"/> is captured but <b>never used as the schedule's displayed text</b> —
/// see <see cref="ScheduleDraft.ScheduleText"/> for why. It is kept only so a mismatch between what
/// the model said and what its own expression means can be surfaced as a warning.</para>
/// </remarks>
public sealed class ScheduleInterpretationPayload
{
    /// <summary>Gets or sets the proposed short job name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the five-field cron expression the model derived.</summary>
    public string? Cron { get; set; }

    /// <summary>Gets or sets the model's own description of the schedule. Used only as a cross-check.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the chosen action's handler key. Must be one of the offered actions.</summary>
    public string? JobKind { get; set; }

    /// <summary>Gets or sets the handler-specific payload the model proposed, as a JSON string.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the steps the model understood, each a plain-language line.</summary>
    public List<string>? Steps { get; set; }

    /// <summary>Gets or sets what should happen with the result — the delivery action.</summary>
    public string? Delivery { get; set; }

    /// <summary>Gets or sets anything the model was unsure about.</summary>
    public List<string>? Notes { get; set; }
}
