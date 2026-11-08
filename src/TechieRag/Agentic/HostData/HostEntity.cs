using System.Text.Json;

namespace TechieRag.Agentic.HostData;

/// <summary>
/// An <see cref="IHostEntity"/> built from the host's handler delegates (REQ-RAG-153 / BRD-211).
/// </summary>
/// <remarks>
/// <para>A handler left null is an operation the entity does not offer; <see cref="Operations"/> follows from
/// which handlers are set. For example:</para>
/// <code>
/// var task = new HostEntity("task", "A to-do item of the user.",
/// [
///     new HostEntityField("title", HostFieldType.Text, "What to do.", Required: true),
///     new HostEntityField("due", HostFieldType.DateTime, "When it is due, local time.")
/// ])
/// {
///     Create = (values, ct) => repository.CreateAsync(values, ct),
///     Read = (id, ct) => repository.ReadAsync(id, ct)
/// };
/// </code>
/// </remarks>
public sealed class HostEntity : IHostEntity
{
    /// <summary>Initializes a new instance of the <see cref="HostEntity"/> class.</summary>
    /// <param name="name">The entity name the model writes, lower case, for example <c>task</c>.</param>
    /// <param name="description">What the entity is, in invariant English for the model.</param>
    /// <param name="fields">The fields the model may read and write.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="description"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public HostEntity(string name, string description, IReadOnlyList<HostEntityField> fields)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(fields);
        Name = name;
        Description = description;
        Fields = fields;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public IReadOnlyList<HostEntityField> Fields { get; }

    /// <summary>Gets the list handler; null when the entity cannot be listed.</summary>
    public Func<HostEntityQuery, CancellationToken, Task<IReadOnlyList<HostEntityRecord>>>? List { get; init; }

    /// <summary>Gets the read handler; null when a record cannot be read by id.</summary>
    public Func<string, CancellationToken, Task<HostEntityRecord?>>? Read { get; init; }

    /// <summary>Gets the create handler; null when the agent may not create records.</summary>
    public Func<IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<HostEntityRecord>>? Create { get; init; }

    /// <summary>Gets the update handler; null when the agent may not change records.</summary>
    public Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<HostEntityRecord>>? Update { get; init; }

    /// <summary>Gets the delete handler; null when the agent may not delete records.</summary>
    public Func<string, CancellationToken, Task>? Delete { get; init; }

    /// <inheritdoc/>
    public HostEntityOperations Operations =>
        (List is null ? HostEntityOperations.None : HostEntityOperations.List)
        | (Read is null ? HostEntityOperations.None : HostEntityOperations.Read)
        | (Create is null ? HostEntityOperations.None : HostEntityOperations.Create)
        | (Update is null ? HostEntityOperations.None : HostEntityOperations.Update)
        | (Delete is null ? HostEntityOperations.None : HostEntityOperations.Delete);

    /// <inheritdoc/>
    public Task<IReadOnlyList<HostEntityRecord>> ListAsync(HostEntityQuery query, CancellationToken cancellationToken) =>
        (List ?? throw NotOffered(HostEntityOperations.List))(query, cancellationToken);

    /// <inheritdoc/>
    public Task<HostEntityRecord?> ReadAsync(string id, CancellationToken cancellationToken) =>
        (Read ?? throw NotOffered(HostEntityOperations.Read))(id, cancellationToken);

    /// <inheritdoc/>
    public Task<HostEntityRecord> CreateAsync(IReadOnlyDictionary<string, JsonElement> values, CancellationToken cancellationToken) =>
        (Create ?? throw NotOffered(HostEntityOperations.Create))(values, cancellationToken);

    /// <inheritdoc/>
    public Task<HostEntityRecord> UpdateAsync(string id, IReadOnlyDictionary<string, JsonElement> values, CancellationToken cancellationToken) =>
        (Update ?? throw NotOffered(HostEntityOperations.Update))(id, values, cancellationToken);

    /// <inheritdoc/>
    public Task DeleteAsync(string id, CancellationToken cancellationToken) =>
        (Delete ?? throw NotOffered(HostEntityOperations.Delete))(id, cancellationToken);

    /// <summary>Builds the exception for an operation this entity does not offer.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The exception.</returns>
    private NotSupportedException NotOffered(HostEntityOperations operation) =>
        new($"The '{Name}' entity does not offer {operation}.");
}
