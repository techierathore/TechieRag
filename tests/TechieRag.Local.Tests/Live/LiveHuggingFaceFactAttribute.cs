using TechieRag.Local.Runtime;
using Xunit;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that downloads a real model from Hugging Face (REQ-RAG-108).
/// </summary>
/// <remarks>
/// Skipped with a printed reason unless <see cref="OptInVariable"/> is <c>1</c> and this platform has a
/// runtime: the download is about 866 MB, so it runs only when asked for. The files go to a throwaway
/// model root under <c>tests/.artifacts/</c>, kept between runs so a second run is offline.
/// </remarks>
public sealed class LiveHuggingFaceFactAttribute : FactAttribute
{
    /// <summary>Environment variable that opts in to the Hugging Face download (value <c>1</c>).</summary>
    public const string OptInVariable = "TechieRagLiveHuggingFace";

    /// <summary>Initializes a new instance of the <see cref="LiveHuggingFaceFactAttribute"/> class.</summary>
    public LiveHuggingFaceFactAttribute()
    {
        var reason = SkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }

    /// <summary>Works out why the live Hugging Face test cannot run here, or null when it can.</summary>
    /// <returns>The reason, or null.</returns>
    internal static string? SkipReason()
    {
        if (LocalRuntimeSelector.ForCurrentPlatform() is null)
        {
            return "Live Hugging Face test. " + LocalRuntimeSelector.NoRuntimeMessage;
        }

        return Environment.GetEnvironmentVariable(OptInVariable) == "1"
            ? null
            : $"Live Hugging Face test. It downloads about 866 MB from huggingface.co; set {OptInVariable}=1 to run it.";
    }
}
