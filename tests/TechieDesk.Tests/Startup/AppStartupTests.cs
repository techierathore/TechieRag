using Microsoft.Extensions.DependencyInjection;
using TechieDesk.Services.Hosting;
using TechieDesk.Services.Workspaces;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Startup;

/// <summary>
/// Covers the "window first, load after" startup contract introduced by REQ-FN-049.
/// </summary>
/// <remarks>
/// The MAUI head cannot be referenced from a <c>net10.0</c> test project, which is exactly why the
/// deferrable half of startup was moved into <see cref="AppStartup"/> in Core. These tests assert the
/// two properties the composition root depends on: the launch thread is never gated on
/// initialization, and initialization failure is recorded as visible state rather than thrown at a
/// caller that has no way to handle it.
/// </remarks>
public sealed class AppStartupTests
{
    /// <summary>
    /// Proves the launch thread returns from <see cref="AppStartup.BeginAsync"/> while
    /// initialization is still running — the whole point of the fix.
    /// </summary>
    /// <remarks>
    /// The fake RAG client blocks until this test releases it. If <c>BeginAsync</c> waited for
    /// initialization in any form, the calling thread would still be inside it when the assertion
    /// runs, and the gate would never be released because only this thread can release it — the
    /// same shape as the original deadlock.
    /// </remarks>
    [Fact]
    public void BeginDoesNotBlockTheLaunchThreadWhileInitializationRuns()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);

        var rag = new FakeRag(() =>
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(30));
        });

        var services = BuildProvider(rag, new FakeWorkspaces(created: true));
        var state = new AppStartupState();

        var running = AppStartup.BeginAsync(services, state);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)),
            "Background initialization never started.");
        Assert.False(running.IsCompleted,
            "BeginAsync returned only after initialization finished; the launch thread is still gated.");
        Assert.Equal(AppStartupPhase.Initializing, state.Phase);

        gate.Set();
        Assert.True(running.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(AppStartupPhase.Ready, state.Phase);
    }

    /// <summary>Proves a successful run reports itself ready.</summary>
    [Fact]
    public async Task InitializeMarksTheAppReadyOnSuccess()
    {
        var services = BuildProvider(new FakeRag(null), new FakeWorkspaces(created: true));
        var state = new AppStartupState();

        await AppStartup.InitializeAsync(services, state);

        Assert.Equal(AppStartupPhase.Ready, state.Phase);
        Assert.True(state.IsReady);
        Assert.Null(state.FailureMessage);
    }

    /// <summary>
    /// Proves a failure is recorded and surfaced rather than thrown, so the already-open window
    /// stays open and can say what went wrong.
    /// </summary>
    [Fact]
    public async Task InitializeRecordsFailureInsteadOfThrowing()
    {
        var rag = new FakeRag(() => throw new InvalidOperationException("vector store unavailable"));
        var services = BuildProvider(rag, new FakeWorkspaces(created: false));
        var state = new AppStartupState();

        await AppStartup.InitializeAsync(services, state);

        Assert.Equal(AppStartupPhase.Failed, state.Phase);
        Assert.False(state.IsReady);
        Assert.Contains("vector store unavailable", state.FailureMessage);
    }

    /// <summary>
    /// 🔴 REQ-FN-049 follow-up: the shell strip binds to <see cref="AppStartupState.Changed"/>
    /// rather than polling, so a phase change that raised no event would leave "Finishing startup"
    /// on screen for the rest of the session.
    /// </summary>
    /// <remarks>
    /// The state is read INSIDE the handler on purpose. The shell re-renders from the handler and
    /// reads <c>Phase</c> during that render, so an implementation that raised the event before
    /// publishing the new phase would paint the OLD one and never be corrected.
    /// </remarks>
    [Fact]
    public async Task PublishesEveryPhaseChangeToTheShell()
    {
        var state = new AppStartupState();
        var observed = new List<AppStartupPhase>();
        state.Changed += (_, _) => observed.Add(state.Phase);

        await AppStartup.InitializeAsync(
            BuildProvider(new FakeRag(null), new FakeWorkspaces(created: true)), state);

        Assert.Equal([AppStartupPhase.Ready], observed);
    }

    /// <summary>
    /// A failure reaches the shell with its message already readable, so the strip can name the
    /// file, port or database that would not open in the same render that reports the failure.
    /// </summary>
    [Fact]
    public async Task PublishesTheFailureMessageWithThePhaseThatCarriesIt()
    {
        var state = new AppStartupState();
        string? messageAtEvent = null;
        AppStartupPhase phaseAtEvent = AppStartupPhase.Initializing;
        state.Changed += (_, _) =>
        {
            phaseAtEvent = state.Phase;
            messageAtEvent = state.FailureMessage;
        };

        var rag = new FakeRag(() => throw new InvalidOperationException("vector store unavailable"));
        await AppStartup.InitializeAsync(
            BuildProvider(rag, new FakeWorkspaces(created: false)), state);

        Assert.Equal(AppStartupPhase.Failed, phaseAtEvent);
        Assert.Contains("vector store unavailable", messageAtEvent);
    }

    /// <summary>
    /// The shell subscribes for the life of a layout and unsubscribes on dispose, so an unsubscribed
    /// handler must genuinely stop being called — otherwise every disposed shell is kept alive by a
    /// process-lifetime singleton.
    /// </summary>
    [Fact]
    public void StopsNotifyingAHandlerThatHasUnsubscribed()
    {
        var state = new AppStartupState();
        var calls = 0;
        EventHandler handler = (_, _) => calls++;

        state.Changed += handler;
        state.MarkReady();
        state.Changed -= handler;
        state.MarkFailed("later failure");

        Assert.Equal(1, calls);
        Assert.Equal(AppStartupPhase.Failed, state.Phase);
    }

    /// <summary>Builds a minimal provider carrying only what the startup path resolves.</summary>
    /// <param name="rag">The RAG client to initialize.</param>
    /// <param name="workspaces">The workspace service to bootstrap through.</param>
    /// <returns>A root service provider.</returns>
    private static ServiceProvider BuildProvider(ITechieRag rag, IWorkspaceService workspaces)
    {
        var services = new ServiceCollection();
        services.AddSingleton(rag);
        services.AddScoped(_ => workspaces);
        return services.BuildServiceProvider();
    }

    /// <summary>A workspace service that only answers the bootstrap question.</summary>
    private sealed class FakeWorkspaces : IWorkspaceService
    {
        private readonly bool created;

        public FakeWorkspaces(bool created) => this.created = created;

        public string CurrentUserId => "test-user";

        public bool CanManageWorkspaces => true;

        public bool CanTuneRetrieval => true;

        public string SlugFor(Workspace workspace) => workspace.Name;

        public Task<bool> EnsureDefaultWorkspaceAsync(
            string userId, CancellationToken cancellationToken = default) => Task.FromResult(created);

        public Task<IReadOnlyList<Workspace>> ListForCurrentUserAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Workspace?> ResolveBySlugAsync(
            string slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Workspace> CreateWorkspaceAsync(
            string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateWorkspaceAsync(
            Workspace workspace, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RenameWorkspaceAsync(
            string workspaceId, string newName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteWorkspaceAsync(
            string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>An <see cref="ITechieRag"/> whose only real member is initialization.</summary>
    private sealed class FakeRag : ITechieRag
    {
        private readonly Action? onInitialize;

        public FakeRag(Action? onInitialize) => this.onInitialize = onInitialize;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            onInitialize?.Invoke();
            return Task.CompletedTask;
        }

        public ILlmProvider? GetLlmProvider() => null;

        public ITokenTracker GetTokenTracker() => throw new NotSupportedException();

        public IConversationMemory? GetConversationMemory() => null;

        public Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> IngestTextAsync(
            string text,
            string documentName,
            Dictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> IngestDirectoryAsync(
            string directoryPath,
            string searchPattern = "*.*",
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string query,
            int topK = 5,
            string? documentFilter = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RagResponse> AskAsync(
            string question,
            int topK = 5,
            string? systemPrompt = null,
            string? documentFilter = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> AskStreamAsync(
            string question,
            int topK = 5,
            string? systemPrompt = null,
            string? documentFilter = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RagResponse> ChatWithRagAsync(
            string userMessage,
            IReadOnlyList<ChatMessage>? conversationHistory = null,
            int topK = 5,
            string? systemPrompt = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<string> ChatWithRagStreamAsync(
            string userMessage,
            IReadOnlyList<ChatMessage>? conversationHistory = null,
            int topK = 5,
            string? systemPrompt = null,
            LlmCompletionOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
