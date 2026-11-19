using Microsoft.Data.Sqlite;

namespace TechieRag.VectorStores;

/// <summary>
/// Turns what a caller gave for a SQLite database into a connection string (REQ-RAG-167, TrStudio TR-RAG-005).
/// </summary>
internal static class SqliteConnectionStrings
{
    /// <summary>
    /// Returns a connection string as it is, and wraps a file path in <c>Data Source=</c>.
    /// </summary>
    /// <param name="pathOrConnectionString">A file path, or a connection string that names a data source.</param>
    /// <returns>The connection string.</returns>
    /// <remarks>
    /// A value counts as a connection string when SQLite's own parser reads it and finds a data source, as in
    /// <c>Data Source=app.db</c> or <c>Filename=app.db;Cache=Shared</c>. Anything else, a bare path included, is a
    /// path and used exactly as given (REQ-RAG-122).
    /// </remarks>
    public static string FromPathOrConnectionString(string pathOrConnectionString)
    {
        if (pathOrConnectionString.Contains('='))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(new SqliteConnectionStringBuilder(pathOrConnectionString).DataSource))
                {
                    return pathOrConnectionString;
                }
            }
            catch (ArgumentException)
            {
                // Not a connection string SQLite understands (a path with '=' in it, for example): a path.
            }
            catch (FormatException)
            {
                // A known key with an unreadable value: a path, as before.
            }
        }

        return "Data Source=" + pathOrConnectionString;
    }
}
