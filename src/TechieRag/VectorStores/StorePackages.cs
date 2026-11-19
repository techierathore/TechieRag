using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;

namespace TechieRag.VectorStores;

/// <summary>
/// Where the builder finds the stores that live in sibling packages since 1.3.0: PostgreSQL in
/// <c>TechieRag.PgVector</c>, Qdrant in <c>TechieRag.Qdrant</c> (REQ-RAG-168, TrStudio TR-RAG-006).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Before 1.3.0 the core package carried Npgsql, Pgvector and Qdrant.Client (with three gRPC
/// libraries), so an app that used only SQLite shipped about 4 MB of clients it never called.</para>
/// <para><b>How the builder finds them.</b> Each sibling package registers its factories here from a module
/// initializer. The builder asks for one only when the configuration names that store; if the package's
/// assembly has not been loaded yet, the builder loads it by name (it is in the app's output when the package is
/// referenced), which runs the initializer. When the assembly is not there, the builder throws one message that
/// names the package to add. <c>UsePgVector</c>, <c>UsePostgres</c>, <c>UseQdrant</c>,
/// <c>WithPersistence(StoreProvider.Postgres)</c> and the configuration keys keep their names, so a host's code
/// compiles unchanged and only gains a package reference.</para>
/// </remarks>
internal static class StorePackages
{
    /// <summary>The package that holds the PostgreSQL stores.</summary>
    internal const string PgVectorPackage = "TechieRag.PgVector";

    /// <summary>The package that holds the Qdrant store.</summary>
    internal const string QdrantPackage = "TechieRag.Qdrant";

    /// <summary>Creates a pgvector store: connection string, logger factory (may be null), dimensions.</summary>
    internal static Func<string, ILoggerFactory?, int, IVectorStore>? PgVectorStore { get; set; }

    /// <summary>Creates the PostgreSQL conversation store from a connection string.</summary>
    internal static Func<string, IConversationStore>? PostgresConversationStore { get; set; }

    /// <summary>Creates the PostgreSQL workspace store from a connection string.</summary>
    internal static Func<string, IWorkspaceStore>? PostgresWorkspaceStore { get; set; }

    /// <summary>Creates the PostgreSQL token-usage log from a connection string.</summary>
    internal static Func<string, ITokenUsageLog>? PostgresTokenUsageLog { get; set; }

    /// <summary>Creates a Qdrant store: endpoint, dimensions, API key (may be null).</summary>
    internal static Func<string, int, string?, IVectorStore>? QdrantStore { get; set; }

    /// <summary>
    /// Returns a factory a sibling package registers, loading that package's assembly first when needed.
    /// </summary>
    /// <typeparam name="T">The factory type.</typeparam>
    /// <param name="read">Reads the factory from this class.</param>
    /// <param name="package">The package (and assembly) name.</param>
    /// <param name="feature">What the caller asked for, for the message.</param>
    /// <returns>The factory.</returns>
    /// <exception cref="InvalidOperationException">The package is not referenced by the app.</exception>
    internal static T Require<T>(Func<T?> read, string package, string feature)
        where T : class
    {
        var factory = read();
        if (factory is not null)
        {
            return factory;
        }

        TryLoad(package);
        return read() ?? throw new InvalidOperationException(
            $"{feature} is in the {package} package since TechieRag 1.3.0. Add a reference to {package}, "
            + "the same version as TechieRag; no code change is needed.");
    }

    private static void TryLoad(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            // Not referenced by the app: Require names the package.
        }
    }
}
