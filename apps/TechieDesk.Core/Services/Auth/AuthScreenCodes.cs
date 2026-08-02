namespace TechieDesk.Services.Auth;

/// <summary>
/// Error codes and navigation helpers shared by the authentication screens (REQ-UI-007/013).
/// </summary>
/// <remarks>
/// REQ-FN-035 extracted these from <c>SessionEndpoints</c>, which is excluded from the desktop build
/// because it is an ASP.NET Core endpoint. The values themselves are pure string logic with no HTTP
/// dependency, and the auth screens still need them to render friendly banners, so they live here
/// rather than being lost with the endpoint.
/// <para>
/// The antiforgery code is deliberately NOT carried over: antiforgery protects an HTML form post
/// against a cross-site forgery, and a desktop app has neither. Keeping a banner for a failure mode
/// that can no longer occur would be dead UI.
/// </para>
/// </remarks>
public static class AuthScreenCodes
{
    /// <summary>Error code for an incomplete form submission.</summary>
    public const string MissingFields = "MISSING_FIELDS";

    /// <summary>Error code for an unexpected failure.</summary>
    public const string Unexpected = "UNEXPECTED";

    /// <summary>Error code for a password that fails the complexity policy.</summary>
    public const string WeakPassword = "WEAK_PASSWORD";

    /// <summary>Error code for mismatched password confirmation.</summary>
    public const string PasswordMismatch = "PASSWORD_MISMATCH";

    /// <summary>
    /// Error code for a sign-in attempted on an install with no licence server configured
    /// (REQ-FN-036 / BRD-129) — there is nothing to sign in to, and local use was never gated.
    /// </summary>
    public const string NoLicenceServer = "NO_LICENCE_SERVER";

    /// <summary>
    /// Filters a return URL down to a safe in-app path, defeating open redirects.
    /// </summary>
    /// <param name="candidate">The requested return URL, or null.</param>
    /// <returns>The candidate when it is a site-relative path; otherwise <c>/</c>.</returns>
    /// <remarks>
    /// A protocol-relative URL (<c>//evil.example</c>) starts with '/' but is absolute, which is why
    /// it is rejected explicitly rather than by the leading-slash check alone.
    /// </remarks>
    public static string SafeReturnUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !candidate.StartsWith('/') || candidate.StartsWith("//"))
        {
            return "/";
        }

        return candidate;
    }
}
