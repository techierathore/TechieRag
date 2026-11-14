using System.Text.Json;
using TechieRag.Agentic.HostData;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.Mcp;
using Xunit;

namespace TechieRag.Tests.Agentic;

/// <summary>
/// The host-data skill (REQ-RAG-153 / BRD-211) and its change records with Undo (REQ-RAG-154 / BRD-212).
/// </summary>
public sealed class HostDataSkillTests
{
    /// <summary>
    /// A host registers a "task" entity; the user asks for a task; the model calls the create tool with the
    /// fields it took from the request, and the host's create handler runs with exactly those fields.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-153 AskingForATaskRunsTheTaskCreateHandlerWithTheExtractedFields")]
    public async Task AskingForATaskRunsTheCreateHandler()
    {
        var tasks = new TaskRepository();
        var skill = new HostDataSkill().Register(tasks.Entity());
        var llm = new ScriptedToolCallingLlmProvider(
            "host_data_create",
            """{"entity":"task","fields":{"title":"call the bank","due":"2026-11-08T15:00:00"}}""");
        var runner = new AgentLoopRunner(llm, skill);

        var response = await runner.RunAsync([ChatMessage.User("add task call the bank at 3")]);

        Assert.Equal("done", response.Content);
        var created = Assert.Single(tasks.CreateCalls);
        Assert.Equal("call the bank", created["title"].GetString());
        Assert.Equal("2026-11-08T15:00:00", created["due"].GetString());
        Assert.Contains(llm.OfferedTools!, tool => tool.Name == "host_data_create");
        Assert.Contains(llm.ObservedToolMessages, message => message.Content!.Contains("\"change\":\"created\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// The tools are one set for every entity: five tools whatever the entity count, each naming only the
    /// entities that offer its operation, with each entity's fields in the first tool's description.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-153 EntitiesShareOneToolSetNamingTheirFields")]
    public void EntitiesShareOneToolSet()
    {
        var skill = new HostDataSkill()
            .Register(new TaskRepository().Entity())
            .Register(new HostEntity("place", "A place the user goes.", [new HostEntityField("name", HostFieldType.Text, "The place name.", Required: true)])
            {
                List = (_, _) => Task.FromResult<IReadOnlyList<HostEntityRecord>>([])
            });

        Assert.Equal(
            ["host_data_list", "host_data_read", "host_data_create", "host_data_update", "host_data_delete"],
            skill.ToolDefinitions.Select(tool => tool.Name));

        var list = skill.ToolDefinitions[0];
        Assert.Contains("\"task\"", list.ParametersSchema, StringComparison.Ordinal);
        Assert.Contains("\"place\"", list.ParametersSchema, StringComparison.Ordinal);
        var create = skill.ToolDefinitions[2];
        Assert.DoesNotContain("\"place\"", create.ParametersSchema, StringComparison.Ordinal);
        // Fields are written once, on the first tool, since REQ-RAG-160 (Sevak TR-RAG-061).
        Assert.Contains("title (text, required)", list.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("title (text, required)", create.Description, StringComparison.Ordinal);
        Assert.True(skill.ToolDefinitions[4].RequiresConfirmation);
    }

    /// <summary>Fields that do not fit go back to the model as a failed result, and the host's handler never runs.</summary>
    [Fact(DisplayName = "REQ-RAG-153 FieldsThatDoNotFitNeverReachTheHostHandler")]
    public async Task FieldsThatDoNotFitNeverReachTheHandler()
    {
        var tasks = new TaskRepository();
        var skill = new HostDataSkill().Register(tasks.Entity());

        var missing = await skill.ExecuteToolAsync(Call("host_data_create", """{"entity":"task","fields":{"due":"2026-11-08T15:00:00"}}"""));
        var unknown = await skill.ExecuteToolAsync(Call("host_data_create", """{"entity":"task","fields":{"title":"x","colour":"red"}}"""));
        var badChoice = await skill.ExecuteToolAsync(Call("host_data_create", """{"entity":"task","fields":{"title":"x","priority":"urgent"}}"""));
        var badEntity = await skill.ExecuteToolAsync(Call("host_data_create", """{"entity":"goal","fields":{"title":"x"}}"""));

        Assert.All([missing, unknown, badChoice, badEntity], result => Assert.False(result.IsSuccess));
        Assert.Equal(HostDataMessageCodes.InvalidFields, missing.Message!.Code);
        Assert.Contains("'title' is required", missing.Content, StringComparison.Ordinal);
        Assert.Contains("'colour' is not a field", unknown.Content, StringComparison.Ordinal);
        Assert.Contains("one of: low, normal, high", badChoice.Content, StringComparison.Ordinal);
        Assert.Equal(HostDataMessageCodes.UnknownEntity, badEntity.Message!.Code);
        Assert.Empty(tasks.CreateCalls);
    }

    /// <summary>List and read return the host's records to the model as JSON.</summary>
    [Fact]
    public async Task ListAndReadReturnTheHostRecords()
    {
        var tasks = new TaskRepository();
        var id = tasks.Seed("buy milk");
        var skill = new HostDataSkill().Register(tasks.Entity());

        var list = await skill.ExecuteToolAsync(Call("host_data_list", """{"entity":"task","query":"milk"}"""));
        var read = await skill.ExecuteToolAsync(Call("host_data_read", $$"""{"entity":"task","id":"{{id}}"}"""));
        var missing = await skill.ExecuteToolAsync(Call("host_data_read", """{"entity":"task","id":"nope"}"""));

        Assert.Contains("buy milk", list.Content, StringComparison.Ordinal);
        Assert.Contains(id, read.Content, StringComparison.Ordinal);
        Assert.Equal(HostDataMessageCodes.RecordNotFound, missing.Message!.Code);
    }

    /// <summary>
    /// Every change comes back as a typed record — entity, id, before, after, the turn that caused it — through
    /// the awaited callback, the event and the turn's list.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-154 EveryChangeComesBackAsATypedChangeRecord")]
    public async Task EveryChangeComesBackAsATypedRecord()
    {
        var tasks = new TaskRepository();
        var id = tasks.Seed("draft report");
        var stored = new List<HostDataChange>();
        var raised = new List<HostDataChange>();
        var skill = new HostDataSkill(new HostDataSkillOptions
        {
            OnChangeAsync = (change, _) => { stored.Add(change); return Task.CompletedTask; }
        }).Register(tasks.Entity());
        skill.ChangeRecorded += (_, change) => raised.Add(change);
        skill.BeginTurn("turn-7");

        await skill.ExecuteToolAsync(Call("host_data_update", $$$"""{"entity":"task","id":"{{{id}}}","fields":{"title":"final report"}}""", "call-1"));
        await skill.ExecuteToolAsync(Call("host_data_delete", $$"""{"entity":"task","id":"{{id}}"}""", "call-2"));

        Assert.Equal(2, stored.Count);
        Assert.Equal(stored, raised);
        Assert.Equal(stored, skill.TurnChanges);

        var update = stored[0];
        Assert.Equal(("task", id, HostDataChangeKind.Updated, "turn-7", "call-1"), (update.Entity, update.EntityId, update.Kind, update.TurnId, update.ToolCallId));
        Assert.Equal("draft report", update.Before!.GetString("title"));
        Assert.Equal("final report", update.After!.GetString("title"));

        var delete = stored[1];
        Assert.Equal(HostDataChangeKind.Deleted, delete.Kind);
        Assert.Equal("final report", delete.Before!.GetString("title"));
        Assert.Null(delete.After);

        skill.BeginTurn("turn-8");
        Assert.Empty(skill.TurnChanges);
    }

    /// <summary>
    /// Undo of an update restores the "before" state through the host's own update handler, and is itself
    /// reported as a change that names the change it reverses.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-154 UndoRestoresTheBeforeStateThroughTheHostHandler")]
    public async Task UndoRestoresTheBeforeState()
    {
        var tasks = new TaskRepository();
        var id = tasks.Seed("draft report", priority: "low");
        var skill = new HostDataSkill().Register(tasks.Entity());
        await skill.ExecuteToolAsync(Call("host_data_update", $$$"""{"entity":"task","id":"{{{id}}}","fields":{"title":"final report","priority":"high"}}"""));
        var change = Assert.Single(skill.TurnChanges);

        var reversal = await skill.UndoAsync(change);

        Assert.Equal("draft report", tasks.Get(id)!.GetString("title"));
        Assert.Equal("low", tasks.Get(id)!.GetString("priority"));
        Assert.Equal(2, tasks.UpdateCalls);
        Assert.Equal(change.ChangeId, reversal.UndoOf);
        Assert.Equal("final report", reversal.Before!.GetString("title"));
        Assert.Equal("draft report", reversal.After!.GetString("title"));
    }

    /// <summary>Undo of a create deletes the record; undo of a delete creates it again from its "before" values.</summary>
    [Fact(DisplayName = "REQ-RAG-154 UndoReversesCreatesAndDeletes")]
    public async Task UndoReversesCreatesAndDeletes()
    {
        var tasks = new TaskRepository();
        var skill = new HostDataSkill().Register(tasks.Entity());

        await skill.ExecuteToolAsync(Call("host_data_create", """{"entity":"task","fields":{"title":"temporary"}}"""));
        var created = Assert.Single(skill.TurnChanges);
        await skill.UndoAsync(created);
        Assert.Null(tasks.Get(created.EntityId));

        var keep = tasks.Seed("keep me", priority: "high");
        skill.BeginTurn("turn-2");
        await skill.ExecuteToolAsync(Call("host_data_delete", $$"""{"entity":"task","id":"{{keep}}"}"""));
        var deleted = Assert.Single(skill.TurnChanges);
        var recreated = await skill.UndoAsync(deleted);

        var restored = tasks.Get(recreated.EntityId);
        Assert.NotNull(restored);
        Assert.Equal("keep me", restored.GetString("title"));
        Assert.Equal("high", restored.GetString("priority"));
    }

    /// <summary>A change record survives being stored as JSON and read back, and still undoes.</summary>
    [Fact]
    public async Task AStoredChangeRecordStillUndoes()
    {
        var tasks = new TaskRepository();
        var id = tasks.Seed("one");
        var skill = new HostDataSkill().Register(tasks.Entity());
        await skill.ExecuteToolAsync(Call("host_data_update", $$$"""{"entity":"task","id":"{{{id}}}","fields":{"title":"two"}}"""));

        var json = JsonSerializer.Serialize(Assert.Single(skill.TurnChanges));
        var stored = JsonSerializer.Deserialize<HostDataChange>(json)!;
        await skill.UndoAsync(stored);

        Assert.Equal("one", tasks.Get(id)!.GetString("title"));
    }

    /// <summary>An undo the entity cannot carry out throws a coded error rather than doing half of it.</summary>
    [Fact]
    public async Task UndoWithoutTheHandlerThrowsACodedError()
    {
        var skill = new HostDataSkill().Register(new HostEntity("note", "A note.", [new HostEntityField("text", HostFieldType.Text, "The text.")])
        {
            Create = (values, _) => Task.FromResult(new HostEntityRecord("n1", values))
        });
        await skill.ExecuteToolAsync(Call("host_data_create", """{"entity":"note","fields":{"text":"hi"}}"""));

        var error = await Assert.ThrowsAsync<HostDataException>(() => skill.UndoAsync(skill.TurnChanges[0]));

        Assert.Equal(HostDataMessageCodes.CannotUndo, error.Code);
    }

    /// <summary>An entity that offers update without read is refused at registration, since no "before" could be recorded.</summary>
    [Fact]
    public void UpdateWithoutReadIsRefused()
    {
        var entity = new HostEntity("note", "A note.", [])
        {
            Update = (id, values, _) => Task.FromResult(new HostEntityRecord(id, values))
        };

        Assert.Throws<ArgumentException>(() => new HostDataSkill().Register(entity));
    }

    /// <summary>Builds a tool call.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="arguments">The arguments JSON.</param>
    /// <param name="id">The call id.</param>
    /// <returns>The call.</returns>
    private static ToolCall Call(string name, string arguments, string id = "call") =>
        new() { Id = id, Name = name, ArgumentsJson = arguments };

    /// <summary>An in-memory task store standing in for a host's repository.</summary>
    private sealed class TaskRepository
    {
        private readonly Dictionary<string, Dictionary<string, JsonElement>> rows = new();
        private int next = 1;

        /// <summary>Gets the field sets the create handler received.</summary>
        public List<IReadOnlyDictionary<string, JsonElement>> CreateCalls { get; } = [];

        /// <summary>Gets how many times the update handler ran.</summary>
        public int UpdateCalls { get; private set; }

        /// <summary>Adds a task directly.</summary>
        /// <param name="title">The title.</param>
        /// <param name="priority">The priority.</param>
        /// <returns>The id.</returns>
        public string Seed(string title, string priority = "normal")
        {
            var id = $"t{next++}";
            rows[id] = new Dictionary<string, JsonElement>
            {
                ["title"] = JsonSerializer.SerializeToElement(title),
                ["priority"] = JsonSerializer.SerializeToElement(priority)
            };
            return id;
        }

        /// <summary>Reads a task.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The record, or null.</returns>
        public HostEntityRecord? Get(string id) =>
            rows.TryGetValue(id, out var values) ? new HostEntityRecord(id, new Dictionary<string, JsonElement>(values)) : null;

        /// <summary>Builds the host entity bound to this store.</summary>
        /// <returns>The entity.</returns>
        public HostEntity Entity() => new(
            "task",
            "A to-do item of the user.",
            [
                new HostEntityField("title", HostFieldType.Text, "What to do.", Required: true),
                new HostEntityField("due", HostFieldType.DateTime, "When it is due, local time."),
                new HostEntityField("priority", HostFieldType.Choice, "How important it is.") { Choices = ["low", "normal", "high"] }
            ])
        {
            List = (query, _) => Task.FromResult<IReadOnlyList<HostEntityRecord>>(rows.Keys
                .Select(id => Get(id)!)
                .Where(record => query.Text is null || record.GetString("title")!.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
                .Take(query.Limit)
                .ToList()),
            Read = (id, _) => Task.FromResult(Get(id)),
            Create = (values, _) =>
            {
                CreateCalls.Add(values);
                var id = $"t{next++}";
                rows[id] = new Dictionary<string, JsonElement>(values);
                return Task.FromResult(Get(id)!);
            },
            Update = (id, values, _) =>
            {
                UpdateCalls++;
                foreach (var (name, value) in values)
                {
                    rows[id][name] = value;
                }

                return Task.FromResult(Get(id)!);
            },
            Delete = (id, _) =>
            {
                rows.Remove(id);
                return Task.CompletedTask;
            }
        };
    }
}
