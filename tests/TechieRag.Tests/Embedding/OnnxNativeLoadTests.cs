using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using TechieRag.Embedded;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Regression tests for the ONNX Runtime native load on non-Windows hosts (TR-RAG-025).
/// </summary>
/// <remarks>
/// <para>These are deliberately integration-flavoured: they load the REAL native library rather than
/// a stub. That is the whole point. TR-RAG-025 — <c>TechieRag.Embedded</c> cannot load ONNX Runtime in
/// a plain <c>net10.0</c> host on macOS — survived every previous test run because the suite never
/// referenced <c>TechieRag.Embedded</c> at all and every RAG test substitutes a stub embedding
/// provider, so nothing ever forced the native library to load. A unit test with a stub cannot catch
/// a native-loading defect; only touching the real thing can.</para>
/// <para>No model files are needed. Creating the ONNX environment is enough to run
/// <c>NativeMethods</c>' type initializer, which is precisely where the failure was.</para>
/// <para><b>The static constructor is load-bearing, not ceremony.</b> The fix is a
/// <c>[ModuleInitializer]</c> in <c>TechieRag.Embedded</c>, so it takes effect only once that
/// assembly is loaded. Touching a type from it here reproduces what a real caller does — ask the
/// embedded provider for something — and makes these tests independent of xUnit's execution order.
/// Without it they pass or fail depending on which test ran first, which is how the ordering
/// constraint was found in the first place.</para>
/// </remarks>
public class OnnxNativeLoadTests
{
    /// <summary>
    /// Loads <c>TechieRag.Embedded</c> before any ONNX call, exactly as a real caller would.
    /// </summary>
    static OnnxNativeLoadTests() => _ = EmbeddedEmbeddingProvider.GetModelDirectory();

    /// <summary>
    /// Verifies ONNX Runtime's native library loads in this plain <c>net10.0</c> test host.
    /// </summary>
    /// <remarks>
    /// Before the fix this threw <c>TypeInitializationException</c> wrapping
    /// <c>DllNotFoundException: Unable to load shared library 'onnxruntime.dll'</c>, because the
    /// declared import name carries a <c>.dll</c> extension that .NET never substitutes for
    /// <c>.dylib</c>/<c>.so</c> — while the package ships <c>libonnxruntime.dylib</c>.
    /// </remarks>
    [Fact]
    public void OnnxRuntimeNativeLibraryLoads()
    {
        var environment = OrtEnv.Instance();

        Assert.NotNull(environment);
        Assert.NotEmpty(environment.GetAvailableProviders());
    }

    /// <summary>Verifies the CPU execution provider is present, so inference can actually run.</summary>
    /// <remarks>
    /// A load that reported zero usable providers would satisfy the test above while still being
    /// useless for embedding. CPU is the one provider guaranteed on every platform.
    /// </remarks>
    [Fact]
    public void CpuExecutionProviderIsAvailable()
    {
        var providers = OrtEnv.Instance().GetAvailableProviders();

        Assert.Contains(providers, p => p.Contains("CPU", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies the native library ships for the running RID, which is what the resolver needs.
    /// </summary>
    /// <remarks>
    /// Skipped on Windows: there the declared name and the shipped file already agree, so the
    /// resolver is a deliberate no-op and there is no RID-scoped layout to assert.
    /// </remarks>
    [Fact]
    public void NativeLibraryIsLaidOutForTheRunningRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileName = OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";
        var expected = Path.Combine(
            AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);

        Assert.True(
            File.Exists(expected),
            $"ONNX Runtime's native library is missing at {expected}. Without it the embedded "
            + "provider cannot start on this host (TR-RAG-025).");
    }

    /// <summary>Verifies the embedded provider answers a path question without loading a model.</summary>
    [Fact]
    public void ModelDirectoryIsResolvableWithoutLoadingAModel()
    {
        var directory = EmbeddedEmbeddingProvider.GetModelDirectory();

        Assert.False(string.IsNullOrWhiteSpace(directory));
    }
}
