namespace TechieDesk.Services.Data;

/// <summary>
/// Insert and list access to <see cref="GdprRequest"/> rows (Dapper-only, BRD-102).
/// </summary>
public interface IGdprRequestRepository
{
    /// <summary>Inserts a GDPR request and returns its new primary key.</summary>
    /// <param name="request">The request to insert; <c>RequestedAt</c> defaults to now (UTC) when unset.</param>
    /// <returns>The generated <c>GdprRequestId</c>.</returns>
    Task<long> InsertAsync(GdprRequest request);

    /// <summary>Lists GDPR requests, optionally restricted to one user, newest first.</summary>
    /// <param name="userId">Optional user filter; null lists all requests.</param>
    /// <returns>Matching requests ordered by <c>RequestedAt</c> descending.</returns>
    Task<IReadOnlyList<GdprRequest>> ListAsync(string? userId = null);
}
