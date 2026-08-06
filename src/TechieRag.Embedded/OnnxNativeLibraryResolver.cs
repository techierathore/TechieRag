using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace TechieRag.Embedded;

/// <summary>
/// Teaches .NET how to find ONNX Runtime's native library on macOS and Linux (TR-RAG-025).
/// </summary>
/// <remarks>
/// <para><b>The defect this fixes.</b> <c>Microsoft.ML.OnnxRuntime</c> declares its P/Invokes against
/// the literal name <c>onnxruntime.dll</c> — extension included. Because the name already carries an
/// extension, .NET's default probing never substitutes the platform one: it tries
/// <c>onnxruntime.dll</c> and <c>libonnxruntime.dll</c>, but never <c>libonnxruntime.dylib</c>, which
/// is what the package actually ships at <c>runtimes/osx-arm64/native/</c>. The result on a plain
/// <c>net10.0</c> host is:</para>
/// <code>
/// System.TypeInitializationException: The type initializer for
///   'Microsoft.ML.OnnxRuntime.NativeMethods' threw an exception.
///  ---> System.DllNotFoundException: Unable to load shared library 'onnxruntime.dll'
/// </code>
/// <para>Measured, not inferred: the failing probe listed
/// <c>…/runtimes/osx-arm64/native/libonnxruntime.dll</c> among the paths it tried, next to the real
/// <c>libonnxruntime.dylib</c>. So the native asset was present and correctly laid out the whole
/// time — only the file name was wrong. This is why the scheduler helper (REQ-FN-042) could not
/// ingest anything on macOS while the MAUI head was unaffected.</para>
/// <para><b>Why a module initializer.</b> The resolver has to be installed before the first P/Invoke
/// runs, and a host cannot reliably do that — the type initializer fires on whichever ONNX call
/// happens first, deep inside the library. Running at module load means every host (console,
/// service, test, MAUI head) is fixed by referencing this assembly, with nothing to remember.</para>
/// <para><b>Known limit — load order, stated precisely.</b> A module initializer runs when THIS
/// assembly is loaded, so the fix applies from that moment on. Code that reaches
/// <c>Microsoft.ML.OnnxRuntime</c> directly, without having touched any <c>TechieRag.Embedded</c>
/// type first, still gets the original <c>DllNotFoundException</c> — verified, not assumed: it is
/// what <c>OnnxNativeLoadTests</c> demonstrates. This is not a problem for the embedded provider's
/// own consumers, who by definition go through this assembly to get an embedding, but it does mean
/// this class cannot be described as fixing ONNX Runtime globally for a process. Making it
/// unconditional would take a startup hook or a renamed copy of the native file, and neither is
/// worth what it costs here.</para>
/// <para><b>Why the MAUI head is unaffected either way.</b> On Mac Catalyst ONNX Runtime is statically
/// linked into the app binary (see the <c>NativeReference</c> in <c>TechieDesk.csproj</c>), so there is
/// no dylib on disk to find. This resolver returns <see cref="IntPtr.Zero"/> for that case, which
/// hands the request straight back to the default resolution that already works there. It can only
/// add a load path, never remove one.</para>
/// </remarks>
internal static class OnnxNativeLibraryResolver
{
    /// <summary>Guards against installing the resolver twice.</summary>
    private static int installed;

    /// <summary>Installs the resolver as this assembly loads.</summary>
    [ModuleInitializer]
    internal static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) != 0)
        {
            return;
        }

        // Windows needs no help — there the declared name and the shipped file agree.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(OrtEnv).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver is already set for that assembly — a host installed its own first. Theirs
            // wins by design; never fight it, and never let this throw during module load.
        }
    }

    /// <summary>Maps ONNX Runtime's declared import name onto the file actually shipped.</summary>
    /// <param name="libraryName">The name from the <c>DllImport</c> declaration.</param>
    /// <param name="assembly">The assembly making the call.</param>
    /// <param name="searchPath">The caller's search-path preference, unused here.</param>
    /// <returns>
    /// A handle to the native library, or <see cref="IntPtr.Zero"/> to fall back to default probing.
    /// </returns>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.StartsWith("onnxruntime", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        var fileName = OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";
        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;

        foreach (var candidate in CandidatePaths(fileName, runtimeIdentifier))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        // Nothing found — let the default resolver try, so its (more detailed) error is what the
        // caller sees rather than a silent zero from here.
        return IntPtr.Zero;
    }

    /// <summary>Yields the places the native library is laid out, nearest first.</summary>
    /// <param name="fileName">Platform file name of the native library.</param>
    /// <param name="runtimeIdentifier">The running RID, e.g. <c>osx-arm64</c>.</param>
    /// <returns>Candidate absolute paths, in probe order.</returns>
    private static IEnumerable<string> CandidatePaths(string fileName, string runtimeIdentifier)
    {
        var baseDirectory = AppContext.BaseDirectory;

        // The normal framework-dependent layout.
        yield return Path.Combine(baseDirectory, "runtimes", runtimeIdentifier, "native", fileName);

        // Self-contained / single-file publishes flatten natives beside the executable.
        yield return Path.Combine(baseDirectory, fileName);

        // A macOS .app bundle puts managed assemblies in Contents/MonoBundle and natives one level
        // up in Contents/MacOS, so the flattened copy is not beside the assembly.
        yield return Path.Combine(baseDirectory, "..", "MacOS", fileName);
    }
}
