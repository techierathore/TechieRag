namespace TechieDesk.Services.Data;

/// <summary>
/// Relational providers supported by the TechieDesk app database (BRD-102).
/// </summary>
public enum AppDbProvider
{
    /// <summary>SQLite file database (default; zero-install).</summary>
    Sqlite,

    /// <summary>PostgreSQL server database (scale-out option, pairs with pgvector).</summary>
    Postgres
}
