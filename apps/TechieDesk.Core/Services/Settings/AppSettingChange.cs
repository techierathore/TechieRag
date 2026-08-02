namespace TechieDesk.Services.Settings;

/// <summary>
/// One field of the App settings Defaults tab that a save actually altered.
/// </summary>
/// <param name="SettingName">The label the screen shows for the field.</param>
/// <param name="OldValue">The value before the save, rendered for display.</param>
/// <param name="NewValue">The value after the save, rendered for display.</param>
public sealed record AppSettingChange(string SettingName, string OldValue, string NewValue);
