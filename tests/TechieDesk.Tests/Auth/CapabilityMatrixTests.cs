using TechieDesk.Services.Auth;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-006 / BRD-24: the data-driven role capability matrix — Admin gets everything,
/// Manager gets workspace/document/connector management, User gets chat and own data.
/// </summary>
public sealed class CapabilityMatrixTests
{
    private readonly CapabilityService service = new();

    /// <summary>Admin holds every capability in the enum, including instance administration.</summary>
    [Fact]
    public void AdminHasAllCapabilities()
    {
        Assert.All(Enum.GetValues<Capability>(),
            capability => Assert.True(service.Has(ProductRole.Admin, capability)));
    }

    /// <summary>Manager holds the workspace/document/connector management capabilities.</summary>
    [Theory]
    [InlineData(Capability.ManageWorkspaces)]
    [InlineData(Capability.ManageDocuments)]
    [InlineData(Capability.ManageConnectors)]
    [InlineData(Capability.TuneRetrieval)]
    [InlineData(Capability.AssignUsersToWorkspaces)]
    [InlineData(Capability.ChatInAssignedWorkspaces)]
    public void ManagerManagesWorkspaces(Capability capability)
    {
        Assert.True(service.Has(ProductRole.Manager, capability));
    }

    /// <summary>Manager does not hold instance-administration capabilities.</summary>
    [Theory]
    [InlineData(Capability.ManageInstanceSettings)]
    [InlineData(Capability.AccessAdminConsole)]
    [InlineData(Capability.ManageApiKeys)]
    [InlineData(Capability.ManageQdrant)]
    [InlineData(Capability.ManageAllWorkspaces)]
    public void ManagerLacksInstanceAdmin(Capability capability)
    {
        Assert.False(service.Has(ProductRole.Manager, capability));
    }

    /// <summary>User holds the chat and own-data capabilities.</summary>
    [Theory]
    [InlineData(Capability.ChatInAssignedWorkspaces)]
    [InlineData(Capability.ManageOwnThreads)]
    [InlineData(Capability.ExportOwnHistory)]
    [InlineData(Capability.ManageOwnProfile)]
    [InlineData(Capability.ViewOwnLicenses)]
    [InlineData(Capability.SubmitSupportTickets)]
    public void UserHasOwnDataCapabilities(Capability capability)
    {
        Assert.True(service.Has(ProductRole.User, capability));
    }

    /// <summary>User holds no management capability of any kind.</summary>
    [Theory]
    [InlineData(Capability.ManageWorkspaces)]
    [InlineData(Capability.ManageDocuments)]
    [InlineData(Capability.ManageConnectors)]
    [InlineData(Capability.AssignUsersToWorkspaces)]
    [InlineData(Capability.ManageInstanceSettings)]
    [InlineData(Capability.AccessAdminConsole)]
    public void UserLacksManagement(Capability capability)
    {
        Assert.False(service.Has(ProductRole.User, capability));
    }

    /// <summary>Role capability sets grow monotonically: User ⊂ Manager ⊂ Admin.</summary>
    [Fact]
    public void RolesAreStrictlyNested()
    {
        var userSet = service.GetCapabilities(ProductRole.User);
        var managerSet = service.GetCapabilities(ProductRole.Manager);
        var adminSet = service.GetCapabilities(ProductRole.Admin);

        // Assert.ProperSubset(expectedSuperset, actual): actual must be a proper subset of the
        // first argument. User ⊂ Manager ⊂ Admin, so Admin/Manager are the supersets.
        Assert.ProperSubset(adminSet.ToHashSet(), managerSet.ToHashSet());
        Assert.ProperSubset(managerSet.ToHashSet(), userSet.ToHashSet());
    }
}
