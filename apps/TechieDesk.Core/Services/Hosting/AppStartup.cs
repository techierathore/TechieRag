using TechieDesk.Services.Auth;
using TechieDesk.Services.Workspaces;
using TechieRag;

namespace TechieDesk.Services.Hosting;

/// <summary>
/// How far the deferred part of startup has got (REQ-FN-049).
/// </summary>
public enum AppStartupPhase
{
    /// <summary>Background initialization is still running. The window is open and usable.</summary>
    Initializing,

    /// <summary>The RAG persistence store and the default workspace are ready.</summary>
    Ready,

    /// <summary>Initialization failed. The app is open; retrieval and workspaces may be unavailable.</summary>
    Failed
}

/// <summary>
/// Observable state of the background half of startup (REQ-FN-049).
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton by the composition root so any surface — the shell, a status strip, a
/// diagnostics page — can render "still loading" or "this failed, here is why" instead of the app
/// silently pretending everything is fine. Before REQ-FN-049 this work ran on the launch thread and
/// its failures were logged and swallowed; nothing on screen could tell you the store had not come
/// up, because there was no screen until it had.
/// </para>
/// <para>
/// Thread-safe by design: it is written from a thread-pool thread and read from the UI.
/// </para>
/// </remarks>
public sealed class AppStartupState
{
    private readonly Lock gate = new();
    private AppStartupPhase phase = AppStartupPhase.Initializing;
    private string? failureMessage;

    /// <summary>Raised on the writing thread whenever the phase changes.</summary>
    /// <remarks>
    /// Consumers on a UI must marshal to their own dispatcher; this deliberately does not know what
    /// kind of host it is in.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>Gets the current phase.</summary>
    public AppStartupPhase Phase
    {
        get { lock (gate) { return phase; } }
    }

    /// <summary>Gets the failure text when <see cref="Phase"/> is
    /// <see cref="AppStartupPhase.Failed"/>, otherwise null.</summary>
    public string? FailureMessage
    {
        get { lock (gate) { return failureMessage; } }
    }

    /// <summary>Gets a value indicating whether background initialization has finished successfully.</summary>
    public bool IsReady => Phase == AppStartupPhase.Ready;

    /// <summary>Records that background initialization completed.</summary>
    public void MarkReady() => Transition(AppStartupPhase.Ready, null);

    /// <summary>Records that background initialization failed.</summary>
    /// <param name="message">What went wrong, in terms a user can act on.</param>
    public void MarkFailed(string message) => Transition(AppStartupPhase.Failed, message);

    private void Transition(AppStartupPhase next, string? message)
    {
        lock (gate)
        {
            phase = next;
            failureMessage = message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The part of startup that runs AFTER the window is on screen (REQ-FN-049).
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than in the MAUI head for two reasons. It is host-agnostic — nothing in it
/// touches a window, a dispatcher or a platform type — and the head targets
/// <c>net10.0-maccatalyst</c>/<c>net10.0-windows</c>, which a <c>net10.0</c> test project cannot
/// reference. Keeping it in Core is what makes the launch sequence testable at all, and the absence
/// of exactly that coverage is why REQ-FN-049 shipped.
/// </para>
/// <para>
/// Nothing in here may throw at its caller. The contract is that the app is ALREADY open by the time
/// this runs, so a failure is a degraded feature, never a failed launch.
/// </para>
/// </remarks>
public static class AppStartup
{
    /// <summary>
    /// Starts background initialization on the thread pool and returns immediately.
    /// </summary>
    /// <param name="services">The built root service provider.</param>
    /// <param name="state">State the UI observes while this runs.</param>
    /// <param name="logger">Optional logger for lifecycle diagnostics.</param>
    /// <returns>
    /// The running task. The launch path ignores it — it is returned so a test can await completion
    /// instead of polling.
    /// </returns>
    /// <remarks>
    /// <c>Task.Run</c> is load-bearing, not decorative. It moves the whole chain onto the thread pool,
    /// where there is no <see cref="SynchronizationContext"/> to post continuations back to, so no
    /// amount of missing <c>ConfigureAwait(false)</c> further down can route work onto the UI thread.
    /// Combined with the caller not waiting, the launch delegate returns in microseconds and the
    /// window is presented while this is still in flight.
    /// </remarks>
    public static Task BeginAsync(
        IServiceProvider services, AppStartupState state, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(state);

        return Task.Run(() => InitializeAsync(services, state, logger));
    }

    /// <summary>
    /// Initializes the TechieRag persistence store and bootstraps the default workspace
    /// (REQ-RAG-007/008/028, REQ-FN-009), recording the outcome on <paramref name="state"/>.
    /// </summary>
    /// <param name="services">The built root service provider.</param>
    /// <param name="state">State the UI observes.</param>
    /// <param name="logger">Optional logger for lifecycle diagnostics.</param>
    /// <returns>A task that completes when initialization has succeeded or failed.</returns>
    /// <remarks>
    /// Never throws. A provider outage, an unreadable saved configuration or a locked database must
    /// leave the app open and say so, which is what <see cref="AppStartupState.MarkFailed"/> is for.
    /// </remarks>
    public static async Task InitializeAsync(
        IServiceProvider services, AppStartupState state, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            using var scope = services.CreateScope();

            // ITechieRag rather than the concrete manager: the composition root registers the
            // manager as the implementation of both, and the interface is what a test can substitute.
            var rag = scope.ServiceProvider.GetRequiredService<ITechieRag>();
            await rag.InitializeAsync().ConfigureAwait(false);
            logger?.LogInformation("TechieRag persistence store initialized (threads/workspaces)");

            var workspaces = scope.ServiceProvider.GetRequiredService<IWorkspaceService>();
            var created = await workspaces
                .EnsureDefaultWorkspaceAsync(TechieDeskUser.BuiltInAdmin.UserId.ToString())
                .ConfigureAwait(false);
            if (created)
            {
                logger?.LogInformation("Default workspace bootstrapped on first run (REQ-FN-009)");
            }

            state.MarkReady();
        }
        catch (Exception exception)
        {
            logger?.LogError(exception,
                "TechieRag persistence init / default-workspace bootstrap failed; the app is open "
                + "but retrieval and workspaces may be unavailable");
            state.MarkFailed(exception.Message);
        }
    }
}
