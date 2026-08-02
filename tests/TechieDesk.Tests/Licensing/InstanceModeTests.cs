using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Licensing;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Licensing;

/// <summary>
/// REQ-FN-044/BRD-142 (instance mode from the AppManager licence tier) and REQ-FN-045/BRD-143
/// (seat-based team licensing).
/// <para>
/// The clause these tests exist for is BRD-129, which is absolute: an unassigned, expired,
/// revoked or unreachable seat degrades the install to <b>full Individual capability</b> — never
/// to a locked, read-only or nagging state. <see cref="NoStateCanMakeLocalDataUnreachable"/>
/// asserts that exhaustively over every licence state the product can be in, and
/// <see cref="LapsedSeatLeavesTheInstallFullyUsable"/> proves it end-to-end through the real
/// <see cref="LicenseService"/> with a seat actually revoked mid-run.
/// </para>
/// </summary>
public sealed class InstanceModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);

    private static TechieDeskUser User() => new(123, "jane@example.com", "Jane Doe", ProductRole.User, true);

    private static LicensingOptions DefaultOptions() => new();

    private static LicenseStatus Status(
        LicenseAvailability availability, string? tier, string? state = "Active")
        => new()
        {
            Availability = availability,
            LicenseName = tier,
            Status = state,
            ValidatedAt = Now.UtcDateTime
        };

    // ---------------------------------------------------------------------------------------
    // REQ-FN-044 acceptance (1): the tier resolves to exactly one of Individual/Team/Enterprise.
    // ---------------------------------------------------------------------------------------

    /// <summary>Offline single-user mode — no licence server — is the Individual default (BRD-129).</summary>
    [Fact]
    public void OfflineResolvesIndividual()
    {
        var mode = InstanceModeResolver.Resolve(LicenseStatus.Offline, DefaultOptions());

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.Equal(SeatState.None, mode.Seat);
        Assert.False(mode.IsTeamOrEnterprise);
        Assert.True(mode.LocalDataAccessible);
    }

    /// <summary>A personal paid tier is still Individual — no organisation seat is involved.</summary>
    [Theory]
    [InlineData("Free")]
    [InlineData("Professional")]
    [InlineData("SomeTierWeHaveNeverHeardOf")]
    [InlineData(null)]
    public void PersonalTiersResolveIndividual(string? tier)
    {
        var mode = InstanceModeResolver.Resolve(Status(LicenseAvailability.Live, tier), DefaultOptions());

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.Equal(SeatState.None, mode.Seat);
        Assert.False(mode.IsSeatDegraded);
    }

    /// <summary>An active Team-tier seat resolves to Team with the seat marked assigned.</summary>
    [Theory]
    [InlineData("Team")]
    [InlineData("Business")]
    [InlineData("team")]
    public void ActiveTeamSeatResolvesTeam(string tier)
    {
        var mode = InstanceModeResolver.Resolve(Status(LicenseAvailability.Live, tier), DefaultOptions());

        Assert.Equal(InstanceMode.Team, mode.Mode);
        Assert.Equal(SeatState.Assigned, mode.Seat);
        Assert.True(mode.TeamFeaturesVisible);
        Assert.False(mode.IsSeatDegraded);
    }

    /// <summary>An active Enterprise-tier seat resolves to Enterprise.</summary>
    [Fact]
    public void ActiveEnterpriseSeatResolvesEnterprise()
    {
        var mode = InstanceModeResolver.Resolve(
            Status(LicenseAvailability.Live, "Enterprise"), DefaultOptions());

        Assert.Equal(InstanceMode.Enterprise, mode.Mode);
        Assert.Equal(SeatState.Assigned, mode.Seat);
        Assert.True(mode.TeamFeaturesVisible);
    }

    /// <summary>Tier names are configurable, so renaming a plan in AppManager needs no code change.</summary>
    [Fact]
    public void TierNamesAreConfigurable()
    {
        var options = new LicensingOptions
        {
            TeamLicenseTiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Squad" }
        };

        var mode = InstanceModeResolver.Resolve(Status(LicenseAvailability.Live, "Squad"), options);

        Assert.Equal(InstanceMode.Team, mode.Mode);
    }

    // ---------------------------------------------------------------------------------------
    // REQ-FN-045 acceptance (2): seat state survives an AppManager outage on BRD-51 grace terms.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// BRD-51: while the cached licence is inside the grace window an outage does not cost the
    /// team member their entitlements — the seat stays assigned, flagged as cached.
    /// </summary>
    [Fact]
    public void CachedTeamSeatKeepsEntitlementsWithinGrace()
    {
        var mode = InstanceModeResolver.Resolve(
            Status(LicenseAvailability.Cached, "Enterprise"), DefaultOptions());

        Assert.Equal(InstanceMode.Enterprise, mode.Mode);
        Assert.Equal(SeatState.Assigned, mode.Seat);
        Assert.True(mode.IsFromCache);
        Assert.True(mode.TeamFeaturesVisible);
    }

    /// <summary>
    /// Past the grace window the seat can no longer be asserted, so the mode falls back to the
    /// Individual floor — <b>not</b> to a locked state. Paid features lock (FeatureGate's job);
    /// the user's own documents do not.
    /// </summary>
    [Fact]
    public void GraceExpiredDegradesToIndividualNotToLocked()
    {
        var mode = InstanceModeResolver.Resolve(
            Status(LicenseAvailability.GraceExpired, "Team"), DefaultOptions());

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.Equal(SeatState.Unverified, mode.Seat);
        Assert.True(mode.LocalDataAccessible);

        // REQ-UI-055: the reassurance is now a resource key, so this reads it back through the
        // REAL localizer rather than off a literal the resolver built.
        using var resources = new ResourceHarness("en");
        Assert.Contains("unaffected", mode.Describe(resources.Localize), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------
    // REQ-FN-045 acceptance (3): a revoked seat degrades at the next successful check.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every way a team seat can stop entitling — revoked, cancelled, suspended, expired,
    /// unassigned or an AppManager status string we have never seen — lands on Individual with
    /// local data intact.
    /// </summary>
    [Theory]
    [InlineData("Revoked", SeatState.Revoked)]
    [InlineData("Cancelled", SeatState.Revoked)]
    [InlineData("Canceled", SeatState.Revoked)]
    [InlineData("Suspended", SeatState.Revoked)]
    [InlineData("Expired", SeatState.Expired)]
    [InlineData("Pending", SeatState.Unassigned)]
    [InlineData("", SeatState.Unassigned)]
    [InlineData(null, SeatState.Unassigned)]
    [InlineData("SomethingAppManagerInventedLater", SeatState.Unassigned)]
    public void LapsedTeamSeatDegradesToIndividual(string? appManagerStatus, SeatState expectedSeat)
    {
        var mode = InstanceModeResolver.Resolve(
            Status(LicenseAvailability.Live, "Team", appManagerStatus), DefaultOptions());

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.Equal(expectedSeat, mode.Seat);
        Assert.True(mode.IsSeatDegraded);
        Assert.False(mode.TeamFeaturesVisible);
        Assert.True(mode.LocalDataAccessible);
    }

    /// <summary>AppManager answering "no valid licence" is Individual, not locked.</summary>
    [Fact]
    public void InvalidLicenceDegradesToIndividual()
    {
        var mode = InstanceModeResolver.Resolve(
            Status(LicenseAvailability.Invalid, "Team", "Invalid"), DefaultOptions());

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.True(mode.LocalDataAccessible);
    }

    /// <summary>Before the first validation the install opens usable, at the Individual floor.</summary>
    [Fact]
    public void UnknownOpensAtTheIndividualFloor()
    {
        var mode = InstanceModeResolver.Resolve(LicenseStatus.Unknown, DefaultOptions());

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.True(mode.LocalDataAccessible);
    }

    // ---------------------------------------------------------------------------------------
    // BRD-129, the clause everything else is written around.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The BRD-129 guarantee, asserted exhaustively.</b> Every combination of licence
    /// availability × tier × AppManager status the product can reach is resolved, and in every
    /// single one: the mode is at least Individual, the local data stays reachable, and no
    /// resolution throws. If a future branch ever tried to express "locked" or "read-only", this
    /// test is what fails.
    /// </summary>
    [Fact]
    public void NoStateCanMakeLocalDataUnreachable()
    {
        string?[] tiers = [null, "", "Free", "Professional", "Team", "Business", "Enterprise", "Weird"];
        string?[] states = [null, "", "Active", "Expired", "Revoked", "Cancelled", "Suspended", "Pending", "Nonsense"];

        var combinations = 0;

        // REQ-UI-055: every combination must also produce a key that RESOLVES. A state whose
        // message key were mistyped would render the key name in the shell banner, which is the
        // failure this whole requirement exists to make impossible.
        using var resources = new ResourceHarness("en");

        foreach (var availability in Enum.GetValues<LicenseAvailability>())
        {
            foreach (var tier in tiers)
            {
                foreach (var state in states)
                {
                    var mode = InstanceModeResolver.Resolve(
                        Status(availability, tier, state), DefaultOptions());

                    combinations++;

                    Assert.True(mode.LocalDataAccessible,
                        $"{availability}/{tier}/{state} made local data unreachable — BRD-129 violated.");
                    Assert.True(mode.Mode >= InstanceMode.Individual,
                        $"{availability}/{tier}/{state} resolved below the Individual floor.");
                    var described = mode.Describe(resources.Localize);
                    Assert.False(string.IsNullOrWhiteSpace(described));
                    Assert.NotEqual(mode.MessageKey, described);

                    // Team/Enterprise entitlements are ONLY ever granted by an assigned seat.
                    if (mode.IsTeamOrEnterprise)
                    {
                        Assert.Equal(SeatState.Assigned, mode.Seat);
                        Assert.Equal("Active", state);
                    }
                }
            }
        }

        Assert.Equal(Enum.GetValues<LicenseAvailability>().Length * tiers.Length * states.Length, combinations);
    }

    /// <summary>
    /// The mode vocabulary itself cannot express a locked install. Individual must be the zero
    /// value (so <c>default</c> is the usable state), and neither enum may gain a member that
    /// reads as a denial of access. This guards the design against a well-meaning future addition
    /// as much as against a bug.
    /// </summary>
    [Fact]
    public void ModeVocabularyCannotExpressALockedInstall()
    {
        Assert.Equal(InstanceMode.Individual, default(InstanceMode));
        Assert.Equal(SeatState.None, default(SeatState));
        Assert.Equal(InstanceMode.Individual, Enum.GetValues<InstanceMode>().Min());
        Assert.Equal(InstanceMode.Individual, new InstanceModeStatus().Mode);
        Assert.True(new InstanceModeStatus().LocalDataAccessible);

        string[] banned = ["lock", "readonly", "disabled", "denied", "blocked", "suspend"];
        var names = Enum.GetNames<InstanceMode>().Concat(Enum.GetNames<SeatState>());

        foreach (var name in names)
        {
            foreach (var word in banned)
            {
                Assert.False(name.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{name} reads as a denial of access — BRD-129 forbids one.");
            }
        }
    }

    /// <summary>
    /// End-to-end over the <b>real</b> <see cref="LicenseService"/> and the real cache: a Team
    /// seat is validated live, then the organisation revokes it. At the next successful check the
    /// install degrades to Individual — and the licence status it degrades to is still one whose
    /// features are evaluated normally, with no locked state anywhere. This is the REQ-FN-045
    /// acceptance-(3) proof.
    /// </summary>
    [Fact]
    public async Task LapsedSeatLeavesTheInstallFullyUsable()
    {
        var time = new FixedTimeProvider(Now);
        var client = new FakeAppManagerClient();
        var (service, mode) = BuildStack(client, time);

        client.OnValidateLicense = (_, _) => Task.FromResult(TeamLicense("Active"));
        var assigned = await mode.RefreshAsync();

        Assert.Equal(InstanceMode.Team, assigned.Mode);
        Assert.Equal(SeatState.Assigned, assigned.Seat);

        // The organisation revokes the seat. Next successful check:
        time.Advance(TimeSpan.FromHours(2));
        client.OnValidateLicense = (_, _) => Task.FromResult(TeamLicense("Revoked"));
        var revoked = await mode.RefreshAsync();

        Assert.Equal(InstanceMode.Individual, revoked.Mode);
        Assert.Equal(SeatState.Revoked, revoked.Seat);
        Assert.False(revoked.TeamFeaturesVisible);

        // …and nothing about that is a lock. Local data stays reachable, the licence status is a
        // normal one (not GraceExpired/Invalid), and the message reassures rather than nags.
        Assert.True(revoked.LocalDataAccessible);
        Assert.Equal(LicenseAvailability.Live, service.Current.Availability);
        using (var resources = new ResourceHarness("en"))
        {
            Assert.Contains(
                "full access to your own documents",
                revoked.Describe(resources.Localize),
                StringComparison.OrdinalIgnoreCase);
        }

        // The install also survives AppManager vanishing entirely afterwards.
        time.Advance(TimeSpan.FromDays(30));
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");
        var offline = await mode.RefreshAsync();

        Assert.Equal(InstanceMode.Individual, offline.Mode);
        Assert.True(offline.LocalDataAccessible);
    }

    /// <summary>
    /// An AppManager outage inside the grace window keeps the Team seat, driven through the real
    /// licence service and its persisted cache rather than a hand-built status (BRD-51).
    /// </summary>
    [Fact]
    public async Task OutageWithinGraceKeepsTheTeamSeatEndToEnd()
    {
        var time = new FixedTimeProvider(Now);
        var client = new FakeAppManagerClient();
        var (_, mode) = BuildStack(client, time);

        client.OnValidateLicense = (_, _) => Task.FromResult(TeamLicense("Active"));
        await mode.RefreshAsync();

        time.Advance(TimeSpan.FromHours(24));            // inside the default 72h window
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");
        var cached = await mode.RefreshAsync();

        Assert.Equal(InstanceMode.Team, cached.Mode);
        Assert.Equal(SeatState.Assigned, cached.Seat);
        Assert.True(cached.IsFromCache);

        // Past the window it degrades to Individual — still fully usable.
        time.Advance(TimeSpan.FromHours(60));
        var lapsed = await mode.RefreshAsync();

        Assert.Equal(InstanceMode.Individual, lapsed.Mode);
        Assert.Equal(SeatState.Unverified, lapsed.Seat);
        Assert.True(lapsed.LocalDataAccessible);
    }

    /// <summary>
    /// The service fails open. Even if the licence service throws outright, the answer is the
    /// fully usable Individual floor — never an exception bubbling into a page, and never a lock.
    /// </summary>
    [Fact]
    public async Task ServiceFailsOpenWhenLicenceLookupThrows()
    {
        var service = new InstanceModeService(
            new ThrowingLicenseService(),
            Options.Create(DefaultOptions()),
            NullLogger<InstanceModeService>.Instance);

        var mode = await service.EnsureFreshAsync();

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.True(mode.LocalDataAccessible);
        Assert.Same(InstanceModeStatus.Individual, service.Current);
    }

    /// <summary>The offline install reports Individual through the whole service, not just the resolver.</summary>
    [Fact]
    public async Task OfflineInstallReportsIndividualThroughTheService()
    {
        var service = new InstanceModeService(
            new FakeLicenseService(LicenseStatus.Offline),
            Options.Create(DefaultOptions()),
            NullLogger<InstanceModeService>.Instance);

        var mode = await service.EnsureFreshAsync();

        Assert.Equal(InstanceMode.Individual, mode.Mode);
        Assert.Equal(SeatState.None, mode.Seat);
        using var resources = new ResourceHarness("en");
        Assert.Contains("offline single-user", mode.Describe(resources.Localize), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------
    // ADR-012: mode is entitlements only. Nothing multi-user, no roles, no data partitioning.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The architectural guard behind BRD-129: <b>no data-access path may consult the instance
    /// mode at all</b>. The exhaustive test above proves no state denies access; this proves no
    /// code is even in a position to ask. It scans the shipped Core sources and fails if a
    /// repository, workspace, storage, thread, ingestion or agent file so much as mentions
    /// <c>InstanceMode</c>.
    /// </summary>
    [Fact]
    public void LocalDataPathsNeverConsultTheInstanceMode()
    {
        var core = Path.Combine(RepositoryRoot(), "apps", "TechieDesk.Core", "Services");
        Assert.True(Directory.Exists(core), $"Core services not found at {core}");

        string[] localDataAreas = ["Data", "Workspaces", "Storage", "Threads", "Agents", "Connectors"];
        var offenders = new List<string>();

        foreach (var area in localDataAreas)
        {
            var folder = Path.Combine(core, area);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(file).Contains("InstanceMode", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(core, file));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Local-data code must never consult the licence tier (BRD-129). Offending files: "
                + string.Join(", ", offenders));
    }

    /// <summary>
    /// ADR-012 / REQ-FN-041: serving teams reinstates nothing multi-tenant. The role mapper, the
    /// capability service and the workspace-assignment repository stay deleted, and this test
    /// fails the moment any of them reappears as a real type in the shipped assembly.
    /// </summary>
    [Fact]
    public void SeatLicensingReinstatesNoRoleOrCapabilityInfrastructure()
    {
        string[] retired =
        [
            "ProductRoleMapper",
            "CapabilityService",
            "ICapabilityService",
            "IWorkspaceAssignmentRepository",
            "WorkspaceAssignmentRepository"
        ];

        var types = typeof(InstanceModeService).Assembly.GetTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in retired)
        {
            Assert.DoesNotContain(name, types);
        }

        // And the mode type itself carries no notion of a role, a capability or a user other
        // than the single person at this machine.
        var members = typeof(InstanceModeStatus).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(members, m => m.Contains("Role", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, m => m.Contains("Capabilit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, m => m.Contains("Permission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, m => m.Contains("Assignment", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static LicenseValidationData TeamLicense(string status) => new()
    {
        IsValid = true,
        License = new ActiveLicenseData
        {
            LicenseId = 42,
            LicenseName = "Team",
            Status = status,
            ExpiryDate = Now.AddDays(300),
            DaysRemaining = 300
        }
    };

    /// <summary>
    /// Builds the real licensing stack — real <see cref="LicenseService"/>, real grace arithmetic,
    /// real cache round-trip — with only the AppManager wire faked.
    /// </summary>
    private static (LicenseService license, InstanceModeService mode) BuildStack(
        FakeAppManagerClient client, FixedTimeProvider time)
    {
        var store = new SessionTokenStore();
        store.SetSession(User(), "access-1", "refresh-1", time.GetUtcNow().AddYears(1));

        var options = Options.Create(new LicensingOptions
        {
            LicenseGraceHours = 72,
            LicenseRevalidationMinutes = 60
        });

        var license = new LicenseService(
            client,
            new InMemoryLicenseCacheRepository(),
            TestFactory.Mode(appManagerEnabled: true),
            new StubUserContext(User()),
            store,
            new StubTokenRefresher(),
            options,
            time,
            NullLogger<LicenseService>.Instance);

        var mode = new InstanceModeService(license, options, NullLogger<InstanceModeService>.Instance);
        return (license, mode);
    }

    /// <summary>Walks up from the test assembly to the directory holding <c>TechieRag.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>An <see cref="ILicenseService"/> that fails on every call, to prove fail-open.</summary>
    private sealed class ThrowingLicenseService : ILicenseService
    {
        public LicenseStatus Current => throw new InvalidOperationException("licence subsystem is broken");

        public Task<LicenseStatus> ValidateAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("licence subsystem is broken");

        public Task<LicenseStatus> EnsureFreshAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("licence subsystem is broken");
    }
}
