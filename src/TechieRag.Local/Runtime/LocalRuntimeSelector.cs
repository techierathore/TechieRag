using System.Runtime.InteropServices;

namespace TechieRag.Local.Runtime;

/// <summary>
/// Picks the runtime for the platform the process runs on (REQ-RAG-058 / BRD-97).
/// </summary>
/// <remarks>
/// <para><b>One engine everywhere.</b> <c>DECISIONS.md</c> 2026-09-25 (decisions 1 and 2, after the
/// measured comparison with LLamaSharp) makes ONNX Runtime GenAI the engine on Windows, Android, iOS
/// and Mac Catalyst; the same package also runs it on macOS and Linux. It is returned wherever the
/// package ships a native build: Windows and Linux on x64 and Arm64, macOS on Apple silicon, Android,
/// iOS and Mac Catalyst. Anywhere else (an Intel Mac, a browser) there is none, and a load throws
/// <see cref="PlatformNotSupportedException"/> with <see cref="NoRuntimeMessage"/>.</para>
/// </remarks>
internal static class LocalRuntimeSelector
{
    /// <summary>The message a load gives on a platform with no runtime.</summary>
    internal const string NoRuntimeMessage =
        "No local inference runtime is available for this platform. TechieRag.Local runs its models with ONNX Runtime "
        + "GenAI on Windows and Linux (x64, Arm64), macOS on Apple silicon, Android, iOS and Mac Catalyst.";

    /// <summary>Gets the runtime for the current platform, or null when there is none.</summary>
    /// <returns>The runtime, or null.</returns>
    internal static ILocalLlmRuntime? ForCurrentPlatform() =>
        IsSupported(RuntimeInformation.ProcessArchitecture) ? OnnxGenAiRuntime.Instance : null;

    /// <summary>Gets whether ONNX Runtime GenAI ships a native build for this operating system and architecture.</summary>
    /// <param name="architecture">The process architecture.</param>
    /// <returns>True when the engine can run.</returns>
    internal static bool IsSupported(Architecture architecture)
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
        {
            return true;
        }

        var desktopArchitecture = architecture is Architecture.X64 or Architecture.Arm64;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            return desktopArchitecture;
        }

        return OperatingSystem.IsMacOS() && architecture == Architecture.Arm64;
    }
}
