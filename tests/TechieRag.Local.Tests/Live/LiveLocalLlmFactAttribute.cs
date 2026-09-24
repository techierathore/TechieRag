using TechieRag.Embedded;
using TechieRag.Local.Runtime;
using Xunit;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that runs a real local model (REQ-RAG-066 / BRD-107).
/// </summary>
/// <remarks>
/// Skipped with a printed reason unless this platform has a runtime and the model under test is fully
/// downloaded in the model root. The model is <see cref="ModelVariable"/> when set, otherwise the
/// platform default. The tests never download: staging a multi-gigabyte model is the host's choice.
/// </remarks>
public sealed class LiveLocalLlmFactAttribute : FactAttribute
{
    /// <summary>Environment variable naming the model id to test (default: the platform default model).</summary>
    public const string ModelVariable = "TechieRagLiveLocalModel";

    /// <summary>Initializes a new instance of the <see cref="LiveLocalLlmFactAttribute"/> class.</summary>
    public LiveLocalLlmFactAttribute()
    {
        var reason = SkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }

    /// <summary>Gets the model the live tests run.</summary>
    public static LocalModel ModelToTest =>
        LocalModel.FromId(Environment.GetEnvironmentVariable(ModelVariable)) ?? LocalModel.PlatformDefault;

    /// <summary>Works out why the live tests cannot run here, or null when they can.</summary>
    /// <returns>The reason, or null.</returns>
    internal static string? SkipReason()
    {
        var runtime = LocalRuntimeSelector.ForCurrentPlatform();
        if (runtime is null)
        {
            return "Live local-model test. " + LocalRuntimeSelector.NoRuntimeMessage;
        }

        var model = ModelToTest;
        var variant = model.FindVariant(runtime.Format);
        if (variant is null)
        {
            return $"Live local-model test. '{model.Id}' has no file set for this platform's runtime.";
        }

        var directory = model.GetDirectory(variant);
        return ModelDownloadService.IsComplete(directory, variant.GetDownloadFiles(null))
            ? null
            : $"Live local-model test. '{model.Id}' is not downloaded at {directory}. Download it once (UseLocalLlm with terms accepted) "
              + $"or set {ModelVariable} to a model that is, then run the suite again.";
    }
}
