using System.Text;
using System.Text.RegularExpressions;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Speech;

/// <summary>
/// Prepares an assistant response for read-aloud (REQ-UI-036, BRD-88).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> An answer is written to be READ, not heard. Handed to a synthesiser raw, a
/// fenced code block becomes a minute of punctuation names, a markdown link reads its own URL out
/// loud, and a long answer holds the speaker for several minutes with no way to skip. This turns
/// the same text into something worth listening to.</para>
/// <para><b>What it does NOT do:</b> it does not translate, summarise or reorder. Every word spoken
/// is a word that was on screen — the listener and the reader must get the same answer.</para>
/// <para>
/// <b>REQ-UI-055 / BRD-91 — why this one is not a plain substitution.</b> The two strings below are
/// not read by eyes, they are SPOKEN by a synthesiser, and a synthesiser will not read a script its
/// voice has no phonemes for. <c>MauiReadAloudService</c> reaches <c>AVSpeechSynthesizer</c> through
/// MAUI Essentials, and a <c>SpeakAsync</c> call with no <c>SpeechOptions.Locale</c> picks
/// <c>AVSpeechSynthesisVoice.CurrentLanguageCode</c> — the language of the OPERATING SYSTEM, not the
/// language TechieDesk is running in, which is a row in the app database (REQ-UI-039). A Hindi
/// TechieDesk on an <c>en-US</c> macOS therefore hands Devanagari to an English voice, which skips
/// it rather than speaking it. Translating these two strings and stopping there would have replaced
/// a sentence the listener hears with silence.
/// </para>
/// <para>
/// So the language is CHOSEN rather than assumed: <see cref="ForSpeech(string?, LocalizeText?)"/>
/// takes a resolver, and the caller passes one only once
/// <see cref="IReadAloudService.CanSpeakAsync"/> has confirmed the platform actually owns a voice
/// for that culture. When it does not, <see cref="ForSpeech(string?)"/> speaks the invariant English
/// — audible, and honest about it. macOS ships Hindi voices (Lekha) as an optional download, so
/// whether Hindi is speakable is a per-machine fact that can only be answered at run time.
/// </para>
/// </remarks>
public static class SpeechText
{
    /// <summary>
    /// The stand-in spoken in place of a fenced code block when the synthesiser has no voice for the
    /// reader's language.
    /// </summary>
    public const string CodeBlockPlaceholder = "Code block omitted.";

    /// <summary>Resource key for the same stand-in, for a language the synthesiser can speak.</summary>
    public const string CodeBlockPlaceholderKey = "SpeechCodeBlockOmitted";

    /// <summary>The longest utterance produced, in characters.</summary>
    /// <remarks>
    /// Roughly four minutes at a normal speaking rate. Past that the control is a nuisance rather
    /// than a feature, so the tail is dropped and the listener is told it was.
    /// </remarks>
    public const int MaxSpokenCharacters = 2400;

    /// <summary>Appended when the text was cut short so silence never reads as "that was all".</summary>
    public const string TruncationNotice = " The rest of the answer is on screen.";

    /// <summary>Resource key for the same notice, for a language the synthesiser can speak.</summary>
    public const string TruncationNoticeKey = "SpeechTruncationNotice";

    private static readonly Regex FencedCodeBlock =
        new(@"```[\s\S]*?(```|$)", RegexOptions.Compiled);

    private static readonly Regex MarkdownLink =
        new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    private static readonly Regex HeadingMarker =
        new(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ListMarker =
        new(@"^\s{0,6}([*+-]|\d{1,3}[.)])\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex BlockquoteMarker =
        new(@"^\s{0,3}>+\s*", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Whitespace =
        new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Converts an assistant response into text worth speaking.
    /// </summary>
    /// <param name="markdown">The response as it is rendered on screen.</param>
    /// <returns>The spoken form; an empty string when there is nothing to say.</returns>
    /// <remarks>
    /// The interjections are the invariant English. Use this overload when the synthesiser has no
    /// voice for the reader's language — English that is audible beats Devanagari that is skipped.
    /// </remarks>
    public static string ForSpeech(string? markdown) => ForSpeech(markdown, null);

    /// <summary>
    /// Converts an assistant response into text worth speaking, in a named language.
    /// </summary>
    /// <param name="markdown">The response as it is rendered on screen.</param>
    /// <param name="localize">
    /// Resolves the two spoken interjections, or null to speak the invariant English ones. Pass a
    /// resolver ONLY when <see cref="IReadAloudService.CanSpeakAsync"/> has confirmed the platform
    /// holds a voice for that language; see the remarks on <see cref="SpeechText"/> for why.
    /// </param>
    /// <returns>The spoken form; an empty string when there is nothing to say.</returns>
    public static string ForSpeech(string? markdown, LocalizeText? localize)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var placeholder = localize is null ? CodeBlockPlaceholder : localize(CodeBlockPlaceholderKey);
        var notice = localize is null ? TruncationNotice : localize(TruncationNoticeKey);

        var text = FencedCodeBlock.Replace(markdown, $" {placeholder} ");
        text = MarkdownLink.Replace(text, "$1");
        text = HeadingMarker.Replace(text, string.Empty);
        text = ListMarker.Replace(text, string.Empty);
        text = BlockquoteMarker.Replace(text, string.Empty);
        text = StripEmphasis(text);
        text = Whitespace.Replace(text, " ").Trim();

        return Truncate(text, notice);
    }

    /// <summary>
    /// Removes the inline markers that would otherwise be spoken as punctuation.
    /// </summary>
    /// <param name="text">The text to clean.</param>
    /// <returns>The text without emphasis or inline-code markers.</returns>
    /// <remarks>
    /// <para>Done character-by-character rather than by regex because the markers are unbalanced
    /// often enough — a lone asterisk, an unclosed backtick — that a pair-matching pattern would
    /// leave half of them behind.</para>
    /// <para>An underscore becomes a SPACE rather than nothing, so <c>some_identifier</c> is spoken
    /// as two words instead of one unpronounceable one. <c>#</c> is deliberately left alone: heading
    /// markers are already gone by this point, and stripping the character would turn "C#" into "C".
    /// </para>
    /// </remarks>
    private static string StripEmphasis(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (character is '*' or '`' or '~')
            {
                continue;
            }

            builder.Append(character == '_' ? ' ' : character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Caps the utterance, preferring to end on a sentence boundary.
    /// </summary>
    /// <param name="text">The cleaned text.</param>
    /// <param name="notice">The notice to speak after the cut, already in the spoken language.</param>
    /// <returns>The text, cut short with a spoken notice when it was over the cap.</returns>
    private static string Truncate(string text, string notice)
    {
        if (text.Length <= MaxSpokenCharacters)
        {
            return text;
        }

        var window = text[..MaxSpokenCharacters];
        var lastSentence = window.LastIndexOfAny(['.', '!', '?']);

        // Only honour a sentence break in the last quarter of the window: an early one would throw
        // away text the listener could have had.
        var cut = lastSentence > MaxSpokenCharacters * 3 / 4 ? lastSentence + 1 : MaxSpokenCharacters;

        return window[..cut].TrimEnd() + notice;
    }
}
