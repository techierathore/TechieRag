namespace TechieRag.Models;

/// <summary>
/// What the host shows the user so they can authorise a subscription sign-in (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// The library never opens a browser. It hands this to the host's sign-in callback; the host opens
/// <see cref="VerificationUri"/> (or shows it with <see cref="UserCode"/> for the user to type on another
/// device) and returns. The library then waits for the vendor to report the authorisation.
/// </remarks>
public sealed record SubscriptionSignInPrompt
{
    /// <summary>Gets the catalog connector being signed in to, e.g. <c>chatgpt-subscription</c>.</summary>
    public required string ConnectorName { get; init; }

    /// <summary>Gets the vendor page the user opens to authorise the app.</summary>
    public required Uri VerificationUri { get; init; }

    /// <summary>Gets the one-time code the user enters on that page.</summary>
    public required string UserCode { get; init; }

    /// <summary>Gets when the code stops working.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
