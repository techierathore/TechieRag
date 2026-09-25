namespace TechieRag.Probe;

/// <summary>
/// The probe application: one window holding <see cref="MainPage"/>.
/// </summary>
public sealed class App : Application
{
    /// <inheritdoc/>
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage(new ProbeRunner()))
        {
            Title = "TechieRag Probe",
            Width = 720,
            Height = 760
        };
}
