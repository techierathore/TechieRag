using TechieRag.Local.Runtime;
using Xunit;
using Xunit.Sdk;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// A live test of the local picture model (REQ-RAG-149, REQ-RAG-150 / BRD-207, BRD-208), gated like the other
/// live local-model tests (owner decision 2026-11-05): it runs only when <see cref="LocalModel.Phi35VisionInstruct"/>
/// is already downloaded on this machine, and a machine with too little free memory skips it rather than failing.
/// </summary>
[XunitTestCaseDiscoverer("TechieRag.Local.Tests.Conformance.ShortMemorySkippingFactDiscoverer", "TechieRag.Local.Tests")]
public sealed class LiveVisionFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping when the picture model is not on disk.</summary>
    public LiveVisionFactAttribute()
    {
        var reason = SkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }

    /// <summary>Gets why the test is skipped on this machine, or null when it runs.</summary>
    /// <returns>The reason, or null.</returns>
    internal static string? SkipReason()
    {
        var runtime = LocalRuntimeSelector.ForCurrentPlatform();
        if (runtime is null)
        {
            return "Live local picture test. " + LocalRuntimeSelector.NoRuntimeMessage;
        }

        var model = LocalModel.Phi35VisionInstruct;
        var variant = model.FindVariant(runtime.Format)!;
        var directory = model.GetDirectory(variant);
        return variant.IsOnDisk(directory)
            ? null
            : $"Live local picture test. '{model.Id}' is not downloaded at {directory}. Run the download test once with "
              + $"{LiveVisionDownloadFactAttribute.OptInVariable}=1, then run the suite again.";
    }
}

/// <summary>
/// The live download of the local picture model through the library (REQ-RAG-145 / BRD-203). It fetches about
/// 3.2 GB from huggingface.co, so it runs only when <see cref="OptInVariable"/> is <c>1</c>, the way the live
/// Hugging Face test is opted into.
/// </summary>
[XunitTestCaseDiscoverer("TechieRag.Local.Tests.Conformance.ShortMemorySkippingFactDiscoverer", "TechieRag.Local.Tests")]
public sealed class LiveVisionDownloadFactAttribute : FactAttribute
{
    /// <summary>The environment variable that opts into the download.</summary>
    public const string OptInVariable = "TechieRagLiveVisionDownload";

    /// <summary>Creates the attribute, skipping unless the download is opted into.</summary>
    public LiveVisionDownloadFactAttribute()
    {
        if (LocalRuntimeSelector.ForCurrentPlatform() is null)
        {
            Skip = "Live local picture download. " + LocalRuntimeSelector.NoRuntimeMessage;
        }
        else if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
        {
            Skip = $"Live local picture download. It downloads about 3.2 GB from huggingface.co; set {OptInVariable}=1 to run it.";
        }
    }
}
