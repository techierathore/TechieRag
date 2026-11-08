using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that talks to a real PostgreSQL server with pgvector
/// installed (REQ-RAG-044 / BRD-125, REQ-RAG-152 / BRD-210).
/// </summary>
/// <remarks>
/// <para><b>Configured, never discovered.</b> The connection string comes from the git-ignored
/// <c>livetests.settings.json</c> beside the tests (<see cref="LiveTestSettings"/>), never from an
/// environment variable (owner's rule, 2026-11-07). The harness writes that file when it starts the server
/// in Docker with its own password: <c>tests/TechieRag.Tests/livetests/start-postgres.sh</c>. This suite does
/// NOT go looking for a server on <c>localhost:5432</c>.</para>
/// <para><b>Skipped, not silently absent.</b> Without the file these report as skipped with the reason
/// below, so "PgVector has never run against a real Postgres" stays visible in the test output.</para>
/// </remarks>
public sealed class LivePostgresFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LivePostgres";

    /// <summary>Initializes a new instance of the <see cref="LivePostgresFactAttribute"/> class.</summary>
    public LivePostgresFactAttribute()
    {
        if (ConnectionString is null)
        {
            Skip = $"No Postgres is configured for this host: {LiveTestSettings.FileName} beside the tests "
                + "is absent or names no Postgres:ConnectionString. Start the server with "
                + "tests/TechieRag.Tests/livetests/start-postgres.sh, which writes the file.";
        }
    }

    /// <summary>Gets the configured connection string, or null when this host offers no Postgres.</summary>
    public static string? ConnectionString => LiveTestSettings.PostgresConnectionString;
}
