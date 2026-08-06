using System.Data;

namespace TechieDesk.Services.Data;

/// <summary>
/// Creates ADO.NET connections to the app database so repositories never own a
/// connection string of their own (Dapper-only data access, BRD-102).
/// </summary>
/// <remarks>
/// The <c>Provider</c> property and the <c>AppDbProvider</c> enum were removed on
/// 2026-07-28 with the PostgreSQL path (REQ-FN-029): BRD-102 was amended on
/// 2026-07-26 to Dapper-over-SQLite only, so a provider discriminator on this
/// interface had exactly one value and no caller ever read it.
/// </remarks>
public interface IAppDbConnectionFactory
{
    /// <summary>
    /// Creates a new, closed SQLite connection. Callers (or Dapper) open it;
    /// dispose after use.
    /// </summary>
    /// <returns>A new <see cref="IDbConnection"/> for the configured database.</returns>
    IDbConnection CreateConnection();
}
