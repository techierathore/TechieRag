namespace TechieDesk.Services.Connectors;

/// <summary>
/// One saved connector, as the <c>Connector</c> table holds it (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para><b>There is no token on this class, and there must never be one.</b>
/// <see cref="CredentialRef"/> is a NAME that says "this connector has a credential, look it up under
/// this key" — the value itself lives in the OS credential store (REQ-FN-039). An instance of this
/// class is written verbatim into the application database, read back by the connector hub, and shown
/// in a grid; a secret on it would be a secret in all three places.</para>
/// <para>Mutable with settable properties rather than a record, because Dapper materializes it
/// column-by-column the same way it does <c>Schedule</c> and <c>ScheduleRun</c>.</para>
/// </remarks>
public sealed class ConnectorDefinition
{
    /// <summary>Gets or sets the stable key this connector is addressed by.</summary>
    /// <remarks>Written into <see cref="ConnectorJobPayload.ConnectorId"/> and, through it, onto every schedule that syncs this source.</remarks>
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind of source — see <see cref="ConnectorTypes"/>.</summary>
    public string ConnectorType { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator-facing name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace ingested documents are linked into, or null for the library only.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Gets or sets a value indicating whether ingested documents are pinned into workspace context.</summary>
    public bool Pinned { get; set; }

    /// <summary>Gets or sets the connector-specific configuration, as JSON.</summary>
    /// <remarks>Parsed with <see cref="ConnectorSettings.Parse"/>, which never throws on a damaged value.</remarks>
    public string Settings { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the NAME of this connector's credential, or null when it reads anonymously.
    /// </summary>
    /// <remarks>
    /// Deliberately opaque and deliberately worthless on its own: it identifies a slot in the OS
    /// credential store, and a copy of this database on another machine resolves it to nothing.
    /// </remarks>
    public string? CredentialRef { get; set; }

    /// <summary>Gets or sets when the connector was first saved, in UTC.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets when the connector was last changed, in UTC.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Gets a value indicating whether a credential is expected for this connector.</summary>
    public bool HasCredential => !string.IsNullOrWhiteSpace(CredentialRef);

    /// <summary>Reads the connector-specific configuration.</summary>
    /// <returns>The parsed settings, or an empty instance when the stored value is unreadable.</returns>
    public ConnectorSettings ReadSettings() => ConnectorSettings.Parse(Settings);

    /// <summary>Builds the payload a run of this connector is started with.</summary>
    /// <returns>A payload naming this connector — and, as always, carrying no credential.</returns>
    public ConnectorJobPayload ToPayload() => new()
    {
        ConnectorId = ConnectorId,
        ConnectorType = ConnectorType,
        DisplayName = DisplayName,
        WorkspaceId = WorkspaceId,
        Pinned = Pinned,
    };
}
