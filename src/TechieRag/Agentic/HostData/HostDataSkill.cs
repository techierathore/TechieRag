using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Orchestration;

namespace TechieRag.Agentic.HostData;

/// <summary>
/// The host-data skill: the host registers its own entities and the agent gets one tool set for all of
/// them, so a host's assistant reads and changes the host's data in plain words (REQ-RAG-153 / BRD-211).
/// Every change comes back as a typed <see cref="HostDataChange"/> that <see cref="UndoAsync"/> reverses
/// through the host's own handlers (REQ-RAG-154 / BRD-212).
/// </summary>
/// <remarks>
/// <para><b>One tool set, not one tool per entity.</b> Five tools — <c>host_data_list</c>, <c>_read</c>,
/// <c>_create</c>, <c>_update</c>, <c>_delete</c> — each take an <c>entity</c> argument; a tool appears only
/// when some entity offers its operation, and its <c>entity</c> enum names only those entities. Eleven entities
/// stay five tools.</para>
/// <para><b>It is an <see cref="IToolHandler"/></b>, so it composes with everything that takes one: the agent
/// loop, flows, <see cref="Services.CompositeToolHandler"/> beside other tools and MCP, guardrails
/// (<see cref="GuardedToolHandler"/>) and, through the seam adapters of <c>TechieRag.Agents</c>, a Microsoft Agent
/// Framework agent (ADR-011). Tool calls show in the trace like any other.</para>
/// <para><b>The host's handlers never see unchecked input.</b> The fields the model sends are checked against the
/// entity's <see cref="HostEntityField"/>s (known name, value type, choice, required on create) first; a
/// mismatch goes back to the model as a failed tool result it can correct, with a
/// <see cref="HostDataMessageCodes"/> code for the person reading the trace.</para>
/// <para><b>Turns.</b> Call <see cref="BeginTurn"/> with the host's turn id at the start of each user turn; each
/// change carries it, and <see cref="TurnChanges"/> lists the changes since. Thread-safe: a model may issue
/// parallel tool calls.</para>
/// </remarks>
public sealed partial class HostDataSkill : IToolHandler
{
    private const string ListSuffix = "_list";
    private const string ReadSuffix = "_read";
    private const string CreateSuffix = "_create";
    private const string UpdateSuffix = "_update";
    private const string DeleteSuffix = "_delete";

    private static readonly JsonSerializerOptions ResultJson = new() { WriteIndented = false };

    private readonly Dictionary<string, IHostEntity> entities = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IHostEntity> ordered = [];
    private readonly HostDataSkillOptions options;
    private readonly object gate = new();
    private readonly List<HostDataChange> turnChanges = [];
    private IReadOnlyList<ToolDefinition>? definitions;
    private string? turnId;

    /// <summary>Initializes a new instance of the <see cref="HostDataSkill"/> class.</summary>
    /// <param name="options">The options; null uses the defaults.</param>
    /// <exception cref="ArgumentException">The tool prefix is not a lower-case identifier, or the list limit is below 1.</exception>
    public HostDataSkill(HostDataSkillOptions? options = null)
    {
        this.options = options ?? new HostDataSkillOptions();
        if (!IdentifierPattern().IsMatch(this.options.ToolPrefix))
        {
            throw new ArgumentException("The tool prefix must be a lower-case identifier, for example host_data.", nameof(options));
        }

        if (this.options.MaxListResults < 1)
        {
            throw new ArgumentException("MaxListResults must be at least 1.", nameof(options));
        }
    }

    /// <summary>Raised after every change the skill makes, including an undo.</summary>
    public event EventHandler<HostDataChange>? ChangeRecorded;

    /// <summary>Gets the registered entities, in registration order.</summary>
    public IReadOnlyList<IHostEntity> Entities
    {
        get
        {
            lock (gate)
            {
                return ordered.ToArray();
            }
        }
    }

    /// <summary>Gets the changes made since the last <see cref="BeginTurn"/>, oldest first.</summary>
    public IReadOnlyList<HostDataChange> TurnChanges
    {
        get
        {
            lock (gate)
            {
                return turnChanges.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions
    {
        get
        {
            lock (gate)
            {
                return definitions ??= BuildDefinitions();
            }
        }
    }

    /// <summary>Registers an entity.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>This skill, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is not a lower-case identifier or is taken; a field name repeats
    /// or is not an identifier; a choice field has no choices; or update or delete is offered without read.</exception>
    public HostDataSkill Register(IHostEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Validate(entity);

        lock (gate)
        {
            if (!entities.TryAdd(entity.Name, entity))
            {
                throw new ArgumentException($"An entity named '{entity.Name}' is already registered.", nameof(entity));
            }

            ordered.Add(entity);
            definitions = null;
        }

        return this;
    }

    /// <summary>Starts a user turn: later changes carry <paramref name="turnId"/> and <see cref="TurnChanges"/> is cleared.</summary>
    /// <param name="turnId">The host's id for the turn; null for none.</param>
    public void BeginTurn(string? turnId)
    {
        lock (gate)
        {
            this.turnId = turnId;
            turnChanges.Clear();
        }
    }

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var operation = OperationOf(toolCall.Name);
        if (operation == HostEntityOperations.None)
        {
            return Failure(toolCall, $"error: unknown tool '{toolCall.Name}'.",
                FlowMessage.Create(HostDataMessageCodes.InvalidArguments, "The tool '{0}' is not offered: {1}", toolCall.Name, "unknown tool"));
        }

        JsonObject arguments;
        try
        {
            arguments = JsonNode.Parse(string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson) as JsonObject
                ?? throw new JsonException("the arguments are not a JSON object");
        }
        catch (JsonException exception)
        {
            return Failure(toolCall, $"error: the arguments are not valid JSON: {exception.Message}",
                FlowMessage.Create(HostDataMessageCodes.InvalidArguments, "The tool '{0}' got arguments it cannot read: {1}", toolCall.Name, exception.Message));
        }

        var entityName = (arguments["entity"] as JsonValue)?.TryGetValue<string>(out var named) == true ? named : null;
        IHostEntity? entity;
        lock (gate)
        {
            entity = entityName is null ? null : entities.GetValueOrDefault(entityName);
        }

        if (entity is null)
        {
            return Failure(toolCall,
                $"error: unknown entity '{entityName}'. Known entities: {string.Join(", ", Entities.Select(e => e.Name))}.",
                FlowMessage.Create(HostDataMessageCodes.UnknownEntity, "No entity named '{0}' is registered.", entityName ?? string.Empty));
        }

        if (!entity.Operations.HasFlag(operation))
        {
            return Failure(toolCall, $"error: the '{entity.Name}' entity does not offer {operation.ToString().ToLowerInvariant()}.",
                FlowMessage.Create(HostDataMessageCodes.OperationNotOffered, "The '{0}' entity does not offer {1}.", entity.Name, operation.ToString()));
        }

        try
        {
            return operation switch
            {
                HostEntityOperations.List => await ListAsync(toolCall, entity, arguments, cancellationToken).ConfigureAwait(false),
                HostEntityOperations.Read => await ReadAsync(toolCall, entity, arguments, cancellationToken).ConfigureAwait(false),
                HostEntityOperations.Create => await CreateAsync(toolCall, entity, arguments, cancellationToken).ConfigureAwait(false),
                HostEntityOperations.Update => await UpdateAsync(toolCall, entity, arguments, cancellationToken).ConfigureAwait(false),
                _ => await DeleteAsync(toolCall, entity, arguments, cancellationToken).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(toolCall, $"error: the host could not {operation.ToString().ToLowerInvariant()} the {entity.Name}: {exception.Message}",
                FlowMessage.Create(HostDataMessageCodes.HandlerFailed, "The '{0}' entity failed to {1}: {2}", entity.Name, operation.ToString(), exception.Message));
        }
    }

    /// <summary>
    /// Reverses a change through the host's own handlers (REQ-RAG-154 / BRD-212): a created record is deleted,
    /// an updated record gets its "before" values back through the update handler, and a deleted record is
    /// created again from its "before" values (the host may give it a new id).
    /// </summary>
    /// <param name="change">The change to reverse, as the skill returned it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The change record of the reversal, with <see cref="HostDataChange.UndoOf"/> set; it is also raised
    /// through <see cref="ChangeRecorded"/> and <see cref="HostDataSkillOptions.OnChangeAsync"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    /// <exception cref="HostDataException">The entity is not registered, does not offer the reversing operation, or the
    /// record lacks the state the reversal needs.</exception>
    public async Task<HostDataChange> UndoAsync(HostDataChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        IHostEntity? entity;
        lock (gate)
        {
            entity = entities.GetValueOrDefault(change.Entity);
        }

        if (entity is null)
        {
            throw new HostDataException(FlowMessage.Create(HostDataMessageCodes.UnknownEntity, "No entity named '{0}' is registered.", change.Entity));
        }

        HostDataChange reversal;
        switch (change.Kind)
        {
            case HostDataChangeKind.Created:
                RequireOperation(entity, change, HostEntityOperations.Delete);
                await entity.DeleteAsync(change.EntityId, cancellationToken).ConfigureAwait(false);
                reversal = new HostDataChange
                {
                    Entity = entity.Name,
                    EntityId = change.EntityId,
                    Kind = HostDataChangeKind.Deleted,
                    Before = change.After,
                    UndoOf = change.ChangeId
                };
                break;

            case HostDataChangeKind.Updated:
                RequireOperation(entity, change, HostEntityOperations.Update);
                var before = change.Before ?? throw CannotUndo(change, "the record has no 'before' state");
                var current = entity.Operations.HasFlag(HostEntityOperations.Read)
                    ? await entity.ReadAsync(change.EntityId, cancellationToken).ConfigureAwait(false)
                    : change.After;
                var restored = await entity.UpdateAsync(change.EntityId, before.Values, cancellationToken).ConfigureAwait(false);
                reversal = new HostDataChange
                {
                    Entity = entity.Name,
                    EntityId = change.EntityId,
                    Kind = HostDataChangeKind.Updated,
                    Before = current,
                    After = restored,
                    UndoOf = change.ChangeId
                };
                break;

            default:
                RequireOperation(entity, change, HostEntityOperations.Create);
                var deleted = change.Before ?? throw CannotUndo(change, "the record has no 'before' state");
                var recreated = await entity.CreateAsync(deleted.Values, cancellationToken).ConfigureAwait(false);
                reversal = new HostDataChange
                {
                    Entity = entity.Name,
                    EntityId = recreated.Id,
                    Kind = HostDataChangeKind.Created,
                    After = recreated,
                    UndoOf = change.ChangeId
                };
                break;
        }

        await PublishAsync(reversal, cancellationToken).ConfigureAwait(false);
        return reversal;
    }

    /// <summary>Runs a list call.</summary>
    private async Task<ToolResult> ListAsync(ToolCall toolCall, IHostEntity entity, JsonObject arguments, CancellationToken cancellationToken)
    {
        var text = (arguments["query"] as JsonValue)?.TryGetValue<string>(out var query) == true && !string.IsNullOrWhiteSpace(query) ? query : null;
        var limit = options.MaxListResults;
        if ((arguments["limit"] as JsonValue)?.TryGetValue<int>(out var asked) == true && asked > 0)
        {
            limit = Math.Min(asked, options.MaxListResults);
        }

        var records = await entity.ListAsync(new HostEntityQuery(text, limit), cancellationToken).ConfigureAwait(false);
        var payload = new JsonObject
        {
            ["entity"] = entity.Name,
            ["count"] = Math.Min(records.Count, limit),
            ["records"] = new JsonArray(records.Take(limit).Select(ToJson).ToArray<JsonNode?>())
        };
        return Success(toolCall, payload);
    }

    /// <summary>Runs a read call.</summary>
    private async Task<ToolResult> ReadAsync(ToolCall toolCall, IHostEntity entity, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (RequireId(toolCall, arguments) is not { } id)
        {
            return MissingId(toolCall);
        }

        var record = await entity.ReadAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound(toolCall, entity, id)
            : Success(toolCall, new JsonObject { ["entity"] = entity.Name, ["record"] = ToJson(record) });
    }

    /// <summary>Runs a create call.</summary>
    private async Task<ToolResult> CreateAsync(ToolCall toolCall, IHostEntity entity, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (CheckFields(entity, arguments, requireAll: true, out var values) is { } problem)
        {
            return InvalidFields(toolCall, entity, problem);
        }

        var created = await entity.CreateAsync(values, cancellationToken).ConfigureAwait(false);
        var change = await RecordAsync(toolCall, entity, created.Id, HostDataChangeKind.Created, before: null, after: created, cancellationToken).ConfigureAwait(false);
        return Changed(toolCall, entity, change, created);
    }

    /// <summary>Runs an update call.</summary>
    private async Task<ToolResult> UpdateAsync(ToolCall toolCall, IHostEntity entity, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (RequireId(toolCall, arguments) is not { } id)
        {
            return MissingId(toolCall);
        }

        if (CheckFields(entity, arguments, requireAll: false, out var values) is { } problem)
        {
            return InvalidFields(toolCall, entity, problem);
        }

        if (values.Count == 0)
        {
            return InvalidFields(toolCall, entity, "no fields to change were given");
        }

        var before = await entity.ReadAsync(id, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return NotFound(toolCall, entity, id);
        }

        var after = await entity.UpdateAsync(id, values, cancellationToken).ConfigureAwait(false);
        var change = await RecordAsync(toolCall, entity, id, HostDataChangeKind.Updated, before, after, cancellationToken).ConfigureAwait(false);
        return Changed(toolCall, entity, change, after);
    }

    /// <summary>Runs a delete call.</summary>
    private async Task<ToolResult> DeleteAsync(ToolCall toolCall, IHostEntity entity, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (RequireId(toolCall, arguments) is not { } id)
        {
            return MissingId(toolCall);
        }

        var before = await entity.ReadAsync(id, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return NotFound(toolCall, entity, id);
        }

        await entity.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        var change = await RecordAsync(toolCall, entity, id, HostDataChangeKind.Deleted, before, after: null, cancellationToken).ConfigureAwait(false);
        return Changed(toolCall, entity, change, record: null);
    }

    /// <summary>Builds, keeps and publishes the change record of a tool call.</summary>
    private async Task<HostDataChange> RecordAsync(
        ToolCall toolCall,
        IHostEntity entity,
        string entityId,
        HostDataChangeKind kind,
        HostEntityRecord? before,
        HostEntityRecord? after,
        CancellationToken cancellationToken)
    {
        string? currentTurn;
        lock (gate)
        {
            currentTurn = turnId;
        }

        var change = new HostDataChange
        {
            Entity = entity.Name,
            EntityId = entityId,
            Kind = kind,
            Before = before,
            After = after,
            TurnId = currentTurn,
            ToolCallId = toolCall.Id
        };

        lock (gate)
        {
            turnChanges.Add(change);
        }

        await PublishAsync(change, cancellationToken).ConfigureAwait(false);
        return change;
    }

    /// <summary>Hands a change to the host: the callback first (awaited), then the event.</summary>
    private async Task PublishAsync(HostDataChange change, CancellationToken cancellationToken)
    {
        if (options.OnChangeAsync is { } callback)
        {
            await callback(change, cancellationToken).ConfigureAwait(false);
        }

        ChangeRecorded?.Invoke(this, change);
    }

    /// <summary>
    /// Checks the model's <c>fields</c> object against the entity: known names, value types, choices, and on a
    /// create every required field present and non-null.
    /// </summary>
    /// <returns>Null when the fields fit, else the problems in one English sentence for the model.</returns>
    private static string? CheckFields(IHostEntity entity, JsonObject arguments, bool requireAll, out Dictionary<string, JsonElement> values)
    {
        var collected = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        values = collected;
        var problems = new List<string>();
        var fields = arguments["fields"] as JsonObject;
        if (arguments["fields"] is not null and not JsonObject)
        {
            return "'fields' must be a JSON object of field names to values";
        }

        var known = entity.Fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, node) in fields ?? new JsonObject())
        {
            if (!known.TryGetValue(name, out var field))
            {
                problems.Add($"'{name}' is not a field of {entity.Name} (fields: {string.Join(", ", entity.Fields.Select(f => f.Name))})");
                continue;
            }

            var element = node is null ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(node);
            if (element.ValueKind != JsonValueKind.Null && CheckValue(field, element) is { } problem)
            {
                problems.Add(problem);
                continue;
            }

            if (element.ValueKind == JsonValueKind.Null && field.Required)
            {
                problems.Add($"'{field.Name}' is required and cannot be null");
                continue;
            }

            values[field.Name] = element;
        }

        if (requireAll)
        {
            problems.AddRange(entity.Fields
                .Where(field => field.Required && !collected.ContainsKey(field.Name) && !problems.Any(p => p.StartsWith($"'{field.Name}'", StringComparison.Ordinal)))
                .Select(field => $"'{field.Name}' is required"));
        }

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>Checks one non-null value against its field's type.</summary>
    /// <returns>Null when it fits, else the problem.</returns>
    private static string? CheckValue(HostEntityField field, JsonElement value) => field.Type switch
    {
        HostFieldType.Text when value.ValueKind != JsonValueKind.String => $"'{field.Name}' must be text",
        HostFieldType.Number when value.ValueKind != JsonValueKind.Number => $"'{field.Name}' must be a number",
        HostFieldType.Boolean when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) => $"'{field.Name}' must be true or false",
        HostFieldType.Date when value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            => $"'{field.Name}' must be a date as yyyy-MM-dd",
        HostFieldType.DateTime when value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _)
            => $"'{field.Name}' must be an ISO 8601 date and time, for example 2026-11-08T15:00:00",
        HostFieldType.Choice when value.ValueKind != JsonValueKind.String
            || !field.Choices.Contains(value.GetString()!, StringComparer.OrdinalIgnoreCase)
            => $"'{field.Name}' must be one of: {string.Join(", ", field.Choices)}",
        _ => null
    };

    /// <summary>Checks an entity's shape before it is registered.</summary>
    private static void Validate(IHostEntity entity)
    {
        if (string.IsNullOrEmpty(entity.Name) || !IdentifierPattern().IsMatch(entity.Name))
        {
            throw new ArgumentException($"The entity name '{entity.Name}' must be a lower-case identifier, for example task.", nameof(entity));
        }

        if (string.IsNullOrWhiteSpace(entity.Description))
        {
            throw new ArgumentException($"The '{entity.Name}' entity needs a description for the model.", nameof(entity));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in entity.Fields ?? throw new ArgumentException($"The '{entity.Name}' entity has no field list.", nameof(entity)))
        {
            if (field is null || !FieldPattern().IsMatch(field.Name ?? string.Empty))
            {
                throw new ArgumentException($"A field of '{entity.Name}' has a name that is not an identifier.", nameof(entity));
            }

            if (!names.Add(field.Name!))
            {
                throw new ArgumentException($"The '{entity.Name}' entity names the field '{field.Name}' twice.", nameof(entity));
            }

            if (field.Type == HostFieldType.Choice && field.Choices.Count == 0)
            {
                throw new ArgumentException($"The choice field '{entity.Name}.{field.Name}' has no choices.", nameof(entity));
            }
        }

        var operations = entity.Operations;
        if ((operations.HasFlag(HostEntityOperations.Update) || operations.HasFlag(HostEntityOperations.Delete))
            && !operations.HasFlag(HostEntityOperations.Read))
        {
            throw new ArgumentException(
                $"The '{entity.Name}' entity offers update or delete without read; the skill reads a record first to record its 'before' state.",
                nameof(entity));
        }
    }

    /// <summary>Builds the tool definitions from the registered entities.</summary>
    private IReadOnlyList<ToolDefinition> BuildDefinitions()
    {
        var list = new List<ToolDefinition>();
        var prefix = options.ToolPrefix;

        Add(HostEntityOperations.List, ListSuffix,
            "Lists or finds records of the user's own data in this app. Use it before reading, changing or deleting a record whose id you do not know.",
            entityNames => Schema(entityNames, new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Words to find, for example part of a title. Omit to list recent records." },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = options.MaxListResults }
            }, []));

        Add(HostEntityOperations.Read, ReadSuffix,
            "Reads one record of the user's own data in this app by its id.",
            entityNames => Schema(entityNames, new JsonObject { ["id"] = IdProperty() }, ["id"]));

        Add(HostEntityOperations.Create, CreateSuffix,
            "Creates a record in the user's own data in this app, with the field values taken from what the user said.",
            entityNames => Schema(entityNames, new JsonObject { ["fields"] = FieldsProperty("The new record's field values.") }, ["fields"]));

        Add(HostEntityOperations.Update, UpdateSuffix,
            "Changes fields of one record in the user's own data in this app; fields not named stay as they are.",
            entityNames => Schema(entityNames, new JsonObject { ["id"] = IdProperty(), ["fields"] = FieldsProperty("Only the fields to change, with their new values.") }, ["id", "fields"]));

        Add(HostEntityOperations.Delete, DeleteSuffix,
            "Deletes one record from the user's own data in this app. The user can undo it.",
            entityNames => Schema(entityNames, new JsonObject { ["id"] = IdProperty() }, ["id"]),
            options.DeleteRequiresConfirmation);

        return list;

        void Add(HostEntityOperations operation, string suffix, string purpose, Func<string[], string> schema, bool confirm = false)
        {
            var offering = ordered.Where(entity => entity.Operations.HasFlag(operation)).ToArray();
            if (offering.Length == 0)
            {
                return;
            }

            list.Add(new ToolDefinition
            {
                Name = prefix + suffix,
                Description = purpose + " Entities:" + Environment.NewLine + Describe(offering),
                ParametersSchema = schema(offering.Select(entity => entity.Name).ToArray()),
                RequiresConfirmation = confirm
            });
        }
    }

    /// <summary>Describes entities and their fields for a tool description.</summary>
    private static string Describe(IEnumerable<IHostEntity> offering)
    {
        var text = new StringBuilder();
        foreach (var entity in offering)
        {
            text.Append("- ").Append(entity.Name).Append(": ").Append(entity.Description.Trim()).Append(" Fields: ");
            text.AppendJoin("; ", entity.Fields.Select(field =>
                $"{field.Name} ({TypeText(field)}{(field.Required ? ", required" : string.Empty)}) {field.Description.Trim()}".TrimEnd()));
            text.AppendLine(".");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>The type of a field as the model reads it.</summary>
    private static string TypeText(HostEntityField field) => field.Type switch
    {
        HostFieldType.Text => "text",
        HostFieldType.Number => "number",
        HostFieldType.Boolean => "true or false",
        HostFieldType.Date => "date yyyy-MM-dd",
        HostFieldType.DateTime => "date and time, ISO 8601",
        _ => "one of " + string.Join(" | ", field.Choices)
    };

    /// <summary>Builds a tool's parameter schema around the entity enum.</summary>
    private static string Schema(string[] entityNames, JsonObject properties, string[] required)
    {
        var all = new JsonObject
        {
            ["entity"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(entityNames.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
                ["description"] = "Which kind of record."
            }
        };
        foreach (var (name, node) in properties.ToArray())
        {
            properties.Remove(name);
            all[name] = node;
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = all,
            ["required"] = new JsonArray(new[] { "entity" }.Concat(required).Select(name => (JsonNode?)JsonValue.Create(name)).ToArray())
        };
        return schema.ToJsonString();
    }

    /// <summary>The <c>id</c> property schema.</summary>
    private static JsonObject IdProperty() => new() { ["type"] = "string", ["description"] = "The record's id, as a list or read returned it." };

    /// <summary>The <c>fields</c> property schema.</summary>
    private static JsonObject FieldsProperty(string description) => new() { ["type"] = "object", ["description"] = description + " Field names and types are listed per entity above." };

    /// <summary>Maps a tool name to its operation.</summary>
    private HostEntityOperations OperationOf(string toolName)
    {
        var prefix = options.ToolPrefix;
        if (toolName is null || !toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return HostEntityOperations.None;
        }

        return toolName[prefix.Length..].ToLowerInvariant() switch
        {
            ListSuffix => HostEntityOperations.List,
            ReadSuffix => HostEntityOperations.Read,
            CreateSuffix => HostEntityOperations.Create,
            UpdateSuffix => HostEntityOperations.Update,
            DeleteSuffix => HostEntityOperations.Delete,
            _ => HostEntityOperations.None
        };
    }

    /// <summary>Reads the <c>id</c> argument.</summary>
    private static string? RequireId(ToolCall toolCall, JsonObject arguments) =>
        arguments["id"] is JsonValue value
            ? value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : value.GetValueKind() == JsonValueKind.Number ? value.ToJsonString() : null
            : null;

    /// <summary>Converts a record to the JSON the model reads.</summary>
    private static JsonObject ToJson(HostEntityRecord record)
    {
        var values = new JsonObject();
        foreach (var (name, value) in record.Values)
        {
            values[name] = JsonNode.Parse(value.GetRawText());
        }

        return new JsonObject { ["id"] = record.Id, ["fields"] = values };
    }

    /// <summary>Throws when an entity does not offer the operation an undo needs.</summary>
    private static void RequireOperation(IHostEntity entity, HostDataChange change, HostEntityOperations operation)
    {
        if (!entity.Operations.HasFlag(operation))
        {
            throw CannotUndo(change, $"the '{entity.Name}' entity does not offer {operation}");
        }
    }

    /// <summary>Builds the exception for a change that cannot be undone.</summary>
    private static HostDataException CannotUndo(HostDataChange change, string reason) =>
        new(FlowMessage.Create(HostDataMessageCodes.CannotUndo, "The change '{0}' cannot be undone: {1}", change.ChangeId, reason));

    /// <summary>A successful result carrying a change.</summary>
    private static ToolResult Changed(ToolCall toolCall, IHostEntity entity, HostDataChange change, HostEntityRecord? record)
    {
        var payload = new JsonObject
        {
            ["entity"] = entity.Name,
            ["id"] = change.EntityId,
            ["change"] = change.Kind.ToString().ToLowerInvariant(),
            ["changeId"] = change.ChangeId
        };
        if (record is not null)
        {
            payload["record"] = ToJson(record);
        }

        return Success(toolCall, payload);
    }

    /// <summary>A successful result.</summary>
    private static ToolResult Success(ToolCall toolCall, JsonObject payload) =>
        new() { ToolCallId = toolCall.Id, Content = payload.ToJsonString(ResultJson) };

    /// <summary>A failed result: English for the model, a coded message for the person.</summary>
    private static ToolResult Failure(ToolCall toolCall, string forModel, FlowMessage forPerson) => new()
    {
        ToolCallId = toolCall.Id,
        Content = forModel,
        IsSuccess = false,
        ErrorMessage = forPerson.Text,
        Message = forPerson
    };

    /// <summary>The result for a missing id.</summary>
    private static ToolResult MissingId(ToolCall toolCall) =>
        Failure(toolCall, "error: 'id' is required; list the records first to find it.",
            FlowMessage.Create(HostDataMessageCodes.InvalidArguments, "The tool '{0}' got arguments it cannot read: {1}", toolCall.Name, "id is required"));

    /// <summary>The result for a record that is not there.</summary>
    private static ToolResult NotFound(ToolCall toolCall, IHostEntity entity, string id) =>
        Failure(toolCall, $"error: no {entity.Name} has the id '{id}'. List the records to find the right one.",
            FlowMessage.Create(HostDataMessageCodes.RecordNotFound, "No '{0}' record has the id '{1}'.", entity.Name, id));

    /// <summary>The result for fields that do not fit.</summary>
    private static ToolResult InvalidFields(ToolCall toolCall, IHostEntity entity, string problem) =>
        Failure(toolCall, $"error: the fields do not fit the {entity.Name}: {problem}.",
            FlowMessage.Create(HostDataMessageCodes.InvalidFields, "The fields do not fit the '{0}' entity: {1}", entity.Name, problem));

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex FieldPattern();
}
