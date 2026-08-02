using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TechieDesk.Tests.Support;

/// <summary>
/// Teaches the test host where ONNX Runtime's native library actually is on this platform
/// (workaround for TR-RAG-016).
/// </summary>
/// <remarks>
/// <para><b>The defect being worked around.</b> <c>Microsoft.ML.OnnxRuntime.Managed</c>'s
/// <c>net8.0</c> assembly — the one a plain <c>net10.0</c> project resolves — declares its
/// <c>DllImport</c> name as the literal <c>"onnxruntime.dll"</c>. On macOS and Linux the package
/// ships <c>libonnxruntime.dylib</c> / <c>libonnxruntime.so</c>, and .NET's probing for
/// <c>"onnxruntime.dll"</c> tries <c>onnxruntime.dll</c>, <c>libonnxruntime.dll</c>,
/// <c>onnxruntime.dll.dylib</c> and <c>libonnxruntime.dll.dylib</c> — none of which is the file that
/// is sitting right there. So <c>TechieRag.Embedded.UseEmbedded()</c> throws
/// <c>DllNotFoundException</c> for every non-Windows consumer whose target framework is plain
/// <c>net10.0</c>. The MAUI heads are unaffected because their platform TFMs resolve a different
/// managed assembly.</para>
/// <para><b>Why it lives in the test project.</b> The fix belongs in <c>TechieRag.Embedded</c>, which
/// this cluster does not own; it is logged as TR-RAG-016 in
/// <c>docs/TechieDesk-TechieRag-Feedback.md</c>. Without a workaround the live end-to-end suite could
/// not run the real embedding model at all on this host, and "we could not run it" is precisely the
/// gap this cluster exists to close. The resolver only ever redirects a name the runtime has already
/// failed to find, so it cannot mask a different problem.</para>
/// <para>Runs as a module initializer so it is installed before any test touches ONNX Runtime — the
/// resolver has to be registered before the type's static constructor runs, and a static constructor
/// only runs once.</para>
/// </remarks>
public static class OnnxRuntimeNativeLibraryResolver
{
    /// <summary>The name the managed assembly asks for, which exists on Windows only.</summary>
    private const string RequestedName = "onnxruntime.dll";

    /// <summary>Installs the resolver. Safe to call more than once.</summary>
    [ModuleInitializer]
    public static void Install()
    {
        Assembly onnxRuntime;
        try
        {
            onnxRuntime = Assembly.Load("Microsoft.ML.OnnxRuntime");
        }
        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException)
        {
            // Nothing in this test run uses ONNX Runtime. Not a failure.
            return;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(onnxRuntime, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver is already installed for this assembly; the first one wins and that is fine.
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals(RequestedName, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        // Returning zero hands the request back to the default probing, so a platform this method
        // does not know about fails with the runtime's own message rather than a misleading one.
        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var fileName = OperatingSystem.IsWindows()
            ? "onnxruntime.dll"
            : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";

        var root = AppContext.BaseDirectory;

        yield return Path.Combine(root, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);

        foreach (var identifier in new[] { "osx-arm64", "osx-x64", "linux-arm64", "linux-x64", "win-x64", "win-arm64" })
        {
            yield return Path.Combine(root, "runtimes", identifier, "native", fileName);
        }

        yield return Path.Combine(root, fileName);
    }
}
