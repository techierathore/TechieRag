namespace TechieDesk.Services.Data;

/// <summary>
/// Bound view of the <c>AppDb</c> configuration section selecting the app-database
/// provider and connection string (BRD-102).
/// </summary>
public sealed class AppDbOptions
{
    /// <summary>Name of the configuration section this type binds to.</summary>
    public const string SectionName = "AppDb";

    /// <summary>Provider name: <c>Sqlite</c> (default) or <c>Postgres</c>.</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>
    /// Provider connection string. When empty for SQLite, a default database file
    /// under the app's <c>data/</c> directory is used.
    /// </summary>
    public string? ConnectionString { get; set; }
}
