using System.Globalization;
using System.Text.Json;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Settings;

/// <summary>
/// Turns a Defaults-tab save into correlated <see cref="EventLog"/> rows.
/// </summary>
/// <remarks>
/// <para>
/// This is the reason the operator event log has anything real to show on a fresh install: the
/// screen that writes app settings is also the screen that records having written them. Every field
/// altered by one save shares a single correlation id, so the event-log Details view can show them
/// together under "Related events" (REQ-UI-026).
/// </para>
/// <para>
/// <b>REQ-UI-055 / BRD-91 — the field labels are classified as persisted audit vocabulary and stay
/// invariant English.</b> They are not rendered from here: <see cref="Compare"/> hands them straight
/// into <see cref="EventLog.EventName"/> and into the <c>Detail</c> JSON, both of which are written
/// ONCE into an append-only table and read for the rest of the install's life. Localizing them would
/// stamp each row with whatever language the app happened to be in at the moment of the save, so an
/// operator who used Hindi for a month would end up with an audit log that no single query can group
/// and that no later language change can re-render — the rows are already on disk. A row that says
/// what happened in one stable vocabulary is worth more than a row translated for the person who
/// wrote it and nobody else.
/// </para>
/// <para>
/// The cost is real and is named here rather than hidden: a Hindi operator reads English event names
/// at <c>/admin/events</c>. Fixing that properly means the event log storing a KEY beside its prose
/// — a schema change across every producer of an event row, not something this requirement can do in
/// one service. It is recorded as the open question, not as done.
/// </para>
/// </remarks>
public sealed class AppSettingsChangeLog : IAppSettingsChangeLog
{
    /// <summary>The event-log category these events are filed under.</summary>
    /// <remarks>
    /// "Configuration", not "Admin": the 2026-07-26 UI-design amendment reworded the event log's
    /// third category from "admin actions" to "configuration changes".
    /// </remarks>
    public const string CategoryName = "Configuration";

    /// <summary>The actor recorded for locally made changes.</summary>
    /// <remarks>
    /// A desktop install has exactly one person using it, so the event log names them the way the
    /// UI design does — "you" — rather than inventing an account identity that does not exist.
    /// </remarks>
    public const string LocalActor = "you";

    /// <summary>The source recorded for changes made from the App settings screen.</summary>
    public const string SourceName = "admin:settings";

    private readonly IEventLogRepository eventLogs;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the change log.</summary>
    /// <param name="eventLogs">The append-only event-log repository.</param>
    /// <param name="timeProvider">Clock used to stamp the group; defaults to the system clock.</param>
    public AppSettingsChangeLog(IEventLogRepository eventLogs, TimeProvider? timeProvider = null)
    {
        this.eventLogs = eventLogs;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Compares two snapshots without writing anything.</summary>
    /// <param name="before">The snapshot the screen loaded.</param>
    /// <param name="after">The snapshot the screen saved.</param>
    /// <returns>The fields whose values differ, in the order the screen lays them out.</returns>
    public static IReadOnlyList<AppSettingChange> Compare(AppDefaults before, AppDefaults after)
    {
        var changes = new List<AppSettingChange>();

        Add(changes, "Default LLM", before.LlmProvider.ToString(), after.LlmProvider.ToString());
        Add(changes, "Default LLM model", before.LlmModel, after.LlmModel);
        Add(changes, "Default embeddings", before.EmbeddingProvider.ToString(), after.EmbeddingProvider.ToString());
        Add(changes, "Vector store", before.VectorStore.ToString(), after.VectorStore.ToString());
        Add(
            changes,
            "Max upload size",
            $"{before.MaxUploadSizeMb.ToString(CultureInfo.InvariantCulture)} MB",
            $"{after.MaxUploadSizeMb.ToString(CultureInfo.InvariantCulture)} MB");

        return changes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppSettingChange>> RecordAsync(AppDefaults before, AppDefaults after)
    {
        var changes = Compare(before, after);
        if (changes.Count == 0)
        {
            return changes;
        }

        var correlationId = $"cfg{Guid.NewGuid():N}";
        var occurredAt = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var change in changes)
        {
            await eventLogs.AppendAsync(new EventLog
            {
                OccurredAt = occurredAt,
                Category = CategoryName,
                Actor = LocalActor,
                EventName = $"{change.SettingName} changed to {change.NewValue}",
                Detail = JsonSerializer.Serialize(new
                {
                    setting = change.SettingName,
                    from = change.OldValue,
                    to = change.NewValue
                }),
                Source = SourceName,
                CorrelationId = correlationId
            }).ConfigureAwait(false);
        }

        return changes;
    }

    private static void Add(List<AppSettingChange> changes, string name, string oldValue, string newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            changes.Add(new AppSettingChange(name, oldValue, newValue));
        }
    }
}
