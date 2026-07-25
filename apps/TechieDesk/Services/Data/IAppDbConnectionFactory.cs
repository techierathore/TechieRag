using System.Data;

namespace TechieDesk.Services.Data;

/// <summary>
/// Creates ADO.NET connections for the configured app-database provider so
/// repositories stay provider-agnostic (Dapper-only data access, BRD-102).
/// </summary>
public interface IAppDbConnectionFactory
{
    /// <summary>The active provider resolved from configuration.</summary>
    AppDbProvider Provider { get; }

    /// <summary>
    /// Creates a new, closed connection for the active provider. Callers (or Dapper)
    /// open it; dispose after use.
    /// </summary>
    /// <returns>A new <see cref="IDbConnection"/> for the configured database.</returns>
    IDbConnection CreateConnection();
}
