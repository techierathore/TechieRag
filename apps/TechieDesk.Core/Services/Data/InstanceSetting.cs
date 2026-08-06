namespace TechieDesk.Services.Data;

/// <summary>
/// Instance-wide key/value setting row (BRD-104 P1 schema).
/// </summary>
public sealed class InstanceSetting
{
    /// <summary>Setting key (primary key).</summary>
    public string SettingKey { get; set; } = string.Empty;

    /// <summary>Setting value serialized as text.</summary>
    public string SettingValue { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; }
}
