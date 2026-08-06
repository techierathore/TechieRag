using Microsoft.ML.OnnxRuntime;

namespace TechieRag.Embedded;

/// <summary>
/// The result of asking whether ONNX Runtime's native library can be loaded in this process.
/// </summary>
/// <param name="Loaded">Whether the native library loaded and an environment was created.</param>
/// <param name="Providers">The execution providers ONNX Runtime reports, empty when it did not load.</param>
/// <param name="Failure">The flattened failure text when it did not load, otherwise <see langword="null"/>.</param>
public sealed record OnnxRuntimeStatus(bool Loaded, IReadOnlyList<string> Providers, string? Failure)
{
    /// <summary>Renders the status as one log-friendly line.</summary>
    /// <returns>A sentence naming either the providers or the failure.</returns>
    public string Describe() => Loaded
        ? $"ONNX Runtime native library loaded (TR-RAG-025); execution providers: {string.Join(", ", Providers)}"
        : $"ONNX Runtime native library did NOT load (TR-RAG-025): {Failure}";
}

/// <summary>
/// Installs <see cref="OnnxNativeLibraryResolver"/> deliberately and reports whether ONNX Runtime's
/// native library then loads — the startup check a non-MAUI host performs (TR-RAG-025, REQ-FN-042).
/// </summary>
/// <remarks>
/// <para><b>Why a host needs this at all.</b> The resolver is installed by a module initializer, so
/// it takes effect only once <c>TechieRag.Embedded</c> has been loaded. In the background scheduler
/// helper that load happened incidentally, deep inside the first call that asked
/// <c>TechieRagManager</c> for an embedding — which means the fix held by ordering luck rather than by
/// construction, and any future code path that reached <c>Microsoft.ML.OnnxRuntime</c> first would
/// have re-broken it with the original <c>DllNotFoundException</c>. Calling this from
/// <c>Main</c> makes the install the first thing the process does.</para>
/// <para><b>It reports rather than throws.</b> Ingestion is the helper's main purpose but not its
/// only one — database maintenance needs no embedding model — so a host that cannot embed should log
/// the reason and keep running the jobs it can. Every caller therefore gets a status object, never an
/// exception.</para>
/// <para><b>Loading the environment is the whole test.</b> <c>OrtEnv.Instance()</c> runs
/// <c>NativeMethods</c>' type initializer, which is exactly where TR-RAG-025 failed. No model files
/// are touched, so the check costs nothing and works on a machine that has never downloaded one.</para>
/// </remarks>
public static class OnnxRuntimeProbe
{
    /// <summary>
    /// Installs the native-library resolver, then reports whether ONNX Runtime loads here.
    /// </summary>
    /// <returns>The status, whose <see cref="OnnxRuntimeStatus.Describe"/> is meant for a log line.</returns>
    /// <remarks>
    /// Safe to call repeatedly and from anywhere: the install is guarded by an interlocked flag and
    /// <c>OrtEnv</c> is a process-wide singleton, so subsequent calls only re-read the provider list.
    /// </remarks>
    public static OnnxRuntimeStatus Check()
    {
        // Idempotent. The module initializer has almost certainly run already — this is what makes
        // that "almost" unnecessary.
        OnnxNativeLibraryResolver.Install();

        try
        {
            var providers = OrtEnv.Instance().GetAvailableProviders();
            return new OnnxRuntimeStatus(true, [.. providers], null);
        }
        catch (Exception exception)
        {
            // The interesting text is in the inner DllNotFoundException, not the
            // TypeInitializationException wrapping it, so the chain is flattened rather than
            // reported by its outermost frame.
            return new OnnxRuntimeStatus(false, [], Flatten(exception));
        }
    }

    /// <summary>Joins an exception chain into one readable line.</summary>
    /// <param name="exception">The exception to flatten.</param>
    /// <returns>Each message in the chain, outermost first.</returns>
    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" ---> ", messages);
    }
}
