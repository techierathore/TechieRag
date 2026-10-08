using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// A call written against 1.0.8 keeps its meaning, and a host can tell an unset vector-store connection
/// string from a set one (Sevak TR-RAG-049 against REQ-RAG-122 / BRD-182).
/// </summary>
public sealed class PersistenceOverloadTests
{
    /// <summary>
    /// <c>WithPersistence(StoreProvider.Sqlite, "Data Source=…")</c>, the 1.0.8 two-argument call, binds to the
    /// connection string again; in 1.1.2 it became the user id and the tables went to the per-user folder.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 TwoArgumentPersistenceCallKeepsItsConnectionString")]
    public void TwoArgumentPersistenceCallKeepsItsConnectionString()
    {
        var builder = new TechieRagBuilder().WithPersistence(StoreProvider.Sqlite, "Data Source=/app/data/sevak.db");

        var persistence = builder.GetConfig().Persistence;
        Assert.Equal("Data Source=/app/data/sevak.db", persistence.ConnectionString);
        Assert.Equal("default", persistence.DefaultUserId);
    }

    /// <summary>
    /// A host that wants only a user with the per-app default database names the argument, and gets the
    /// default database (no connection string) for that user.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 NamedUserWithoutConnectionStringUsesDefaultDatabase")]
    public void NamedUserWithoutConnectionStringUsesDefaultDatabase()
    {
        var builder = new TechieRagBuilder().WithPersistence(StoreProvider.Sqlite, defaultUserId: "alice");

        var persistence = builder.GetConfig().Persistence;
        Assert.Null(persistence.ConnectionString);
        Assert.Equal("alice", persistence.DefaultUserId);
        Assert.Equal(StoreProvider.Sqlite, persistence.Provider);
    }

    /// <summary>Only SQLite has a default database: PostgreSQL with no connection string is refused.</summary>
    [Fact(DisplayName = "REQ-RAG-122 PostgresWithoutConnectionStringIsRefused")]
    public void PostgresWithoutConnectionStringIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new TechieRagBuilder().WithPersistence(StoreProvider.Postgres, connectionString: null));
        Assert.Throws<ArgumentException>(() => new TechieRagBuilder().WithPersistence(StoreProvider.Sqlite, string.Empty));
    }

    /// <summary>
    /// <c>VectorStoreConfig.IsConnectionStringSet</c> is public: false while the getter returns the per-app
    /// default, true once a value, relative or absolute, was set.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 HostCanTellUnsetVectorStoreConnectionString")]
    public void HostCanTellUnsetVectorStoreConnectionString()
    {
        var config = new TechieRagConfig();
        Assert.False(config.VectorStore.IsConnectionStringSet);

        config.VectorStore.ConnectionString = "Data Source=techierag.db";
        Assert.True(config.VectorStore.IsConnectionStringSet);
        Assert.Equal("Data Source=techierag.db", config.VectorStore.ConnectionString);
    }
}
