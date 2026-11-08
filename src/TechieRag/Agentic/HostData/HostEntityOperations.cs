namespace TechieRag.Agentic.HostData;

/// <summary>The operations a host entity offers to the agent (REQ-RAG-153 / BRD-211).</summary>
[Flags]
public enum HostEntityOperations
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>List or find records.</summary>
    List = 1,

    /// <summary>Read one record by id.</summary>
    Read = 2,

    /// <summary>Create a record.</summary>
    Create = 4,

    /// <summary>Change fields of a record.</summary>
    Update = 8,

    /// <summary>Delete a record.</summary>
    Delete = 16,

    /// <summary>Every operation.</summary>
    All = List | Read | Create | Update | Delete
}
