using System.Runtime.CompilerServices;
using TechieRag.Models;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Hands the typed <see cref="ToolResult"/> from <see cref="ToolHandlerAIFunction"/> up to
/// <see cref="AgentStepReporter"/>'s function middleware (REQ-RAG-017 / BRD-85).
/// </summary>
/// <remarks>
/// The model must receive the result's <c>Content</c> string, so that is what the function returns.
/// The trace needs more (<c>IsSuccess</c>, <c>ErrorMessage</c>, the <c>FlowMessage</c> code), and the
/// middleware only sees the returned object. The middleware opens a slot around its <c>next</c> call;
/// the function, running inside that call, fills it. An async-local box flows downward, which is the
/// only direction needed.
/// </remarks>
internal static class ToolResultCapture
{
    private static readonly AsyncLocal<StrongBox<ToolResult?>?> Slot = new();

    /// <summary>Opens a slot for the call about to run.</summary>
    /// <returns>The box the function will fill.</returns>
    public static StrongBox<ToolResult?> Open()
    {
        var box = new StrongBox<ToolResult?>();
        Slot.Value = box;
        return box;
    }

    /// <summary>Records a result into the open slot, if there is one.</summary>
    /// <param name="result">The tool result.</param>
    public static void Record(ToolResult result)
    {
        if (Slot.Value is { } box) box.Value = result;
    }
}
