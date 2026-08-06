namespace TechieDesk.Services.Scheduling;

/// <summary>The machine's power source, as far as the app can tell.</summary>
public enum PowerState
{
    /// <summary>The probe could not tell. Never blocks a run.</summary>
    Unknown = 0,

    /// <summary>Running on mains power.</summary>
    Mains = 1,

    /// <summary>Running on battery.</summary>
    Battery = 2
}

/// <summary>
/// The run conditions BRD-139 attaches to background scheduling.
/// </summary>
/// <param name="RequireMainsPower">Only run while on mains power.</param>
/// <param name="RestrictToNamedNetworks">Only run while joined to one of <paramref name="AllowedNetworks"/>.</param>
/// <param name="AllowedNetworks">Network names the user permitted. Empty means the restriction cannot apply.</param>
public sealed record RunConditions(
    bool RequireMainsPower = false,
    bool RestrictToNamedNetworks = false,
    IReadOnlyList<string>? AllowedNetworks = null)
{
    /// <summary>Gets conditions that never block a run.</summary>
    public static RunConditions Unrestricted { get; } = new();
}

/// <summary>The outcome of testing run conditions.</summary>
/// <param name="IsAllowed">Whether the run may proceed.</param>
/// <param name="Reason">Why it may not, as codes and arguments. Null when allowed.</param>
public sealed record RunConditionVerdict(bool IsAllowed, JobMessage? Reason = null)
{
    /// <summary>Gets a verdict permitting the run.</summary>
    public static RunConditionVerdict Allowed { get; } = new(true);
}

/// <summary>
/// Reads the machine state the run conditions are tested against.
/// </summary>
/// <remarks>
/// Split from the evaluation so the decision is testable without a battery. The platform
/// implementation shells out to OS tools; both probes are allowed to answer "I do not know", and
/// unknown never blocks.
/// </remarks>
public interface IRunEnvironmentProbe
{
    /// <summary>Reads the current power source.</summary>
    /// <returns>The power state, or <see cref="PowerState.Unknown"/> when it cannot be determined.</returns>
    PowerState GetPowerState();

    /// <summary>Reads the name of the network currently joined.</summary>
    /// <returns>The network name, or <see langword="null"/> when it cannot be determined.</returns>
    string? GetCurrentNetworkName();
}

/// <summary>
/// Decides whether a scheduled run may proceed under the configured run conditions (BRD-139).
/// </summary>
/// <remarks>
/// <para><b>An unreadable probe never blocks a run.</b> The failure mode of "cannot tell, so do not
/// run" is an automation that silently stops working on a machine where a shell-out is unavailable —
/// and stops without ever saying so. Blocking requires positive evidence that a condition is
/// violated.</para>
/// <para><b>REQ-UI-055, corrected by REQ-UI-056.</b> The reason is written into the run history and
/// read there by a person asking "why did this not run last night", so it is composed from resource
/// keys. REQ-UI-055 resolved those keys HERE and stored the sentence, which made the row a historical
/// record "written in the language the app was running in at the time" — meaning a user who later
/// switched language read their own history in a language they had stopped using. It now returns the
/// codes and arguments and the run-details dialog resolves them, so the row has no language of its
/// own. That is also why this class no longer takes a <see cref="LocalizeText"/> at all: there is
/// nothing left here that could render.</para>
/// </remarks>
public sealed class RunConditionEvaluator
{
    private readonly IRunEnvironmentProbe probe;

    /// <summary>Initializes the evaluator.</summary>
    /// <param name="probe">Reads the machine state.</param>
    public RunConditionEvaluator(IRunEnvironmentProbe probe)
    {
        this.probe = probe;
    }

    /// <summary>Tests the conditions.</summary>
    /// <param name="conditions">The configured conditions.</param>
    /// <returns>Whether a run may proceed, and why not when it may not.</returns>
    public RunConditionVerdict Evaluate(RunConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        if (conditions.RequireMainsPower && probe.GetPowerState() == PowerState.Battery)
        {
            return new RunConditionVerdict(false, JobMessage.Of("SchedulerSkipOnBattery"));
        }

        if (!conditions.RestrictToNamedNetworks || conditions.AllowedNetworks is not { Count: > 0 } allowed)
        {
            return RunConditionVerdict.Allowed;
        }

        var current = probe.GetCurrentNetworkName();
        if (string.IsNullOrWhiteSpace(current))
        {
            return RunConditionVerdict.Allowed;
        }

        foreach (var network in allowed)
        {
            if (string.Equals(network, current, StringComparison.OrdinalIgnoreCase))
            {
                return RunConditionVerdict.Allowed;
            }
        }

        // The network's own name goes through verbatim: it is what the user called their WiFi, not
        // something this app gets to translate.
        return new RunConditionVerdict(false, JobMessage.Of("SchedulerSkipNotOnAllowedNetwork", current));
    }
}
