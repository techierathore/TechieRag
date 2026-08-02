namespace TechieDesk.Services.Auth;

/// <summary>
/// The result of an in-process sign-in or registration attempt (REQ-FN-039 / REQ-UI-007).
/// </summary>
/// <param name="Succeeded">Whether a session was established.</param>
/// <param name="ErrorCode">
/// The wire or screen error code to render a banner from, or null on success. The codes are exactly
/// those <c>Login.razor</c> and <c>Register.razor</c> already switch on, so the screens' existing
/// messaging is unchanged by the move off the retired HTTP endpoints.
/// </param>
public readonly record struct SignInOutcome(bool Succeeded, string? ErrorCode)
{
    /// <summary>A successful sign-in.</summary>
    /// <returns>The outcome.</returns>
    public static SignInOutcome Success() => new(true, null);

    /// <summary>A failed sign-in.</summary>
    /// <param name="errorCode">The error code to render.</param>
    /// <returns>The outcome.</returns>
    public static SignInOutcome Failure(string errorCode) => new(false, errorCode);
}
