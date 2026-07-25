namespace TechieDesk.Services.AppManager;

/// <summary>
/// Maps AppManager wire-format error codes (<c>SCREAMING_SNAKE_CASE</c>) to the
/// <see cref="AppManagerError"/> enum.
/// </summary>
public static class AppManagerErrorMapper
{
    /// <summary>
    /// Maps a wire error code such as <c>DECRYPTION_FAILED</c> to its typed
    /// <see cref="AppManagerError"/> member. Unknown or empty codes map to
    /// <see cref="AppManagerError.Unknown"/>.
    /// </summary>
    /// <param name="errorCode">The raw error code string from the API response, or null.</param>
    /// <returns>The corresponding <see cref="AppManagerError"/> member.</returns>
    public static AppManagerError Map(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return AppManagerError.Unknown;
        }

        var parts = errorCode.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

        return Enum.TryParse<AppManagerError>(pascal, ignoreCase: false, out var parsed)
            ? parsed
            : AppManagerError.Unknown;
    }
}
