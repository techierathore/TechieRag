namespace TechieRag.Probe;

/// <summary>
/// Builds the probe's MAUI app (REQ-FN-056).
/// </summary>
public static class MauiProgram
{
    /// <summary>Creates the app.</summary>
    /// <returns>The configured MAUI app.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        return builder.Build();
    }
}
