namespace TechieRag.Models;

/// <summary>
/// A signed-in consumer-subscription session with an LLM vendor (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> What the library hands to <see cref="Abstractions.ISubscriptionSessionStore"/>
/// after a sign-in or a token refresh, so the host can persist it and the user signs in once.</para>
/// <para><b>It holds secrets.</b> The tokens let anyone holding them spend the user's subscription.
/// The library keeps a session in memory only; a host that persists it must use its platform's
/// secure store (Windows Credential Manager, Keychain, Android Keystore, <c>DataProtection</c>), never
/// a plain file or a log. <see cref="ToString"/> is overridden so a session is never logged by accident.</para>
/// </remarks>
public sealed record SubscriptionSession
{
    /// <summary>Gets the catalog connector the session belongs to, e.g. <c>chatgpt-subscription</c>.</summary>
    public required string ConnectorName { get; init; }

    /// <summary>Gets the bearer token sent with each model call.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Gets the token used to obtain a new access token, or null when the vendor issued none.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Gets the OpenID Connect identity token, or null when the vendor issued none.</summary>
    public string? IdToken { get; init; }

    /// <summary>Gets the vendor account the subscription belongs to, when the vendor needs it on each call.</summary>
    public string? AccountId { get; init; }

    /// <summary>Gets when the access token expires, or null when unknown.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets whether the access token has expired or will within <paramref name="margin"/>.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="margin">How long before expiry a token already counts as expired.</param>
    /// <returns>True when the token should be refreshed before use.</returns>
    public bool IsExpired(DateTimeOffset now, TimeSpan margin) =>
        ExpiresAt is { } expiresAt && expiresAt - margin <= now;

    /// <summary>Returns a description that never contains a token.</summary>
    /// <returns>The connector, account and expiry.</returns>
    public override string ToString() =>
        $"SubscriptionSession {{ ConnectorName = {ConnectorName}, AccountId = {AccountId}, ExpiresAt = {ExpiresAt:O} }}";
}
