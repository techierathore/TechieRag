using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Hosting;
using TechieDesk.Services.Workspaces;
using TechieDesk.Tests.Auth;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Workspaces;

/// <summary>
/// REQ-FN-041 (2026-07-26): the single regression this refactor could plausibly introduce.
/// <see cref="WorkspaceService.ListForCurrentUserAsync"/> used to return the FULL workspace list
/// only for a caller holding <c>Capability.ManageAllWorkspaces</c>, and otherwise intersected it
/// with the user's <c>WorkspaceAssignment</c> rows. Both the capability matrix and the assignment
/// table are gone; taking the wrong branch of that removal would have silently returned an EMPTY
/// list, hiding every workspace the owner has — including the bootstrap "Default" one, which would
/// leave the shell with no sidebar entries at all.
/// </summary>
/// <remarks>
/// The service is exercised against a REAL library <see cref="WorkspaceManager"/> over a temporary
/// SQLite store, so what is asserted is the actual list the shell renders rather than a mock's
/// idea of it. Only the embedding provider is stubbed — workspace listing never embeds anything.
/// </remarks>
public sealed class WorkspaceListingTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"techiedesk-workspaces-{Guid.NewGuid():N}");

    /// <summary>
    /// THE acceptance: every workspace in the store is listed for the local owner. Three
    /// workspaces exist and nobody was ever "assigned" to any of them; all three come back.
    /// </summary>
    [Fact]
    public async Task EveryWorkspaceIsListedForTheLocalOwner()
    {
        var (service, manager) = CreateService();
        await manager.CreateWorkspaceAsync("Default");
        await manager.CreateWorkspaceAsync("Research");
        await manager.CreateWorkspaceAsync("Invoices");

        var listed = await service.ListForCurrentUserAsync();

        Assert.Equal(3, listed.Count);
        Assert.Contains(listed, w => w.Name == "Default");
        Assert.Contains(listed, w => w.Name == "Research");
        Assert.Contains(listed, w => w.Name == "Invoices");
    }

    /// <summary>
    /// The bootstrap path (REQ-FN-009) still produces a workspace the owner can then see. This is
    /// the exact sequence a first launch runs, and the one the smoke test looks at on screen.
    /// </summary>
    [Fact]
    public async Task DefaultWorkspaceIsCreatedAndThenVisible()
    {
        var (service, _) = CreateService();

        var created = await service.EnsureDefaultWorkspaceAsync(service.CurrentUserId);
        var listed = await service.ListForCurrentUserAsync();

        Assert.True(created);
        Assert.Equal("Default", Assert.Single(listed).Name);
        Assert.False(await service.EnsureDefaultWorkspaceAsync(service.CurrentUserId));
    }

    /// <summary>
    /// A workspace created through the service is listed immediately, with no membership row to
    /// grant visibility. The old code depended on writing an assignment here; forgetting to remove
    /// that dependency correctly would have made freshly created workspaces invisible.
    /// </summary>
    [Fact]
    public async Task CreatedWorkspaceIsImmediatelyVisibleAndResolvable()
    {
        var (service, _) = CreateService();

        var created = await service.CreateWorkspaceAsync("My Notes");
        var listed = await service.ListForCurrentUserAsync();
        var resolved = await service.ResolveBySlugAsync("my-notes");

        Assert.Equal(created.WorkspaceId, Assert.Single(listed).WorkspaceId);
        Assert.NotNull(resolved);
        Assert.Equal(created.WorkspaceId, resolved!.WorkspaceId);
    }

    /// <summary>
    /// The local owner is never denied a workspace operation: there is no role that can withhold
    /// management or retrieval tuning on a single-user install.
    /// </summary>
    [Fact]
    public async Task LocalOwnerMayManageEverything()
    {
        var (service, _) = CreateService();

        var created = await service.CreateWorkspaceAsync("Temp");
        await service.RenameWorkspaceAsync(created.WorkspaceId, "Renamed");
        var afterRename = await service.ListForCurrentUserAsync();
        await service.DeleteWorkspaceAsync(created.WorkspaceId);

        Assert.True(service.CanManageWorkspaces);
        Assert.True(service.CanTuneRetrieval);
        Assert.Equal("Renamed", Assert.Single(afterRename).Name);
        Assert.Empty(await service.ListForCurrentUserAsync());
    }

    /// <summary>Removes the temporary store.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }

    private (WorkspaceService Service, WorkspaceManager Manager) CreateService()
    {
        Directory.CreateDirectory(directory);

        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new StubEmbeddingProvider())
            .UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={Path.Combine(directory, "vectors.db")}")
            .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(directory, "rag.db")}")
            .Build();

        var manager = rag.GetWorkspaceManager()
            ?? throw new InvalidOperationException("The builder produced no workspace manager.");

        var ragManager = new StubRagManager(manager, directory);
        var sessions = SessionTestHarness.Store();
        var userContext = new TechieDeskUserContext(SessionTestHarness.Circuit(sessions, null));

        return (new WorkspaceService(ragManager, userContext), manager);
    }

    /// <summary>
    /// A <see cref="TechieRagManager"/> that hands out a workspace manager built over a temporary
    /// store, so the test never stands up an embedding model or a vector index.
    /// </summary>
    private sealed class StubRagManager : TechieRagManager
    {
        private readonly WorkspaceManager manager;

        public StubRagManager(WorkspaceManager manager, string contentRoot)
            : base(
                new AppEnvironment(contentRoot),
                NullLoggerFactory.Instance,
                NullLogger<TechieRagManager>.Instance,
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(contentRoot, "keys"))),
                new ConfigurationBuilder().Build())
        {
            this.manager = manager;
        }

        public override Task<WorkspaceManager?> GetWorkspaceManagerAsync() =>
            Task.FromResult<WorkspaceManager?>(manager);
    }

    /// <summary>An embedding provider that is never called; workspace listing embeds nothing.</summary>
    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public string Name => "Stub";

        public string ModelName => "stub";

        public int Dimensions => 4;

        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            OnEmbeddingCompleted?.Invoke(this, null!);
            return Task.FromResult(new float[Dimensions]);
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[Dimensions]).ToList());
    }
}
