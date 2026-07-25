using TechieDesk.Services.Auth;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-005 / BRD-23: mapping the AppManager app-scoped applicationRole to product roles.
/// </summary>
public sealed class RoleMappingTests
{
    /// <summary>An Admin application role maps to the Admin product role.</summary>
    [Fact]
    public void AdminMapsToAdmin()
    {
        Assert.Equal(ProductRole.Admin, ProductRoleMapper.Map("Admin"));
    }

    /// <summary>A Manager application role maps to the Manager product role.</summary>
    [Fact]
    public void ManagerMapsToManager()
    {
        Assert.Equal(ProductRole.Manager, ProductRoleMapper.Map("Manager"));
    }

    /// <summary>Role mapping is case-insensitive.</summary>
    [Theory]
    [InlineData("ADMIN", ProductRole.Admin)]
    [InlineData("admin", ProductRole.Admin)]
    [InlineData("mAnAgEr", ProductRole.Manager)]
    [InlineData("user", ProductRole.User)]
    public void MappingIgnoresCase(string applicationRole, ProductRole expected)
    {
        Assert.Equal(expected, ProductRoleMapper.Map(applicationRole));
    }

    /// <summary>An unknown application role falls back to the User product role.</summary>
    [Theory]
    [InlineData("SuperDuperAdmin")]
    [InlineData("Editor")]
    [InlineData("")]
    public void UnknownRoleMapsToUser(string applicationRole)
    {
        Assert.Equal(ProductRole.User, ProductRoleMapper.Map(applicationRole));
    }

    /// <summary>An absent (null) application role falls back to the User product role.</summary>
    [Fact]
    public void AbsentRoleMapsToUser()
    {
        Assert.Equal(ProductRole.User, ProductRoleMapper.Map(null));
    }
}
