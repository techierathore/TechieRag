namespace TechieDesk.Services.Appearance;

/// <summary>
/// The appearance mode chosen by the operator (REQ-UI-038 / BRD-90).
/// </summary>
/// <remarks>
/// <see cref="System"/> is a real third state, not a synonym for <see cref="Light"/>: it defers to
/// the operating system's <c>prefers-color-scheme</c> and therefore CHANGES when macOS or Windows
/// switches at dusk. Collapsing it to a resolved light/dark value at save time would freeze whatever
/// the OS happened to be at that moment, which is the one behaviour "match system" must not have.
/// </remarks>
public enum ThemeMode
{
    /// <summary>Always render the light palette.</summary>
    Light = 0,

    /// <summary>Always render the dark palette.</summary>
    Dark = 1,

    /// <summary>Follow the operating system's colour-scheme preference.</summary>
    System = 2
}
