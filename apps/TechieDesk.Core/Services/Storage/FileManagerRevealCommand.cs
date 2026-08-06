namespace TechieDesk.Services.Storage;

/// <summary>
/// The executable and arguments that reveal a path in a host's file manager (REQ-UI-041).
/// </summary>
/// <param name="FileName">The executable to run, e.g. <c>open</c> or <c>explorer.exe</c>.</param>
/// <param name="Arguments">Arguments passed individually, never joined into a shell string.</param>
public sealed record FileManagerRevealCommand(string FileName, IReadOnlyList<string> Arguments);
