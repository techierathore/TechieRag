using TechieDesk.Services.Scheduling;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// BRD-139 run conditions. The rule under test throughout is that only positive evidence blocks a
/// run — a probe that cannot answer must never quietly disable every automation on the machine.
/// </summary>
public sealed class RunConditionTests
{
    /// <summary>With nothing restricted, a run always proceeds.</summary>
    [Fact]
    public void UnrestrictedConditionsAlwaysAllow()
    {
        var verdict = Evaluate(RunConditions.Unrestricted, PowerState.Battery, "Coffee shop");

        Assert.True(verdict.IsAllowed);
    }

    /// <summary>Mains-power-only blocks a run on battery, and says which condition blocked it.</summary>
    [Fact]
    public void MainsPowerOnlyBlocksOnBattery()
    {
        var verdict = Evaluate(new RunConditions(RequireMainsPower: true), PowerState.Battery, null);

        Assert.False(verdict.IsAllowed);
        Assert.Contains("battery", verdict.Reason!.ToInvariantString(), StringComparison.Ordinal);
    }

    /// <summary>Mains-power-only allows a run on mains.</summary>
    [Fact]
    public void MainsPowerOnlyAllowsOnMains()
    {
        Assert.True(Evaluate(new RunConditions(RequireMainsPower: true), PowerState.Mains, null).IsAllowed);
    }

    /// <summary>An unreadable power probe allows the run rather than silently stopping every schedule.</summary>
    [Fact]
    public void AnUnreadablePowerProbeAllowsTheRun()
    {
        Assert.True(Evaluate(new RunConditions(RequireMainsPower: true), PowerState.Unknown, null).IsAllowed);
    }

    /// <summary>A named-network restriction allows a run on a listed network.</summary>
    [Fact]
    public void NamedNetworksAllowAListedNetwork()
    {
        var conditions = new RunConditions(RestrictToNamedNetworks: true, AllowedNetworks: ["Home", "Office"]);

        Assert.True(Evaluate(conditions, PowerState.Mains, "office").IsAllowed);
    }

    /// <summary>A named-network restriction blocks an unlisted network and names it.</summary>
    [Fact]
    public void NamedNetworksBlockAnUnlistedNetwork()
    {
        var conditions = new RunConditions(RestrictToNamedNetworks: true, AllowedNetworks: ["Home"]);

        var verdict = Evaluate(conditions, PowerState.Mains, "Airport WiFi");

        Assert.False(verdict.IsAllowed);
        Assert.Contains("Airport WiFi", verdict.Reason!.ToInvariantString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An unreadable network name allows the run — recent macOS releases withhold the SSID without
    /// Location Services, and that must not disable scheduling.
    /// </summary>
    [Fact]
    public void AnUnreadableNetworkNameAllowsTheRun()
    {
        var conditions = new RunConditions(RestrictToNamedNetworks: true, AllowedNetworks: ["Home"]);

        Assert.True(Evaluate(conditions, PowerState.Mains, null).IsAllowed);
    }

    /// <summary>A network restriction with an empty list cannot apply, so it does not block.</summary>
    [Fact]
    public void AnEmptyNetworkListCannotBlock()
    {
        var conditions = new RunConditions(RestrictToNamedNetworks: true, AllowedNetworks: []);

        Assert.True(Evaluate(conditions, PowerState.Mains, "Anywhere").IsAllowed);
    }

    /// <summary>The saved preferences project onto the conditions the scheduler tests.</summary>
    [Fact]
    public void PreferencesProjectOntoRunConditions()
    {
        var preferences = new SchedulerPreferences(
            BackgroundServiceEnabled: true,
            MainsPowerOnly: true,
            NamedNetworksOnly: true,
            AllowedNetworks: ["Home"]);

        var conditions = preferences.ToRunConditions();

        Assert.True(conditions.RequireMainsPower);
        Assert.True(conditions.RestrictToNamedNetworks);
        Assert.Equal(["Home"], conditions.AllowedNetworks);
    }

    private static RunConditionVerdict Evaluate(RunConditions conditions, PowerState power, string? network) =>
        new RunConditionEvaluator(new FakeRunEnvironmentProbe(power, network)).Evaluate(conditions);
}
