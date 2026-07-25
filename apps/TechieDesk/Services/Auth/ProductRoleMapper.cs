namespace TechieDesk.Services.Auth;

/// <summary>
/// Maps the AppManager app-scoped <c>applicationRole</c> string to a <see cref="ProductRole"/>
/// (BRD-23). Unknown or absent roles map to <see cref="ProductRole.User"/>.
/// </summary>
public static class ProductRoleMapper
{
    /// <summary>
    /// Maps an application role code to the corresponding product role.
    /// </summary>
    /// <param name="applicationRole">The AppManager role code (e.g. <c>Admin</c>), or null.</param>
    /// <returns>
    /// <see cref="ProductRole.Admin"/> or <see cref="ProductRole.Manager"/> for a
    /// case-insensitive match; <see cref="ProductRole.User"/> for everything else.
    /// </returns>
    public static ProductRole Map(string? applicationRole)
    {
        if (string.Equals(applicationRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return ProductRole.Admin;
        }

        if (string.Equals(applicationRole, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return ProductRole.Manager;
        }

        return ProductRole.User;
    }
}
