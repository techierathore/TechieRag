using Microsoft.Extensions.Logging;

#if MACCATALYST
using AVFoundation;
using Foundation;
using Speech;
#endif

namespace TechieDesk.Services.Speech;

/// <summary>
/// The platform half of REQ-UI-035: microphone dictation through Apple's Speech framework.
/// </summary>
/// <remarks>
/// <para><b>Why not the Web Speech API.</b> BRD-87 originally said "browser speech recognition".
/// TechieDesk is no longer a browser (BRD-128) — it is a Mac Catalyst app hosting a WKWebView, and
/// WKWebView does not implement <c>SpeechRecognition</c>. There is nothing to feature-detect and
/// nothing to fall back to inside the page, which is exactly why BRD-87 was amended on 2026-07-26
/// to require the platform API. This class is that platform API.</para>
/// <para><b>What it uses.</b> <c>SFSpeechRecognizer</c> (Speech.framework) fed by an
/// <c>AVAudioEngine</c> input tap. Both ship with the OS, so this adds no NuGet dependency. On
/// macOS 13+ the recogniser can run on-device, which matters for REQ-NFR-008 — but Apple decides
/// that per language and per machine, and it is not something this class can promise.</para>
/// <para><b>Permissions.</b> Two are required and both are refusable: microphone access
/// (<c>NSMicrophoneUsageDescription</c>) and speech recognition
/// (<c>NSSpeechRecognitionUsageDescription</c>). The OS prompts once; after a refusal it never
/// prompts again, so a denial is reported to the user with the System Settings path rather than
/// retried.</para>
/// </remarks>
public sealed class CatalystDictationService : IDictationService
{
    private readonly ILogger<CatalystDictationService> logger;

#if MACCATALYST
    private readonly SemaphoreSlim gate = new(1, 1);

    private SFSpeechRecognizer? recognizer;
    private SFSpeechAudioBufferRecognitionRequest? request;
    private SFSpeechRecognitionTask? task;
    private AVAudioEngine? engine;
    private DictationCallbacks? callbacks;
#endif

    /// <summary>Creates the service.</summary>
    /// <param name="logger">Logger for permission and capture failures.</param>
    public CatalystDictationService(ILogger<CatalystDictationService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
#if MACCATALYST
    public bool IsSupported => true;
#else
    public bool IsSupported => false;
#endif

    /// <inheritdoc/>
    public string? UnsupportedReason =>
        IsSupported ? null : "Dictation needs a platform speech recognizer, which this build has none of.";

#if !MACCATALYST
    /// <inheritdoc/>
    public Task<DictationPermission> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DictationPermission.Unsupported);

    /// <inheritdoc/>
    public Task StartAsync(DictationCallbacks callbacks, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
#else
    /// <inheritdoc/>
    public async Task<DictationPermission> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        var speech = await RequestSpeechAuthorizationAsync();
        if (speech != SFSpeechRecognizerAuthorizationStatus.Authorized)
        {
            logger.LogWarning("Speech recognition authorization returned {Status}", speech);
            return DictationPermission.Denied;
        }

        var microphone = await RequestMicrophoneAsync();
        if (!microphone)
        {
            logger.LogWarning("Microphone access was refused");
            return DictationPermission.Denied;
        }

        return DictationPermission.Granted;
    }

    /// <inheritdoc/>
    public async Task StartAsync(DictationCallbacks callbacks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callbacks);

        await gate.WaitAsync(cancellationToken);
        try
        {
            TearDown();
            this.callbacks = callbacks;

            recognizer = callbacks.Language is null
                ? new SFSpeechRecognizer()
                : new SFSpeechRecognizer(NSLocale.FromLocaleIdentifier(callbacks.Language));

            if (recognizer is null || !recognizer.Available)
            {
                await RaiseFailureAsync(callbacks, "The system speech recognizer is not available right now.");
                return;
            }

            request = new SFSpeechAudioBufferRecognitionRequest { ShouldReportPartialResults = true };
            engine = new AVAudioEngine();

            var input = engine.InputNode;
            var format = input.GetBusOutputFormat(0);
            input.InstallTapOnBus(0, 4096, format, (buffer, _) => request?.Append(buffer));

            engine.Prepare();
            if (!engine.StartAndReturnError(out var startError))
            {
                var description = startError?.LocalizedDescription ?? "unknown error";
                logger.LogWarning("AVAudioEngine failed to start: {Error}", description);
                TearDown();
                await RaiseFailureAsync(callbacks, $"The microphone could not be opened ({description}).");
                return;
            }

            task = recognizer.GetRecognitionTask(request, HandleRecognition);
            logger.LogInformation("Dictation started (REQ-UI-035)");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TearDown();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Handles one recognition callback from the Speech framework.
    /// </summary>
    /// <param name="result">The result so far, or null when only an error arrived.</param>
    /// <param name="error">The error, or null on success.</param>
    /// <remarks>
    /// The transcript is CUMULATIVE — the recogniser revises earlier words as later context lands —
    /// so the whole formatted string is forwarded each time rather than a delta.
    /// </remarks>
    private void HandleRecognition(SFSpeechRecognitionResult? result, NSError? error)
    {
        var sink = callbacks;
        if (sink is null)
        {
            return;
        }

        if (error is not null)
        {
            logger.LogWarning("Speech recognition ended with: {Error}", error.LocalizedDescription);
            _ = RaiseFailureAsync(sink, "Dictation stopped: " + error.LocalizedDescription);
            return;
        }

        var transcript = result?.BestTranscription?.FormattedString;
        if (transcript is not null && sink.OnTranscriptUpdated is not null)
        {
            _ = sink.OnTranscriptUpdated(transcript);
        }
    }

    /// <summary>Asks the Speech framework for authorization.</summary>
    /// <returns>The authorization status the OS reported.</returns>
    private static Task<SFSpeechRecognizerAuthorizationStatus> RequestSpeechAuthorizationAsync()
    {
        var completion = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>();
        SFSpeechRecognizer.RequestAuthorization(status => completion.TrySetResult(status));
        return completion.Task;
    }

    /// <summary>Asks the OS for microphone access.</summary>
    /// <returns>True when access was granted.</returns>
    private static Task<bool> RequestMicrophoneAsync()
    {
        var completion = new TaskCompletionSource<bool>();
        AVAudioSession.SharedInstance().RequestRecordPermission(granted => completion.TrySetResult(granted));
        return completion.Task;
    }

    /// <summary>Reports a failure through the caller's callbacks.</summary>
    /// <param name="sink">The callbacks supplied at start.</param>
    /// <param name="message">The message to report.</param>
    /// <returns>A task that completes once the caller has handled it.</returns>
    private static Task RaiseFailureAsync(DictationCallbacks sink, string message) =>
        sink.OnFailed is null ? Task.CompletedTask : sink.OnFailed(message);

    /// <summary>
    /// Closes the microphone and releases every native object this session owns.
    /// </summary>
    /// <remarks>
    /// Order matters: the tap must be removed before the engine stops, and the request must be
    /// ended before the task is cancelled, or the recogniser keeps a half-open session alive and the
    /// next start fails with the microphone already in use.
    /// </remarks>
    private void TearDown()
    {
        if (engine is not null)
        {
            if (engine.Running)
            {
                engine.Stop();
            }

            engine.InputNode?.RemoveTapOnBus(0);
            engine.Dispose();
            engine = null;
        }

        request?.EndAudio();
        request?.Dispose();
        request = null;

        task?.Cancel();
        task?.Dispose();
        task = null;

        recognizer?.Dispose();
        recognizer = null;
        callbacks = null;
    }
#endif
}
