using System.Collections.Concurrent;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// The default <see cref="ISubscriptionSessionStore"/>: keeps sessions in process memory only
/// (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// The user signs in again after every restart. A host that wants one sign-in per device implements
/// <see cref="ISubscriptionSessionStore"/> over its platform's secure store.
/// </remarks>
public sealed class InMemorySubscriptionSessionStore : ISubscriptionSessionStore
{
    private readonly ConcurrentDictionary<string, SubscriptionSession> sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<SubscriptionSession?> LoadAsync(string connectorName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectorName);
        return Task.FromResult(sessions.TryGetValue(connectorName, out var session) ? session : null);
    }

    /// <inheritdoc/>
    public Task SaveAsync(SubscriptionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        sessions[session.ConnectorName] = session;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearAsync(string connectorName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectorName);
        sessions.TryRemove(connectorName, out _);
        return Task.CompletedTask;
    }
}
