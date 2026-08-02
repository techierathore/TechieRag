namespace TechieDesk.Services.Settings;

/// <summary>
/// Writes the audit trail behind the App settings screen into the event log (REQ-UI-026/REQ-UI-028).
/// </summary>
public interface IAppSettingsChangeLog
{
    /// <summary>Records every field a save altered, as one correlated group of events.</summary>
    /// <param name="before">The snapshot the screen loaded.</param>
    /// <param name="after">The snapshot the screen saved.</param>
    /// <returns>
    /// The changes that were recorded, in field order. An empty list means the two snapshots were
    /// identical and nothing was written — a save that changed nothing is not an audit event.
    /// </returns>
    Task<IReadOnlyList<AppSettingChange>> RecordAsync(AppDefaults before, AppDefaults after);
}
