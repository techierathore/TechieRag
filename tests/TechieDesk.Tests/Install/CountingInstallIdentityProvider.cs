using TechieDesk.Services.Install;

namespace TechieDesk.Tests.Install;

/// <summary>
/// An <see cref="IInstallIdentityProvider"/> that counts how many times it was asked, so a test can
/// assert that an account-free install never resolves an identity at all (BRD-129, REQ-FN-051).
/// </summary>
/// <remarks>
/// Counting is the point. Asserting "the flag is off" would pass against code that computed the
/// identity anyway and merely declined to send it — which is exactly the kind of test this
/// codebase's checklist records as having passed against broken code.
/// </remarks>
public sealed class CountingInstallIdentityProvider : IInstallIdentityProvider
{
    private const string StubInstallId = "1111111111111111111111111111aaaa";
    private const string StubFingerprint = "2222222222222222222222222222bbbb";

    /// <summary>Gets how many times <see cref="Current"/> has been read.</summary>
    public int Resolutions { get; private set; }

    /// <inheritdoc />
    public InstallIdentity Current
    {
        get
        {
            Resolutions++;
            return new InstallIdentity
            {
                InstallId = StubInstallId,
                MachineFingerprint = StubFingerprint,
                CompositeId = InstallIdentityStore.ComposeId(StubInstallId, StubFingerprint),
                CreatedAtUtc = new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
                IsMachineBound = true,
                HasMovedMachine = false
            };
        }
    }
}
