using Xunit;

namespace TechieDesk.Tests.Web.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that talks to the real internet and runs the real
/// embedding model (REQ-RAG-016/017/018).
/// </summary>
/// <remarks>
/// <para>Opt-in for two reasons, either of which alone would be enough. It reaches third-party hosts,
/// so it is flaky by construction; and it loads the BGE-M3 ONNX model, which is a 2.3 GB download on
/// first use and seconds of CPU on every use. Neither belongs in the run that gates a change.</para>
/// <para>The gate is the same environment variable the library suite uses, so one switch turns on
/// every live test in the repository rather than one per project.</para>
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
