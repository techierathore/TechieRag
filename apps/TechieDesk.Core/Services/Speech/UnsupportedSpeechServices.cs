namespace TechieDesk.Services.Speech;

/// <summary>
/// The dictation service used by a host with no microphone platform (REQ-UI-035).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the composer's mic button resolvable in every host — the test
/// project included — without pretending it can hear anything. It reports its own unavailability
/// rather than throwing, because a missing microphone must disable one control, not break the
/// chat page.</para>
/// </remarks>
public sealed class UnsupportedDictationService : IDictationService
{
    /// <summary>The reason shown when no platform recognizer is present.</summary>
    public const string Reason = "Dictation is not available in this build.";

    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public string? UnsupportedReason => Reason;

    /// <inheritdoc/>
    public Task<DictationPermission> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DictationPermission.Unsupported);

    /// <inheritdoc/>
    public Task StartAsync(DictationCallbacks callbacks, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// The read-aloud service used by a host with no speech synthesiser (REQ-UI-036).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Same contract as <see cref="UnsupportedDictationService"/> — the control
/// resolves and disables itself instead of failing the page.</para>
/// </remarks>
public sealed class UnsupportedReadAloudService : IReadAloudService
{
    /// <inheritdoc/>
    public bool IsSupported => false;

    /// <inheritdoc/>
    public bool IsSpeaking => false;

    /// <inheritdoc/>
    /// <remarks>
    /// A host with no synthesiser can speak NO language, English included, so this is false for
    /// every culture rather than true for the default one. Answering otherwise would send a caller
    /// down the "the platform can say this" branch on a build that says nothing at all.
    /// </remarks>
    public Task<bool> CanSpeakAsync(string culture, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <inheritdoc/>
    public Task SpeakAsync(string text, string? culture = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync() => Task.CompletedTask;
}
