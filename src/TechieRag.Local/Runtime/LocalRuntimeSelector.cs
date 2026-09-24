namespace TechieRag.Local.Runtime;

/// <summary>
/// Picks the runtime for the platform the process runs on (REQ-RAG-058 / BRD-97).
/// </summary>
/// <remarks>
/// <para><b>No runtime is chosen yet.</b> <c>DECISIONS.md</c> 2026-09-24 item 3 says the runtime
/// (LLamaSharp or ONNX Runtime GenAI) is chosen per platform after a measured comparison recorded
/// there with numbers, before any runtime is built. The comparison and a recommendation are in
/// <c>docs/TechieRag-Decision-Request.md</c>; until the owner decides, every platform returns null and
/// the provider refuses to load with a message saying so. The decision adds one class per runtime
/// and one line per platform here; nothing above this seam changes.</para>
/// </remarks>
internal static class LocalRuntimeSelector
{
    /// <summary>The message a load gives while no runtime is chosen for the platform.</summary>
    internal const string NoRuntimeMessage =
        "No local inference runtime is available for this platform yet. TechieRag.Local runs a model once "
        + "a runtime (LLamaSharp or ONNX Runtime GenAI) has been chosen for the platform; that choice is pending.";

    /// <summary>Gets the runtime for the current platform, or null when none is chosen.</summary>
    /// <returns>The runtime, or null.</returns>
    internal static ILocalLlmRuntime? ForCurrentPlatform() => null;
}
