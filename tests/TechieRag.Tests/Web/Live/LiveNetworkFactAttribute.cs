using Xunit;

namespace TechieRag.Tests.Web.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that talks to the real internet (REQ-RAG-031 / BRD-112).
/// </summary>
/// <remarks>
/// <para><b>Opt-in, and it has to be.</b> Every other test in this suite is hermetic. A test that
/// reaches a third-party host is flaky by construction — the host can be down, rate-limit, change
/// its markup, or simply not be reachable from a build agent — so leaving these in the default run
/// would trade a suite that means something for one that goes red for reasons nobody caused. They
/// are skipped unless <c>TechieRagLiveNetworkTests</c> is set, and additionally carry
/// <c>[Trait("Category", "LiveNetwork")]</c> so they can be selected or excluded by filter.</para>
/// <para><b>Skipped, not silently absent.</b> The default run reports them as skipped with the
/// reason below, so the gap is visible in the test output rather than being something a reader has
/// to know to go looking for.</para>
/// <para><b>Why an environment variable.</b> The coding standards route configuration through
/// <c>IConfiguration</c>, but an xUnit discovery attribute runs before any host exists, so the
/// process environment is the only channel available. The name follows the standards' PascalCase,
/// no-separators rule.</para>
/// </remarks>
public sealed class LiveNetworkFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveNetwork";

    /// <summary>Environment variable that enables the live-network tests.</summary>
    public const string OptInVariable = "TechieRagLiveNetworkTests";

    /// <summary>Initializes a new instance of the <see cref="LiveNetworkFactAttribute"/> class.</summary>
    public LiveNetworkFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = $"Live-network test. Set {OptInVariable}=1 to run it.";
        }
    }

    /// <summary>Gets a value indicating whether live-network tests were opted into.</summary>
    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(OptInVariable) is { Length: > 0 } value
        && !value.Equals("0", StringComparison.Ordinal)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
