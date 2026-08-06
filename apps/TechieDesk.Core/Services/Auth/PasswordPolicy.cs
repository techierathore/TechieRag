namespace TechieDesk.Services.Auth;

/// <summary>
/// Client-side password complexity check mirroring the AppManager registration/reset policy
/// (BRD-12): at least 8 characters with at least one uppercase letter, one digit, and one
/// special character. The server remains the authority — this is a fast pre-flight so the user
/// gets per-field feedback before a round-trip.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>The human-readable complexity requirement, shown as a hint under password fields.</summary>
    public const string Requirement = "Min 8 chars, 1 uppercase, 1 number, 1 special character";

    /// <summary>
    /// Determines whether a candidate password satisfies the complexity policy.
    /// </summary>
    /// <param name="password">The candidate password.</param>
    /// <returns><c>true</c> when the password meets every rule; otherwise <c>false</c>.</returns>
    public static bool IsValid(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            return false;
        }

        var hasUpper = false;
        var hasDigit = false;
        var hasSpecial = false;

        foreach (var ch in password)
        {
            if (char.IsUpper(ch))
            {
                hasUpper = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }
            else if (!char.IsLetterOrDigit(ch))
            {
                hasSpecial = true;
            }
        }

        return hasUpper && hasDigit && hasSpecial;
    }
}
