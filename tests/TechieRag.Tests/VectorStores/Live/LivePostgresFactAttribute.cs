using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that talks to a real PostgreSQL server with pgvector
/// installed (REQ-RAG-044 / BRD-125).
/// </summary>
/// <remarks>
/// <para><b>Configured, never discovered.</b> The connection string comes from the environment
/// variable named by <c>runtimeVerification.services.postgres.connectionStringEnv</c> in
/// <c>.tfcore/core-config.yaml</c> — the declared-endpoint rule in
/// <c>.tfcore/tasks/_smoke-test-policy.md</c>. This suite does NOT go looking for a server on
/// <c>localhost:5432</c>: a host either offers one and says so, or it does not.</para>
/// <para><b>Skipped, not silently absent.</b> With the variable unset these report as skipped with
/// the reason below, so "PgVector has never run against a real Postgres" stays visible in the test
/// output instead of being a gap a reader has to already know about. That visibility is the point —
/// it is the open half of BRD-125's acceptance.</para>
/// <para><b>Why an environment variable rather than the YAML directly.</b> An xUnit discovery
/// attribute runs before any host or configuration binder exists, so the process environment is the
/// only channel available. It is also the rule for secrets: a connection string carries a password
/// and must never be written into a committed file (REQ-NFR-002). The name follows the coding
/// standards' PascalCase, no-separators convention, matching <c>TechieRagLiveNetworkTests</c>.</para>
/// </remarks>
public sealed class LivePostgresFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LivePostgres";

    /// <summary>
    /// Environment variable holding the connection string, named by
    /// <c>runtimeVerification.services.postgres.connectionStringEnv</c>.
    /// </summary>
    public const string ConnectionStringVariable = "TechieRagTestPostgres";

    /// <summary>Initializes a new instance of the <see cref="LivePostgresFactAttribute"/> class.</summary>
    public LivePostgresFactAttribute()
    {
        if (ConnectionString is null)
        {
            Skip = $"No Postgres is configured for this host. Set {ConnectionStringVariable} to the "
                + "connection string named by runtimeVerification.services.postgres.connectionStringEnv "
                + "(see docs/VERIFICATION-ENDPOINTS.md).";
        }
    }

    /// <summary>Gets the configured connection string, or null when this host offers no Postgres.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value
            ? value
            : null;
}
