namespace TechieDesk.Services.Auth;

/// <summary>
/// Fine-grained product capabilities gated by role (BRD-24, BRD §5 role matrix). The
/// role-to-capability assignment lives in <see cref="CapabilityService"/>'s single data table.
/// </summary>
public enum Capability
{
    // Admin-only — instance administration.

    /// <summary>Manage instance-wide settings (providers, defaults).</summary>
    ManageInstanceSettings,

    /// <summary>Configure LLM/embedding providers.</summary>
    ManageProviders,

    /// <summary>See and manage every workspace in the instance.</summary>
    ManageAllWorkspaces,

    /// <summary>Access the admin console (<c>/admin/*</c>).</summary>
    AccessAdminConsole,

    /// <summary>Create and revoke API keys.</summary>
    ManageApiKeys,

    /// <summary>Customize branding/white-label settings.</summary>
    ManageBranding,

    /// <summary>View instance logs.</summary>
    ViewLogs,

    /// <summary>Administer the Qdrant vector store (<c>/qdrant-admin</c>).</summary>
    ManageQdrant,

    /// <summary>Configure MCP servers.</summary>
    ManageMcpServers,

    // Manager and above — workspace/content management.

    /// <summary>Create and manage workspaces.</summary>
    ManageWorkspaces,

    /// <summary>Manage the document library (ingest, delete, re-embed).</summary>
    ManageDocuments,

    /// <summary>Manage data connectors.</summary>
    ManageConnectors,

    /// <summary>Tune retrieval settings (threshold, topK, chunking).</summary>
    TuneRetrieval,

    /// <summary>Assign users to workspaces.</summary>
    AssignUsersToWorkspaces,

    // Every authenticated user — own data.

    /// <summary>Chat in assigned workspaces.</summary>
    ChatInAssignedWorkspaces,

    /// <summary>Create and manage own threads.</summary>
    ManageOwnThreads,

    /// <summary>Export own chat history.</summary>
    ExportOwnHistory,

    /// <summary>View and update own profile.</summary>
    ManageOwnProfile,

    /// <summary>View own licenses and billing.</summary>
    ViewOwnLicenses,

    /// <summary>Create and comment on own support tickets.</summary>
    SubmitSupportTickets
}
