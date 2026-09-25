using Xunit;

namespace TechieRag.Agents.Tests.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that drives a real LM Studio server (REQ-RAG-016 / BRD-84).
/// </summary>
/// <remarks>
/// Skipped with a reason unless <see cref="ModelVariable"/> names a tool-capable model loaded in LM
/// Studio; <see cref="EndpointVariable"/> overrides the default <c>http://localhost:1234</c>. Environment
/// variables because an xUnit discovery attribute runs before any host exists; the names follow the
/// suite's PascalCase convention (<c>TechieRagLiveNetworkTests</c>).
/// </remarks>
public sealed class LiveLmStudioFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveLmStudio";

    /// <summary>Environment variable naming the LM Studio model id to use.</summary>
    public const string ModelVariable = "TechieRagLiveLmStudioModel";

    /// <summary>Environment variable overriding the LM Studio endpoint.</summary>
    public const string EndpointVariable = "TechieRagLiveLmStudioEndpoint";

    /// <summary>Initializes a new instance of the <see cref="LiveLmStudioFactAttribute"/> class.</summary>
    public LiveLmStudioFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            Skip = $"Live LM Studio test. Load a tool-capable model in LM Studio and set {ModelVariable}=<model id> to run it.";
        }
    }

    /// <summary>Gets the configured model id, or null.</summary>
    public static string? Model => Environment.GetEnvironmentVariable(ModelVariable);

    /// <summary>Gets the configured endpoint.</summary>
    public static string Endpoint => Environment.GetEnvironmentVariable(EndpointVariable) is { Length: > 0 } value ? value : "http://localhost:1234";
}
