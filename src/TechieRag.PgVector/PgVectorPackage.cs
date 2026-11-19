using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Persistence;
using TechieRag.VectorStores;

namespace TechieRag.PgVector;

/// <summary>
/// Registers this package's PostgreSQL stores with the TechieRag builder when the assembly loads
/// (REQ-RAG-168, TrStudio TR-RAG-006).
/// </summary>
/// <remarks>
/// A host never calls this: <c>UsePgVector</c>, <c>UsePostgres</c> and <c>WithPersistence(StoreProvider.Postgres)</c>
/// find the package by name and load it, which runs this initializer.
/// </remarks>
internal static class PgVectorPackage
{
    /// <summary>Hands the builder a factory for each PostgreSQL store.</summary>
    [ModuleInitializer]
    internal static void Register()
    {
        StorePackages.PgVectorStore = static (connectionString, loggerFactory, dimensions) =>
            new PgVectorStore(
                connectionString,
                loggerFactory?.CreateLogger<PgVectorStore>() ?? NullLogger<PgVectorStore>.Instance,
                dimensions);
        StorePackages.PostgresConversationStore = static connectionString => new PostgresConversationStore(connectionString);
        StorePackages.PostgresWorkspaceStore = static connectionString => new PostgresWorkspaceStore(connectionString);
        StorePackages.PostgresTokenUsageLog = static connectionString => new PostgresTokenUsageLog(connectionString);
    }
}
