using TechieDesk.Services.Install;

namespace TechieDesk.Tests.Install;

/// <summary>
/// An <see cref="IProcessLiveness"/> with a fixed answer, so the "the owner crashed" case can be
/// produced deterministically (REQ-FN-051 clause 3).
/// </summary>
public sealed class StubProcessLiveness : IProcessLiveness
{
    private readonly bool isAlive;

    private StubProcessLiveness(bool isAlive) => this.isAlive = isAlive;

    /// <summary>Gets a stub reporting every recorded owner as still running.</summary>
    public static StubProcessLiveness AllAlive { get; } = new(true);

    /// <summary>Gets a stub reporting every recorded owner as gone — the aftermath of a crash.</summary>
    public static StubProcessLiveness NoneAlive { get; } = new(false);

    /// <inheritdoc />
    public bool IsAlive(int processId, DateTimeOffset? startedAtUtc) => isAlive;
}
