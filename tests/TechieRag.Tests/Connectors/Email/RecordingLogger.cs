using Microsoft.Extensions.Logging;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// A logger that keeps everything written to it, at every level.
/// </summary>
/// <typeparam name="T">The category type.</typeparam>
/// <remarks>
/// <see cref="IsEnabled"/> answers true for every level on purpose: a diagnostic emitted only at
/// Debug is still a diagnostic somebody turns on, and a credential in one is still disclosed.
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Gets every message formatted so far.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Messages.Add(formatter(state, exception));

        if (exception is not null)
        {
            Messages.Add(exception.ToString());
        }
    }
}
