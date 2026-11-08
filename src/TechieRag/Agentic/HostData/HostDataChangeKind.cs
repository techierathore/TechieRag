namespace TechieRag.Agentic.HostData;

/// <summary>What a host-data change did (REQ-RAG-154 / BRD-212).</summary>
public enum HostDataChangeKind
{
    /// <summary>A record was created; <see cref="HostDataChange.Before"/> is null.</summary>
    Created,

    /// <summary>A record's fields were changed; both states are set.</summary>
    Updated,

    /// <summary>A record was deleted; <see cref="HostDataChange.After"/> is null.</summary>
    Deleted
}
