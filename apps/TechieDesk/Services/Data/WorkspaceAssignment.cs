namespace TechieDesk.Services.Data;

/// <summary>
/// Maps a user into a workspace with a product role (BRD-104 P1 schema).
/// Unique per (WorkspaceId, UserId).
/// </summary>
public sealed class WorkspaceAssignment
{
    /// <summary>Primary key.</summary>
    public long WorkspaceAssignmentId { get; set; }

    /// <summary>Identifier of the workspace the user is assigned to.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>AppManager user identifier.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Product role within the workspace (Admin / Manager / User).</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the assignment was created.</summary>
    public DateTime CreatedAt { get; set; }
}
