using System.Runtime.CompilerServices;
using TechieRag.VectorStores;

namespace TechieRag.Qdrant;

/// <summary>
/// Registers this package's Qdrant store with the TechieRag builder when the assembly loads
/// (REQ-RAG-168, TrStudio TR-RAG-006).
/// </summary>
/// <remarks>A host never calls this: <c>UseQdrant</c> finds the package by name and loads it, which runs this initializer.</remarks>
internal static class QdrantPackage
{
    /// <summary>Hands the builder a factory for the Qdrant store.</summary>
    [ModuleInitializer]
    internal static void Register() =>
        StorePackages.QdrantStore = static (endpoint, dimensions, apiKey) => new QdrantStore(endpoint, dimensions, apiKey: apiKey);
}
