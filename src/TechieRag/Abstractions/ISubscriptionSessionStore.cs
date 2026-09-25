using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// The seam through which a host persists a subscription sign-in so the user signs in once
/// (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// <para><b>Who implements it:</b> the host. The library ships only
/// <see cref="Llm.InMemorySubscriptionSessionStore"/>, which forgets the session when the process ends;
/// the library never writes a token to disk (Architecture §Identity).</para>
/// <para><b>When it is called:</b> <see cref="LoadAsync"/> before the first model call; <see cref="SaveAsync"/>
/// after a sign-in and after every token refresh (a refresh can rotate the refresh token, so the saved
/// copy must be replaced); <see cref="ClearAsync"/> when the vendor rejects the session outright, so
/// the next call signs in again.</para>
/// <para><b>Keep it secret:</b> a session carries bearer tokens. Store it in the platform's secure
/// store, keyed by <see cref="SubscriptionSession.ConnectorName"/> and by the host's own user.</para>
/// </remarks>
public interface ISubscriptionSessionStore
{
    /// <summary>Loads the saved session for a connector.</summary>
    /// <param name="connectorName">The catalog connector, e.g. <c>chatgpt-subscription</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session, or null when the user has not signed in.</returns>
    Task<SubscriptionSession?> LoadAsync(string connectorName, CancellationToken cancellationToken = default);

    /// <summary>Saves a session, replacing any earlier one for the same connector.</summary>
    /// <param name="session">The session to keep.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session is saved.</returns>
    Task SaveAsync(SubscriptionSession session, CancellationToken cancellationToken = default);

    /// <summary>Forgets the session for a connector.</summary>
    /// <param name="connectorName">The catalog connector.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session is gone.</returns>
    Task ClearAsync(string connectorName, CancellationToken cancellationToken = default);
}
