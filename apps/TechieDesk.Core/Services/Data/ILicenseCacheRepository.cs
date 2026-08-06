namespace TechieDesk.Services.Data;

/// <summary>
/// Upsert and get access to per-user cached license payloads (Dapper-only, BRD-102).
/// </summary>
public interface ILicenseCacheRepository
{
    /// <summary>Creates or replaces the cached license payload for a user.</summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="payloadJson">The validated license payload as JSON.</param>
    /// <param name="validatedAt">UTC timestamp of the successful validation.</param>
    Task UpsertAsync(string userId, string payloadJson, DateTime validatedAt);

    /// <summary>Gets the cached license payload for a user.</summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The cache row, or null when the user has no cached license.</returns>
    Task<LicenseCache?> GetAsync(string userId);
}
