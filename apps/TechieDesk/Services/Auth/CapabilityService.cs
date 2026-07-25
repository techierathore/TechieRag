using System.Collections.Frozen;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Data-driven implementation of <see cref="ICapabilityService"/>. The entire role matrix
/// (BRD §5, BRD-24) is the single <see cref="Matrix"/> table below: Admin = everything,
/// Manager = workspace/document/connector management plus User capabilities, User = chat in
/// assigned workspaces and own data.
/// </summary>
public sealed class CapabilityService : ICapabilityService
{
    private static readonly Capability[] UserCapabilities =
    {
        Capability.ChatInAssignedWorkspaces,
        Capability.ManageOwnThreads,
        Capability.ExportOwnHistory,
        Capability.ManageOwnProfile,
        Capability.ViewOwnLicenses,
        Capability.SubmitSupportTickets
    };

    private static readonly Capability[] ManagerCapabilities = UserCapabilities
        .Concat(new[]
        {
            Capability.ManageWorkspaces,
            Capability.ManageDocuments,
            Capability.ManageConnectors,
            Capability.TuneRetrieval,
            Capability.AssignUsersToWorkspaces
        })
        .ToArray();

    /// <summary>The single role-to-capability table (Admin holds every capability).</summary>
    private static readonly FrozenDictionary<ProductRole, FrozenSet<Capability>> Matrix =
        new Dictionary<ProductRole, FrozenSet<Capability>>
        {
            [ProductRole.User] = UserCapabilities.ToFrozenSet(),
            [ProductRole.Manager] = ManagerCapabilities.ToFrozenSet(),
            [ProductRole.Admin] = Enum.GetValues<Capability>().ToFrozenSet()
        }.ToFrozenDictionary();

    /// <inheritdoc />
    public bool Has(ProductRole role, Capability capability)
    {
        return Matrix.TryGetValue(role, out var capabilities) && capabilities.Contains(capability);
    }

    /// <inheritdoc />
    public IReadOnlySet<Capability> GetCapabilities(ProductRole role)
    {
        return Matrix.TryGetValue(role, out var capabilities)
            ? capabilities
            : FrozenSet<Capability>.Empty;
    }
}
