namespace TechieDesk.Services.Backup;

/// <summary>One <c>TrWorkspace</c> row as carried in the archive.</summary>
/// <remarks>
/// These records ARE the credential allow-list for the workspace store. Every column the packer can
/// emit is named here, so widening what an archive carries requires editing this file — it cannot
/// happen by a table gaining a column elsewhere, which is how a secret would otherwise leak in.
/// </remarks>
/// <param name="WorkspaceId">Primary key.</param>
/// <param name="Name">Display name.</param>
/// <param name="SystemPrompt">Workspace system prompt.</param>
/// <param name="LlmModel">Preferred model name; a name only, never a key.</param>
/// <param name="SimilarityThreshold">Retrieval similarity floor.</param>
/// <param name="TopK">Retrieval fan-out.</param>
/// <param name="RerankEnabled">Whether reranking is on, stored as 0/1.</param>
/// <param name="ChatMode">Chat or query mode.</param>
/// <param name="CreatedAt">Creation timestamp, ISO-8601.</param>
/// <param name="UpdatedAt">Last-update timestamp, ISO-8601.</param>
public sealed record BackupWorkspaceRow(
    string WorkspaceId,
    string Name,
    string? SystemPrompt,
    string? LlmModel,
    double? SimilarityThreshold,
    long? TopK,
    long RerankEnabled,
    string ChatMode,
    string CreatedAt,
    string UpdatedAt);

/// <summary>One <c>TrWorkspaceDocument</c> link row as carried in the archive.</summary>
/// <param name="WorkspaceId">Owning workspace.</param>
/// <param name="DocumentId">Catalogue document identifier.</param>
/// <param name="ContentHash">Content hash recorded at attach time.</param>
/// <param name="IsPinned">Whether the document is pinned, stored as 0/1.</param>
/// <param name="AddedAt">Attach timestamp, ISO-8601.</param>
public sealed record BackupWorkspaceDocumentRow(
    string WorkspaceId,
    string DocumentId,
    string ContentHash,
    long IsPinned,
    string AddedAt);

/// <summary>One <c>TrThread</c> row as carried in the archive.</summary>
/// <param name="ThreadId">Primary key.</param>
/// <param name="UserId">Owning user identifier as recorded locally.</param>
/// <param name="WorkspaceId">Owning workspace, when the thread belongs to one.</param>
/// <param name="Title">Thread title.</param>
/// <param name="CreatedAt">Creation timestamp, ISO-8601.</param>
/// <param name="UpdatedAt">Last-update timestamp, ISO-8601.</param>
public sealed record BackupThreadRow(
    string ThreadId,
    string UserId,
    string? WorkspaceId,
    string Title,
    string CreatedAt,
    string UpdatedAt);

/// <summary>One <c>TrMessage</c> row as carried in the archive.</summary>
/// <param name="MessageId">Primary key.</param>
/// <param name="ThreadId">Owning thread.</param>
/// <param name="Role">Message role.</param>
/// <param name="Content">Message body.</param>
/// <param name="SourcesJson">Citation payload as stored.</param>
/// <param name="CreatedAt">Creation timestamp, ISO-8601.</param>
public sealed record BackupMessageRow(
    string MessageId,
    string ThreadId,
    string Role,
    string? Content,
    string? SourcesJson,
    string CreatedAt);

/// <summary>One catalogue <c>Documents</c> row as carried in the archive.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">Display name.</param>
/// <param name="SourcePath">Origin path or URL the document was ingested from.</param>
/// <param name="ChunkCount">Number of chunks produced.</param>
/// <param name="IngestedAt">Ingestion timestamp, ISO-8601.</param>
/// <param name="Metadata">Free-form ingestion metadata as stored.</param>
public sealed record BackupDocumentRow(
    string Id,
    string Name,
    string SourcePath,
    long ChunkCount,
    string IngestedAt,
    string? Metadata);

/// <summary>One <c>Chunks</c> row, embedding vector included, as carried in the archive.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="DocumentId">Owning document.</param>
/// <param name="Text">Chunk text.</param>
/// <param name="Vector">
/// The embedding, serialised as base64 by the JSON writer. Null when the chunk was never embedded,
/// or when a restore discarded a foreign model's vector under
/// <see cref="RestoreChoices.ReEmbedOnModelMismatch"/>.
/// </param>
/// <param name="PageNumber">Source page, when the format had one.</param>
/// <param name="ChunkIndex">Ordinal within the document.</param>
/// <param name="Metadata">Free-form chunk metadata as stored.</param>
/// <param name="CreatedAt">Creation timestamp, ISO-8601.</param>
public sealed record BackupChunkRow(
    string Id,
    string DocumentId,
    string Text,
    byte[]? Vector,
    long? PageNumber,
    long? ChunkIndex,
    string? Metadata,
    string CreatedAt);

/// <summary>A workspace offered on the export screen.</summary>
/// <param name="WorkspaceId">Identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="DocumentCount">Documents attached to it.</param>
/// <param name="ThreadCount">Threads belonging to it.</param>
public sealed record BackupWorkspaceSummary(
    string WorkspaceId,
    string Name,
    int DocumentCount,
    int ThreadCount);
