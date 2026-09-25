using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// Guards that the library's data access stays free of Reflection.Emit (REQ-FN-056,
/// MISS-TechieRag-20260924-09).
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> A Mac Catalyst Release build of the probe embedded its texts and then
/// threw <c>PlatformNotSupportedException: PlatformNotSupported_ReflectionEmit</c> from
/// <c>Dapper.SqlMapper.CreateParamInfoGenerator</c> inside <c>SqliteVecStore.UpsertBatchAsync</c>.
/// iOS and Mac Catalyst Release builds are compiled ahead of time and have no Reflection.Emit;
/// Debug builds run the interpreter, which is why every Debug smoke and every test on a desktop JIT
/// was green.</para>
/// <para><b>Why a structural test.</b> This process runs on a JIT that has Reflection.Emit, so no
/// behavioural test here can reproduce the failure. What it can do is fail the moment the thing that
/// caused it comes back: a Dapper reference, or any direct use of <c>System.Reflection.Emit</c>, in
/// the core assembly. The decisive proof stays the Mac Catalyst Release smoke.</para>
/// </remarks>
public class DataAccessIsAotSafeTests
{
    /// <summary>
    /// REQ-FN-056: the core assembly (the SQLite vector store and the relational conversation and
    /// workspace stores) does not reference Dapper, whose mappers are generated with Reflection.Emit.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-056 CoreAssemblyDoesNotReferenceDapper")]
    public void CoreAssemblyDoesNotReferenceDapper()
    {
        var references = typeof(SqliteVecStore).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => string.Equals(reference.Name, "Dapper", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// REQ-FN-056: no type in the core assembly uses a <c>System.Reflection.Emit</c> type directly
    /// (<c>DynamicMethod</c>, <c>ILGenerator</c>, <c>AssemblyBuilder</c>...), which would throw in an
    /// ahead-of-time compiled iOS or Mac Catalyst app.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-056 CoreAssemblyUsesNoReflectionEmit")]
    public void CoreAssemblyUsesNoReflectionEmit()
    {
        using var stream = File.OpenRead(typeof(SqliteVecStore).Assembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var emitTypes = metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Where(type => metadata.GetString(type.Namespace) == "System.Reflection.Emit")
            .Select(type => metadata.GetString(type.Name))
            .ToList();

        Assert.Empty(emitTypes);
    }
}
