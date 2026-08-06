namespace TechieDesk.Services.Updates;

/// <summary>
/// Raised when the update feed cannot be read (REQ-FN-038b).
/// </summary>
/// <remarks>
/// REQ-NFR-010: an update check that cannot reach the network must say so. It must never be
/// presented as "you are up to date", which is the same statement a successful check makes and would
/// leave an operator believing they hold the newest build while a security fix sits unapplied.
/// </remarks>
public sealed class UpdateFeedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UpdateFeedException"/> class.</summary>
    public UpdateFeedException()
        : base("The update feed could not be read.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UpdateFeedException"/> class.</summary>
    /// <param name="message">Operator-facing description of what failed.</param>
    public UpdateFeedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UpdateFeedException"/> class.</summary>
    /// <param name="message">Operator-facing description of what failed.</param>
    /// <param name="innerException">The underlying failure.</param>
    public UpdateFeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
