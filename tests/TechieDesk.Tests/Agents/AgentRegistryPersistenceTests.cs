using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Agents;
using TechieDesk.Services.Data;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-UI-045 / REQ-RAG-021 / REQ-RAG-022 — agents and the workspace skill catalogue really
/// persist. These tests run the SHIPPED DbUp migrations against a temporary database file rather
/// than hand-writing the schema, so <c>0003-AgentRegistry.sql</c> itself is exercised: a script that
/// does not apply, or whose column names drift from the Dapper parameters, fails here rather than at
/// the user's first launch.
/// </summary>
public sealed class AgentRegistryPersistenceTests : IDisposable
{
    private const string WorkspaceId = "ws-contracts";

    private static readonly DateTime Now = new(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-agents-{Guid.NewGuid():N}.db");

    /// <summary>Creates the temporary database by applying every shipped SQLite migration.</summary>
    public AgentRegistryPersistenceTests()
    {
        var exitCode = MigrationRunner.Run("Sqlite", ConnectionString);
        Assert.Equal(0, exitCode);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private string ConnectionString => $"Data Source={databasePath}";

    /// <summary>
    /// The migration really created the agent registry tables with the constrained names the coding
    /// standards require. A silently missing table would otherwise only show up as a runtime failure.
    /// </summary>
    [Fact]
    public async Task MigrationCreatesTheAgentRegistrySchema()
    {
        using var connection = new SqliteConnection(ConnectionString);
        var names = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type IN ('table','index');")).ToList();

        Assert.Contains("WorkspaceAgent", names);
        Assert.Contains("WorkspaceAgentSkill", names);
        Assert.Contains("WorkspaceSkill", names);
        Assert.Contains("IXWorkspaceAgentWorkspaceId", names);
        Assert.Contains("IXWorkspaceSkillWorkspaceId", names);
    }

    /// <summary>Re-running the migrator applies nothing new — DbUp journals what it has run.</summary>
    [Fact]
    public void ReRunningTheMigratorAppliesNothingNew()
    {
        Assert.Equal(0, MigrationRunner.Run("Sqlite", ConnectionString));

        using var connection = new SqliteConnection(ConnectionString);
        var applied = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM \"SchemaVersions\" WHERE \"ScriptName\" LIKE '%0003-AgentRegistry%';");
        Assert.Equal(1, applied);
    }

    /// <summary>
    /// A workspace that has never been configured still lists an agent: the built-in <c>@agent</c>
    /// is synthesized rather than requiring a seed row, so installs that predate this feature behave
    /// identically to new ones without a backfill.
    /// </summary>
    [Fact]
    public async Task BuiltInAgentExistsWithoutASeedRow()
    {
        var registry = NewRegistry();

        var listed = await registry.ListAsync(WorkspaceId);

        var builtIn = Assert.Single(listed);
        Assert.Equal(AgentDefinition.BuiltInHandle, builtIn.Handle);
        Assert.True(builtIn.IsBuiltIn);
        Assert.Equal(0, builtIn.WorkspaceAgentId);
        Assert.True(builtIn.UsesEveryEnabledSkill);
    }

    /// <summary>
    /// A created agent survives a completely rebuilt service graph on the same file — the create
    /// half of REQ-UI-045's create/edit/delete, proven across a modelled restart rather than in a
    /// cache.
    /// </summary>
    [Fact]
    public async Task CreatedAgentSurvivesARestart()
    {
        var identifier = await NewRegistry().SaveAsync(Analyst());

        var reloaded = await NewRegistry().ResolveAsync(WorkspaceId, "@Analyst");

        Assert.NotNull(reloaded);
        Assert.Equal(identifier, reloaded!.WorkspaceAgentId);
        Assert.Equal("Contract Analyst", reloaded.DisplayName);
        Assert.Equal("Answer only from the retrieved clauses.", reloaded.Instructions);
        Assert.Equal("llama3.1:8b", reloaded.Model);
        Assert.Equal(AgentKnowledgeScope.CallingWorkspace, reloaded.KnowledgeScope);
        Assert.True(reloaded.RestrictToPinned);
        Assert.Equal(6, reloaded.MaxToolCalls);
        Assert.Equal([SkillCatalog.ChartGenerate, SkillCatalog.RagSearch],
            reloaded.SelectedSkills.OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>
    /// Editing replaces the skill selection rather than accumulating it, so removing a skill in the
    /// editor really removes it instead of leaving a stale row that keeps granting the tool.
    /// </summary>
    [Fact]
    public async Task EditingReplacesTheSkillSelection()
    {
        var registry = NewRegistry();
        var agent = Analyst();
        await registry.SaveAsync(agent);

        agent.SelectedSkills.Clear();
        agent.SelectedSkills.Add(SkillCatalog.RagSearch);
        agent.DisplayName = "Renewal Watcher";
        await registry.SaveAsync(agent);

        var reloaded = await NewRegistry().ResolveAsync(WorkspaceId, "analyst");
        Assert.Equal("Renewal Watcher", reloaded!.DisplayName);
        Assert.Equal([SkillCatalog.RagSearch], reloaded.SelectedSkills);

        using var connection = new SqliteConnection(ConnectionString);
        var rows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM \"WorkspaceAgentSkill\";");
        Assert.Equal(1, rows);
    }

    /// <summary>
    /// REQ-NFR-013: the per-agent "ask before any skill that leaves this machine" value survives a
    /// restart AND reaches the thing that enforces it. Asserting the stored column alone would have
    /// passed while nothing read the flag, so the reload is carried through into an
    /// <see cref="EgressGate"/> — the object the running turn actually consults.
    /// </summary>
    [Fact]
    public async Task PerAgentEgressConfirmationSurvivesARestartAndReachesTheGate()
    {
        var agent = Analyst();
        agent.ConfirmEgress = false;
        await NewRegistry().SaveAsync(agent);

        var reloaded = await NewRegistry().ResolveAsync(WorkspaceId, "@Analyst");

        Assert.NotNull(reloaded);
        Assert.False(reloaded!.ConfirmEgress);
        Assert.False(new EgressGate(reloaded, confirmation: null).IsConfirmationRequired);

        reloaded.ConfirmEgress = true;
        await NewRegistry().SaveAsync(reloaded);

        var reconfirmed = await NewRegistry().ResolveAsync(WorkspaceId, "@Analyst");
        Assert.True(reconfirmed!.ConfirmEgress);
        Assert.True(new EgressGate(reconfirmed, confirmation: null).IsConfirmationRequired);
    }

    /// <summary>Deleting removes the agent and its skill rows, leaving no orphans behind.</summary>
    [Fact]
    public async Task DeletingRemovesTheAgentAndItsSkillRows()
    {
        var registry = NewRegistry();
        var identifier = await registry.SaveAsync(Analyst());

        await registry.DeleteAsync(WorkspaceId, identifier);

        Assert.Null(await NewRegistry().ResolveAsync(WorkspaceId, "analyst"));

        using var connection = new SqliteConnection(ConnectionString);
        Assert.Equal(0, await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM \"WorkspaceAgentSkill\";"));
    }

    /// <summary>
    /// The built-in agent cannot be deleted once it has been edited into a real row — the rule the
    /// acceptance names explicitly.
    /// </summary>
    [Fact]
    public async Task BuiltInAgentCannotBeDeleted()
    {
        var registry = NewRegistry();
        var builtIn = AgentDefinition.BuiltIn(WorkspaceId, Now);
        builtIn.Instructions = "Be concise.";
        var identifier = await registry.SaveAsync(builtIn);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.DeleteAsync(WorkspaceId, identifier));

        Assert.Contains("cannot be deleted", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await NewRegistry().ResolveAsync(WorkspaceId, AgentDefinition.BuiltInHandle));
    }

    /// <summary>
    /// Two agents cannot share a handle in one workspace, because <c>@analyst</c> has to name
    /// exactly one agent for REQ-RAG-021 to route deterministically.
    /// </summary>
    [Fact]
    public async Task HandlesAreUniqueWithinAWorkspace()
    {
        var registry = NewRegistry();
        await registry.SaveAsync(Analyst());

        var duplicate = Analyst();
        duplicate.DisplayName = "Another analyst";

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.SaveAsync(duplicate));

        Assert.Contains("already used", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same handle in a different workspace is fine — agents are per-workspace.</summary>
    [Fact]
    public async Task TheSameHandleIsFreeInAnotherWorkspace()
    {
        var registry = NewRegistry();
        await registry.SaveAsync(Analyst());

        var other = Analyst();
        other.WorkspaceId = "ws-hr";

        await registry.SaveAsync(other);

        Assert.NotNull(await registry.ResolveAsync("ws-hr", "analyst"));
        Assert.Single(await registry.ListAsync("ws-hr"), a => a.Handle == "analyst");
    }

    /// <summary>
    /// A workspace catalogue toggle persists, and the effective skill set is recomputed against it
    /// — so turning a skill off in the catalogue disables it for an already-saved agent without the
    /// agent being touched. This is the run-time intersection the checklist calls the hard part.
    /// </summary>
    [Fact]
    public async Task RevokingACatalogueSkillDisablesItForAnAlreadySavedAgent()
    {
        var registry = NewRegistry();
        await registry.SaveAsync(Analyst());
        var skills = NewSkillRepository();
        await skills.SetAsync(WorkspaceId, SkillCatalog.ChartGenerate, true, Now);

        var agent = await registry.ResolveAsync(WorkspaceId, "analyst");
        var before = await registry.PermittedSkillsAsync(agent!);

        await skills.SetAsync(WorkspaceId, SkillCatalog.ChartGenerate, false, Now);
        var after = await NewRegistry().PermittedSkillsAsync(agent!);

        Assert.Contains(SkillCatalog.ChartGenerate, before);
        Assert.DoesNotContain(SkillCatalog.ChartGenerate, after);
        Assert.Contains(SkillCatalog.RagSearch, after);
        Assert.Contains(SkillCatalog.ChartGenerate, agent!.SelectedSkills);
    }

    /// <summary>
    /// The catalogue is stored as one upsert per skill rather than accumulating a row per click, so
    /// toggling repeatedly cannot leave two contradictory rows deciding a permission.
    /// </summary>
    [Fact]
    public async Task TogglingASkillUpsertsASingleRow()
    {
        var skills = NewSkillRepository();

        await skills.SetAsync(WorkspaceId, SkillCatalog.WebSearch, true, Now);
        await skills.SetAsync(WorkspaceId, SkillCatalog.WebSearch, false, Now.AddMinutes(1));
        await skills.SetAsync(WorkspaceId, SkillCatalog.WebSearch, true, Now.AddMinutes(2));

        using var connection = new SqliteConnection(ConnectionString);
        var rows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM \"WorkspaceSkill\" WHERE \"SkillName\" = @skill;",
            new { skill = SkillCatalog.WebSearch });

        Assert.Equal(1, rows);
        var catalogue = await NewSkillRepository().GetCatalogueAsync(WorkspaceId);
        Assert.True(catalogue[SkillCatalog.WebSearch]);
    }

    /// <summary>
    /// A catalogue read covers every catalogue skill even for an untouched workspace, so the toggle
    /// screen never renders a partial list and the resolver never sees a hole.
    /// </summary>
    [Fact]
    public async Task UntouchedWorkspaceStillReadsAFullCatalogue()
    {
        var catalogue = await NewSkillRepository().GetCatalogueAsync("ws-never-configured");

        Assert.Equal(SkillCatalog.Skills.Count, catalogue.Count);
        Assert.True(catalogue[SkillCatalog.RagSearch]);
        Assert.False(catalogue[SkillCatalog.WebSearch]);
    }

    /// <summary>Recording a run stamps "last used" so the agent list can show it.</summary>
    [Fact]
    public async Task RunningAnAgentStampsLastUsed()
    {
        var registry = NewRegistry();
        await registry.SaveAsync(Analyst());
        var agent = await registry.ResolveAsync(WorkspaceId, "analyst");
        Assert.Null(agent!.LastUsedAt);

        await registry.MarkUsedAsync(agent);

        var reloaded = await NewRegistry().ResolveAsync(WorkspaceId, "analyst");
        Assert.NotNull(reloaded!.LastUsedAt);
    }

    /// <summary>A handle chat could never invoke is refused before it reaches storage.</summary>
    [Fact]
    public async Task AnUnusableHandleIsRefused()
    {
        var agent = Analyst();
        agent.Handle = "contract analyst";

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewRegistry().SaveAsync(agent));
    }

    private IAppDbConnectionFactory NewConnectionFactory() =>
        new AppDbConnectionFactory(Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = ConnectionString
        }));

    private IWorkspaceSkillRepository NewSkillRepository() =>
        new WorkspaceSkillRepository(NewConnectionFactory());

    /// <summary>
    /// Builds a completely fresh registry over the same file, modelling a restarted process: new
    /// repositories, new connection factory, nothing carried over in memory.
    /// </summary>
    /// <returns>A new registry.</returns>
    private IAgentRegistry NewRegistry()
    {
        var factory = NewConnectionFactory();
        return new AgentRegistry(
            new AgentRepository(factory),
            new WorkspaceSkillRepository(factory),
            new FakeTimeProvider(Now));
    }

    private static AgentDefinition Analyst()
    {
        var agent = new AgentDefinition
        {
            WorkspaceId = WorkspaceId,
            Handle = "analyst",
            DisplayName = "Contract Analyst",
            Description = "Answers with the clause it relied on",
            Instructions = "Answer only from the retrieved clauses.",
            Model = "llama3.1:8b",
            RestrictToPinned = true,
            MaxToolCalls = 6
        };
        agent.SelectedSkills.Add(SkillCatalog.RagSearch);
        agent.SelectedSkills.Add(SkillCatalog.ChartGenerate);
        return agent;
    }

    /// <summary>A fixed clock, so created/updated stamps are deterministic.</summary>
    /// <param name="now">The instant the clock reports.</param>
    private sealed class FakeTimeProvider(DateTime now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
