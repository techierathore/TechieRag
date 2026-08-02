using Dapper;
using TechieDesk.Services.Flows;
using TechieRag.Orchestration;
using Xunit;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// Flows survive a restart, stay inside their workspace, and one broken row does not take the list
/// down (REQ-UI-040 / BRD-92).
/// </summary>
public sealed class FlowPersistenceTests : IDisposable
{
    private readonly FlowTestHost host = new();

    private const string FinanceWorkspace = "workspace-finance";
    private const string MarketingWorkspace = "workspace-marketing";

    /// <summary>
    /// A composed flow round-trips through storage unchanged, read back by a completely new object
    /// graph over the same file.
    /// </summary>
    /// <remarks>
    /// The second repository is the whole point: it models the process restart that separates durable
    /// storage from a dictionary. Everything the builder sets is asserted, including the
    /// uninterpreted <c>Metadata</c> the canvas uses for layout, because a store that silently drops
    /// the layout is a store that loses the user's work in a way nothing else here would catch.
    /// </remarks>
    [Fact]
    public async Task AFlowSurvivesARestart()
    {
        var flow = SampleFlow("flow-round-trip", "Triage");
        flow.Metadata["zoom"] = "1.25";
        flow.Nodes[0].Metadata["x"] = "120";
        flow.Nodes[0].Metadata["y"] = "40";

        await host.NewRepository().SaveAsync(Record(FinanceWorkspace, flow));

        // A new repository over the same file: nothing is carried in memory from the write.
        var reloaded = await host.NewRepository().FindAsync(FinanceWorkspace, flow.Id);

        Assert.NotNull(reloaded);
        Assert.True(FlowSerializer.TryFromJson(reloaded.DefinitionJson, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal("Triage", parsed.Name);
        Assert.Equal(flow.Nodes.Count, parsed.Nodes.Count);
        Assert.Equal(flow.Edges.Count, parsed.Edges.Count);
        Assert.Equal("analyst", parsed.Nodes[0].AgentId);
        Assert.Equal("1.25", parsed.Metadata["zoom"]);
        Assert.Equal("120", parsed.Nodes[0].Metadata["x"]);
        Assert.Equal("40", parsed.Nodes[0].Metadata["y"]);
    }

    /// <summary>
    /// A flow saved in one workspace is invisible, unreadable and undeletable from another.
    /// </summary>
    /// <remarks>
    /// A flow names agents, tools and guardrails, so it is a capability composition. Listing is only
    /// the obvious half — this also asserts that a caller who already KNOWS the flow id cannot read
    /// or delete it from the wrong workspace, which is the leak a list-only test would miss.
    /// </remarks>
    [Fact]
    public async Task WorkspaceScopingDoesNotLeak()
    {
        var flow = SampleFlow("flow-finance-only", "Ledger review");
        await host.NewRepository().SaveAsync(Record(FinanceWorkspace, flow));

        var repository = host.NewRepository();

        Assert.Empty(await repository.ListAsync(MarketingWorkspace));
        Assert.Null(await repository.FindAsync(MarketingWorkspace, flow.Id));
        Assert.False(await repository.DeleteAsync(MarketingWorkspace, flow.Id));
        Assert.False(await repository.SetEnabledAsync(MarketingWorkspace, flow.Id, false));

        // The flow is still there for the workspace that owns it.
        Assert.Single(await repository.ListAsync(FinanceWorkspace));
    }

    /// <summary>
    /// One row whose stored definition cannot be parsed is reported as such and does not stop the
    /// other flows in the workspace from listing.
    /// </summary>
    /// <remarks>
    /// This is what <c>FlowSerializer.TryFromJson</c> exists for. A repository that threw would take
    /// the whole screen down for a single hand-edited or newer-schema row, leaving the user's other
    /// flows unreachable — and the row a user most wants to delete is exactly the broken one.
    /// </remarks>
    [Fact]
    public async Task ACorruptDefinitionDoesNotBreakTheList()
    {
        var good = SampleFlow("flow-good", "Working flow");
        await host.NewRepository().SaveAsync(Record(FinanceWorkspace, good));

        // Written directly, because the point is a row that did NOT come through the serializer:
        // hand-edited, truncated, or produced by a version this build cannot read.
        await WriteRawAsync("flow-broken", FinanceWorkspace, "Broken flow", "{ this is not json");

        var rows = await host.NewRepository().ListAsync(FinanceWorkspace);
        Assert.Equal(2, rows.Count);

        var items = rows
            .Select(row =>
            {
                FlowSerializer.TryFromJson(row.DefinitionJson, out var flow, out var error);
                return new FlowListItem(row, flow, error);
            })
            .ToList();

        var broken = Assert.Single(items, item => item.Record.FlowId == "flow-broken");
        Assert.False(broken.IsReadable);
        Assert.False(string.IsNullOrWhiteSpace(broken.ReadError));

        var readable = Assert.Single(items, item => item.Record.FlowId == "flow-good");
        Assert.True(readable.IsReadable);
        Assert.Equal("Working flow", readable.Definition!.Name);
    }

    /// <summary>
    /// A flow written by a NEWER schema than this build reads is refused with a reason rather than
    /// partially deserialized.
    /// </summary>
    /// <remarks>
    /// The mirrored <c>SchemaVersion</c> column is what lets a list grey the row out without parsing
    /// the blob; this asserts the parse itself also refuses, so the two cannot disagree.
    /// </remarks>
    [Fact]
    public async Task AFlowFromANewerSchemaIsRefusedWithAReason()
    {
        var future = FlowSerializer.ToJson(SampleFlow("flow-future", "From tomorrow"))
            .Replace(
                $"\"SchemaVersion\": {FlowSerializer.CurrentSchemaVersion}",
                $"\"SchemaVersion\": {FlowSerializer.CurrentSchemaVersion + 1}",
                StringComparison.Ordinal);

        await WriteRawAsync("flow-future", FinanceWorkspace, "From tomorrow", future);

        var row = Assert.Single(await host.NewRepository().ListAsync(FinanceWorkspace));
        Assert.False(FlowSerializer.TryFromJson(row.DefinitionJson, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Contains("schema version", error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Re-saving a flow is an edit: the creation time stays and the edit time moves.</summary>
    [Fact]
    public async Task ResavingAFlowKeepsItsCreationTime()
    {
        var flow = SampleFlow("flow-edited", "First name");
        var created = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        var record = Record(FinanceWorkspace, flow);
        record.CreatedAtUtc = created;
        record.UpdatedAtUtc = created;
        await host.NewRepository().SaveAsync(record);

        flow.Name = "Second name";
        var edit = Record(FinanceWorkspace, flow);
        edit.CreatedAtUtc = created;
        edit.UpdatedAtUtc = created.AddHours(3);
        await host.NewRepository().SaveAsync(edit);

        var reloaded = await host.NewRepository().FindAsync(FinanceWorkspace, flow.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Second name", reloaded.Name);
        Assert.Equal(created, reloaded.CreatedAtUtc);
        Assert.Equal(created.AddHours(3), reloaded.UpdatedAtUtc);
    }

    /// <summary>Disabling a flow survives a restart, so the switch is not a label.</summary>
    [Fact]
    public async Task DisablingAFlowSurvivesARestart()
    {
        var flow = SampleFlow("flow-suspended", "Suspended");
        await host.NewRepository().SaveAsync(Record(FinanceWorkspace, flow));

        Assert.True(await host.NewRepository().SetEnabledAsync(FinanceWorkspace, flow.Id, false));

        var reloaded = await host.NewRepository().FindAsync(FinanceWorkspace, flow.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsEnabled);
    }

    /// <summary>Deleting removes the named flow and leaves the rest of the workspace alone.</summary>
    [Fact]
    public async Task DeletingRemovesOnlyTheNamedFlow()
    {
        await host.NewRepository().SaveAsync(Record(FinanceWorkspace, SampleFlow("flow-one", "One")));
        await host.NewRepository().SaveAsync(Record(FinanceWorkspace, SampleFlow("flow-two", "Two")));

        Assert.True(await host.NewRepository().DeleteAsync(FinanceWorkspace, "flow-one"));

        var remaining = Assert.Single(await host.NewRepository().ListAsync(FinanceWorkspace));
        Assert.Equal("flow-two", remaining.FlowId);
    }

    /// <summary>Builds a small but complete flow: an agent step, a branch and two endings.</summary>
    /// <param name="id">The flow identifier.</param>
    /// <param name="name">The flow's display name.</param>
    /// <returns>The flow.</returns>
    private static FlowDefinition SampleFlow(string id, string name)
    {
        var agent = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, "step-agent");
        agent.AgentId = "analyst";
        agent.Instruction = "Classify the request.";
        agent.OutputVariable = "verdict";

        var branch = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition, "step-branch");
        var urgent = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-urgent");
        var routine = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-routine");

        return new FlowDefinition
        {
            Id = id,
            Name = name,
            Description = "A sample flow.",
            StartNodeId = agent.Id,
            Nodes = [agent, branch, urgent, routine],
            Edges =
            [
                new FlowEdge { Id = "edge-1", FromNodeId = agent.Id, ToNodeId = branch.Id },
                new FlowEdge
                {
                    Id = "edge-2",
                    FromNodeId = branch.Id,
                    ToNodeId = urgent.Id,
                    Order = 0,
                    Condition = new FlowCondition { Kind = FlowConditionKind.Contains, Operand = "urgent" }
                },
                new FlowEdge { Id = "edge-3", FromNodeId = branch.Id, ToNodeId = routine.Id, Order = 1 }
            ]
        };
    }

    /// <summary>Wraps a flow as the row the repository stores.</summary>
    /// <param name="workspaceId">The owning workspace.</param>
    /// <param name="flow">The flow to store.</param>
    /// <returns>The record.</returns>
    private static FlowRecord Record(string workspaceId, FlowDefinition flow)
    {
        var json = FlowSerializer.ToJson(flow);
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        return new FlowRecord
        {
            FlowId = flow.Id,
            WorkspaceId = workspaceId,
            Name = flow.Name,
            Description = flow.Description,
            DefinitionJson = json,
            SchemaVersion = flow.SchemaVersion,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    /// <summary>Writes a row straight to the table, bypassing the serializer.</summary>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="workspaceId">The owning workspace.</param>
    /// <param name="name">The mirrored display name.</param>
    /// <param name="definitionJson">The stored document, however malformed.</param>
    /// <returns>A task that completes when the row is written.</returns>
    private async Task WriteRawAsync(string flowId, string workspaceId, string name, string definitionJson)
    {
        const string sql = """
            INSERT INTO "Flow" (
                "FlowId", "WorkspaceId", "Name", "Description", "DefinitionJson",
                "SchemaVersion", "IsEnabled", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES (@flowId, @workspaceId, @name, NULL, @definitionJson, 1, 1, @stamp, @stamp);
            """;

        using var connection = host.OpenConnection();
        await connection.ExecuteAsync(sql, new
        {
            flowId,
            workspaceId,
            name,
            definitionJson,
            stamp = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc).ToString("O")
        });
    }

    /// <inheritdoc />
    public void Dispose() => host.Dispose();
}
