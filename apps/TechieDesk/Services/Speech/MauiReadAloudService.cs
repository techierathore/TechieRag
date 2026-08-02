using Microsoft.Extensions.Logging;
using Microsoft.Maui.Media;

namespace TechieDesk.Services.Speech;

/// <summary>
/// The platform half of REQ-UI-036: read-aloud through the OS speech synthesiser.
/// </summary>
/// <remarks>
/// <para><b>Why MAUI Essentials and not the WebView.</b> BRD-88 was written when TechieDesk was a
/// browser app, where <c>window.speechSynthesis</c> would have been the obvious answer. It is not
/// a browser any more (BRD-128): the UI runs inside a WKWebView embedded in a Mac Catalyst app,
/// and speech in that WebView is not something this product can depend on. MAUI Essentials'
/// <see cref="TextToSpeech"/> reaches <c>AVSpeechSynthesizer</c> on Mac Catalyst and SAPI on
/// Windows — the OS voices the user has already configured, in a process we control, with no new
/// package and no permission prompt.</para>
/// <para><b>Why it lives in the head.</b> Same rule as <c>OsCredentialStore</c>: Core owns the
/// contract, the head owns the platform. TechieDesk.Core is plain net10.0 and cannot reference
/// MAUI.</para>
/// <para><b>Why stopping is a cancellation.</b> <see cref="TextToSpeech"/> exposes no Stop — the
/// only way to interrupt an utterance is to cancel the token the speak call was given. The service
/// therefore holds the live token source and cancels it, which is also what makes a second
/// read-aloud click replace the first rather than queue behind it.</para>
/// </remarks>
public sealed class MauiReadAloudService : IReadAloudService, IDisposable
{
    private readonly ILogger<MauiReadAloudService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    private CancellationTokenSource? playback;

    /// <summary>Creates the service.</summary>
    /// <param name="logger">Logger for synthesis failures.</param>
    public MauiReadAloudService(ILogger<MauiReadAloudService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool IsSupported => true;

    /// <inheritdoc/>
    public bool IsSpeaking => playback is not null;

    /// <inheritdoc/>
    public async Task<bool> CanSpeakAsync(string culture, CancellationToken cancellationToken = default)
        => await FindLocaleAsync(culture, cancellationToken).ConfigureAwait(false) is not null;

    /// <inheritdoc/>
    public async Task SpeakAsync(
        string text, string? culture = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await StopAsync();

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            playback = source;
        }
        finally
        {
            gate.Release();
        }

        try
        {
            // The locale is what makes the voice match the SCRIPT. Without it AVSpeechSynthesizer
            // uses AVSpeechSynthesisVoice.CurrentLanguageCode — the language of macOS, not of
            // TechieDesk — so a Hindi answer on an en-US Mac is handed to an English voice and the
            // Devanagari is skipped rather than spoken (REQ-UI-055). A culture with no installed
            // voice leaves options null, which is the pre-REQ-UI-055 behaviour and still audible.
            var locale = culture is null
                ? null
                : await FindLocaleAsync(culture, source.Token).ConfigureAwait(false);

            if (locale is null)
            {
                await TextToSpeech.Default.SpeakAsync(text, cancelToken: source.Token);
            }
            else
            {
                await TextToSpeech.Default.SpeakAsync(
                    text, new SpeechOptions { Locale = locale }, source.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // The user pressed stop, or started a different message. Not a failure.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Speech synthesis failed; read-aloud is unavailable for this turn");
        }
        finally
        {
            await ClearAsync(source);
            source.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        CancellationTokenSource? current;

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            current = playback;
        }
        finally
        {
            gate.Release();
        }

        if (current is null)
        {
            return;
        }

        try
        {
            await current.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The utterance finished between reading the field and cancelling it.
        }
    }

    /// <summary>
    /// Finds an installed voice whose language matches a culture, neutral culture included.
    /// </summary>
    /// <param name="culture">A culture name such as <c>hi</c> or <c>hi-IN</c>.</param>
    /// <param name="cancellationToken">Token to abandon the enumeration.</param>
    /// <returns>The matching locale, or null when this machine has no voice for that language.</returns>
    /// <remarks>
    /// Matched on the NEUTRAL language, because the platform reports a voice as <c>hi-IN</c> while
    /// the app's culture is <c>hi</c> (REQ-UI-039 ships neutral cultures), and an exact-string
    /// comparison would report "no Hindi voice" on a Mac that has one. The enumeration itself is
    /// treated as best-effort: a platform that refuses to list its voices means "speak the default",
    /// never "fail the read-aloud button".
    /// </remarks>
    private async Task<Locale?> FindLocaleAsync(string culture, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        var separator = culture.IndexOf('-', StringComparison.Ordinal);
        var neutral = separator > 0 ? culture[..separator] : culture;

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync().WaitAsync(cancellationToken);

            foreach (var locale in locales)
            {
                var language = locale.Language ?? string.Empty;
                var languageSeparator = language.IndexOf('-', StringComparison.Ordinal);
                var localeNeutral = languageSeparator > 0 ? language[..languageSeparator] : language;

                if (string.Equals(localeNeutral, neutral, StringComparison.OrdinalIgnoreCase))
                {
                    return locale;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The utterance was abandoned before the voice list came back.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enumerate the installed speech voices (REQ-UI-055)");
        }

        return null;
    }

    /// <summary>Releases the semaphore held for the live playback token source.</summary>
    public void Dispose()
    {
        playback?.Dispose();
        playback = null;
        gate.Dispose();
    }

    /// <summary>
    /// Clears the tracked token source, but only when it is still the current one.
    /// </summary>
    /// <param name="source">The token source whose utterance has ended.</param>
    /// <returns>A task that completes once the field is cleared.</returns>
    private async Task ClearAsync(CancellationTokenSource source)
    {
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            if (ReferenceEquals(playback, source))
            {
                playback = null;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
