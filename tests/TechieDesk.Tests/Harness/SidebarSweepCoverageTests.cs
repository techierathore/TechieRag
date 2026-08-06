using Xunit;
using Xunit.Abstractions;

namespace TechieDesk.Tests.Harness;

/// <summary>
/// REQ-NFR-014 guard 1: the sweep harness's destination table covers EVERY sidebar destination the
/// app declares, so a screen cannot ship un-graded.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this closes.</b> <c>run_sweep.SIDEBAR</c> is a hardcoded table of the screens the
/// verify sweep visits. <c>/settings/backup</c> (REQ-FN-046/047) was added to <c>MainLayout.razor</c>
/// and NOT to that table, so for days it was never navigated to, never render-gated, never
/// visual-gated — and every sweep still printed a clean run, because a harness cannot report a
/// screen it does not know exists. <b>A silently incomplete harness is worse than a missing one: it
/// manufactures false confidence.</b> The only observer that can see the omission is one that
/// compares the table against the product, which is what this class is.
/// </para>
/// <para>
/// <b>What "covers" means here.</b> Set equality in BOTH directions, on three axes, keyed on the
/// resource key each screen is known by:
/// </para>
/// <list type="number">
/// <item>every <c>&lt;SidebarMenuButton&gt;</c> has a <c>SIDEBAR</c> row — adding a screen without
/// updating the table fails;</item>
/// <item>every <c>SIDEBAR</c> row has a <c>&lt;SidebarMenuButton&gt;</c> — a phantom row that
/// pretends a retired screen is still being graded fails too;</item>
/// <item>the row's ROUTE and the button's <c>Href</c> agree, and the button's REQ-UI-053 <c>id</c>
/// and <c>nav.NAV_IDS</c> agree, so a rename on one side of either contract fails rather than
/// degrading to the label fallback in silence.</item>
/// </list>
/// <para>
/// Keying on the resource key rather than the route is deliberate: it is the key that
/// <c>nav.sidebar_key()</c> clicks with and that <c>run_sweep.arrival_ok()</c> proves arrival with
/// (<c>MainLayout.CurrentTrail</c> renders the same key for the breadcrumb's page rung), so it is
/// the identity the harness actually navigates by.
/// </para>
/// <para>
/// <b>Not covered, deliberately.</b> This proves the table is complete, not that the screens pass —
/// grading is the sweep's job. And it cannot see a screen with no sidebar entry at all
/// (<c>/register</c>, <c>/setup</c>); those are <c>run_sweep.CHROMELESS</c>'s population and are
/// reached another way.
/// </para>
/// </remarks>
/// <param name="output">The xunit output sink.</param>
public sealed class SidebarSweepCoverageTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every sidebar destination the app declares is a row of the sweep's table, and every row is a
    /// destination the app declares.
    /// </summary>
    /// <remarks>
    /// This is the assertion <c>/settings/backup</c> needed. Adding a
    /// <c>&lt;SidebarMenuButton&gt;</c> without adding its <c>SIDEBAR</c> row fails HERE, on the
    /// change that adds it, instead of being discovered as an ungraded screen some phases later.
    /// </remarks>
    [Fact]
    public void SweepSidebarTableCoversEverySidebarMenuButton()
    {
        var declared = HarnessSource.DeclaredSidebarButtons();
        var swept = HarnessSource.SweepSidebarTable();

        var declaredKeys = declared.Select(button => button.ResourceKey).ToHashSet(StringComparer.Ordinal);
        var sweptKeys = swept.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

        var ungraded = declared
            .Where(button => !sweptKeys.Contains(button.ResourceKey))
            .Select(button => $"{button.RouteTemplate} (key {button.ResourceKey}, id {button.AutomationId})")
            .ToArray();

        Assert.True(
            ungraded.Length == 0,
            $"{ungraded.Length} sidebar screen(s) are in MainLayout.razor but NOT in run_sweep.SIDEBAR, " +
            "so no verify sweep has ever graded them and every sweep still reported clean — the " +
            "/settings/backup failure, exactly. Add a (slug, route, resource-key) row for each: " +
            string.Join("; ", ungraded));

        var phantom = swept
            .Where(row => !declaredKeys.Contains(row.Key))
            .Select(row => $"{row.Slug} -> {row.Route} (key {row.Key})")
            .ToArray();

        Assert.True(
            phantom.Length == 0,
            $"{phantom.Length} run_sweep.SIDEBAR row(s) name a resource key no SidebarMenuButton uses. " +
            "The screen was retired or its label key renamed; the sweep will report `nav key NOT " +
            "FOUND` for it forever. Remove or repoint: " + string.Join("; ", phantom));

        Assert.Equal(swept.Count, sweptKeys.Count);
        output.WriteLine($"{declared.Count} sidebar destinations, all present in run_sweep.SIDEBAR.");
    }

    /// <summary>
    /// Each sweep row points at the route its <c>&lt;SidebarMenuButton&gt;</c> actually links to.
    /// </summary>
    /// <remarks>
    /// Presence alone is not coverage. <c>run_sweep.arrival_ok()</c> falls back to matching the
    /// route's last segment against the breadcrumb when the key path does not resolve, so a stale
    /// route in the table degrades that fallback silently. The per-install workspace slug is
    /// normalised away (<see cref="HarnessSource.NormaliseRoute"/>) because it is the one segment
    /// that legitimately differs between the markup and the harness.
    /// </remarks>
    [Fact]
    public void SweepSidebarRoutesMatchTheDeclaredHref()
    {
        var declared = HarnessSource.DeclaredSidebarButtons()
            .ToDictionary(button => button.ResourceKey, StringComparer.Ordinal);

        var drifted = HarnessSource.SweepSidebarTable()
            .Where(row => declared.TryGetValue(row.Key, out var button) && button.RouteTemplate != row.Route)
            .Select(row => $"{row.Key}: SIDEBAR says '{row.Route}', MainLayout links '{declared[row.Key].RouteTemplate}'")
            .ToArray();

        Assert.True(
            drifted.Length == 0,
            $"{drifted.Length} sweep row(s) name a route the sidebar no longer links to: " +
            string.Join("; ", drifted));
    }

    /// <summary>
    /// <c>nav.NAV_IDS</c> holds the identifier every sidebar button really carries, for every button.
    /// </summary>
    /// <remarks>
    /// The other half of the REQ-UI-053 contract that <c>MainLayout.razor</c>'s own comment calls a
    /// PUBLIC CONTRACT with <c>tests/appium/nav.py</c>. Its README already names the failure this
    /// asserts away: "One screen says <c>label=…</c> while the others say <c>id=…</c>" — a route
    /// renamed on one side only, which degrades <c>sidebar_key()</c> to the locale-dependent label
    /// fallback and, as the README puts it, "degrades silently".
    /// </remarks>
    [Fact]
    public void NavIdTableMatchesEverySidebarMenuButtonIdentifier()
    {
        var declared = HarnessSource.DeclaredSidebarButtons();
        var mapped = HarnessSource.NavIdTable().ToDictionary(row => row.Key, row => row.AutomationId, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var button in declared)
        {
            if (!mapped.TryGetValue(button.ResourceKey, out var identifier))
            {
                problems.Add($"{button.ResourceKey} ({button.RouteTemplate}) has no nav.NAV_IDS entry");
            }
            else if (identifier != button.AutomationId)
            {
                problems.Add($"{button.ResourceKey}: NAV_IDS says '{identifier}', MainLayout renders id='{button.AutomationId}'");
            }
        }

        var declaredKeys = declared.Select(button => button.ResourceKey).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(mapped.Keys
            .Where(key => !declaredKeys.Contains(key))
            .Select(key => $"NAV_IDS maps '{key}' -> '{mapped[key]}' but no SidebarMenuButton uses that key"));

        Assert.True(
            problems.Count == 0,
            $"{problems.Count} break(s) in the REQ-UI-053 identifier contract between " +
            "MainLayout.razor and tests/appium/nav.py. Both halves change together or the sweep " +
            "silently falls back to clicking localized labels: " + string.Join("; ", problems));
    }

    /// <summary>
    /// The scan is really reading the sidebar and the harness, and both parsed to plausible sizes.
    /// </summary>
    /// <remarks>
    /// Without this the three tests above pass perfectly over two empty lists. That is the same
    /// defect as the incomplete <c>SIDEBAR</c> table — a check reporting clean over a population it
    /// never actually looked at — so it is asserted rather than assumed, exactly as
    /// <c>MenuBarLocalizationTests.TheScanReadsTheRealMenuBarSource</c> does.
    /// </remarks>
    [Fact]
    public void TheScanReadsTheRealSidebarAndHarnessSource()
    {
        var declared = HarnessSource.DeclaredSidebarButtons();
        var swept = HarnessSource.SweepSidebarTable();
        var mapped = HarnessSource.NavIdTable();

        Assert.True(declared.Count >= 20, $"only {declared.Count} SidebarMenuButton(s) parsed out of MainLayout.razor");
        Assert.True(swept.Count >= 20, $"only {swept.Count} row(s) parsed out of run_sweep.SIDEBAR");
        Assert.True(mapped.Count >= 20, $"only {mapped.Count} entr(y/ies) parsed out of nav.NAV_IDS");

        Assert.Equal(declared.Count, declared.Select(button => button.AutomationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(declared.Count, declared.Select(button => button.ResourceKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(swept.Count, swept.Select(row => row.Slug).Distinct(StringComparer.Ordinal).Count());

        output.WriteLine($"MainLayout.razor: {declared.Count} buttons | SIDEBAR: {swept.Count} rows | NAV_IDS: {mapped.Count}");
        foreach (var button in declared)
        {
            output.WriteLine($"  {button.Ordinal,2}. {button.RouteTemplate,-32} {button.AutomationId,-26} {button.ResourceKey}");
        }
    }
}
