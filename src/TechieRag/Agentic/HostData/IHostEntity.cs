using System.Text.Json;

namespace TechieRag.Agentic.HostData;

/// <summary>
/// One kind of the host's own data the agent may read and change, with the host's handlers for it
/// (REQ-RAG-153 / BRD-211).
/// </summary>
/// <remarks>
/// <para>Register entities on a <see cref="HostDataSkill"/>; the agent gets one tool set for all of them.
/// <see cref="HostEntity"/> builds one from delegates, so a host need not implement this interface.</para>
/// <para>A handler is only called for an operation named in <see cref="Operations"/>. The skill reads a record
/// before it updates or deletes it, to fill the change record's "before" state, so an entity that offers
/// <see cref="HostEntityOperations.Update"/> or <see cref="HostEntityOperations.Delete"/> must offer
/// <see cref="HostEntityOperations.Read"/>.</para>
/// </remarks>
public interface IHostEntity
{
    /// <summary>Gets the entity name the model writes, lower case, for example <c>task</c>.</summary>
    string Name { get; }

    /// <summary>Gets what the entity is, in invariant English for the model.</summary>
    string Description { get; }

    /// <summary>Gets the fields the model may read and write.</summary>
    IReadOnlyList<HostEntityField> Fields { get; }

    /// <summary>Gets the operations this entity offers.</summary>
    HostEntityOperations Operations { get; }

    /// <summary>Lists or finds records.</summary>
    /// <param name="query">What the model asked for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching records, at most <see cref="HostEntityQuery.Limit"/>.</returns>
    Task<IReadOnlyList<HostEntityRecord>> ListAsync(HostEntityQuery query, CancellationToken cancellationToken);

    /// <summary>Reads one record.</summary>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or null when there is none with that id.</returns>
    Task<HostEntityRecord?> ReadAsync(string id, CancellationToken cancellationToken);

    /// <summary>Creates a record.</summary>
    /// <param name="values">The field values, already checked against <see cref="Fields"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created record, with the id the host gave it.</returns>
    Task<HostEntityRecord> CreateAsync(IReadOnlyDictionary<string, JsonElement> values, CancellationToken cancellationToken);

    /// <summary>Changes the named fields of a record and leaves the others.</summary>
    /// <param name="id">The record id.</param>
    /// <param name="values">The fields to change, already checked against <see cref="Fields"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record after the change.</returns>
    Task<HostEntityRecord> UpdateAsync(string id, IReadOnlyDictionary<string, JsonElement> values, CancellationToken cancellationToken);

    /// <summary>Deletes a record.</summary>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the record is deleted.</returns>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
