using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechieDesk.Resources;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// One piece of a <see cref="JobMessage"/>: either a resource CODE with the values its holes take,
/// or text that could not be coded (REQ-UI-056 / BRD-91).
/// </summary>
/// <param name="Code">
/// A key present in <c>AppStrings.resx</c>, or <see langword="null"/> when this segment is verbatim
/// text. Persisted, so it is treated as wire vocabulary: renaming one orphans stored rows.
/// </param>
/// <param name="Arguments">
/// The values the code's holes take, already formatted invariantly. Every one of them is DATA — a
/// count, an item name, a workspace id — and is carried through verbatim, never translated.
/// </param>
/// <param name="Text">
/// The sentence to print when <paramref name="Code"/> is <see langword="null"/>. This is how a
/// message the app did not author — a library exception, an OS error — travels through the same
/// pipe as a coded one without pretending to be translatable.
/// </param>
public sealed record JobMessageSegment(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("args")] IReadOnlyList<string> Arguments,
    [property: JsonPropertyName("text")] string? Text)
{
    /// <summary>Creates a segment that names a resource code and the values it formats.</summary>
    /// <param name="code">A key present in <c>AppStrings.resx</c>.</param>
    /// <param name="arguments">The values the key's holes take.</param>
    /// <returns>The coded segment.</returns>
    /// <remarks>
    /// Arguments are formatted with <see cref="CultureInfo.InvariantCulture"/> at CAPTURE time, for
    /// the reason <see cref="CronDescriber"/> records: a count stored as "2" reads the same in every
    /// culture, whereas a number formatted at render time would make a stored row's meaning depend on
    /// who is looking at it.
    /// </remarks>
    public static JobMessageSegment Coded(string code, params object?[] arguments) =>
        new(code, Format(arguments), null);

    /// <summary>Creates a segment carrying text this app did not author and cannot translate.</summary>
    /// <param name="text">The sentence to print as it stands.</param>
    /// <returns>The verbatim segment.</returns>
    public static JobMessageSegment Verbatim(string text) => new(null, [], text);

    /// <summary>Renders this segment in the reader's language.</summary>
    /// <param name="localize">Resolves a resource code into the reader's language.</param>
    /// <returns>The sentence to show.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localize"/> is <see langword="null"/>.</exception>
    public string Resolve(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        if (Code is null)
        {
            return Text ?? string.Empty;
        }

        return Arguments.Count == 0
            ? localize(Code)
            : localize(Code, [.. Arguments.Cast<object?>()]);
    }

    private static IReadOnlyList<string> Format(IReadOnlyList<object?> arguments)
    {
        if (arguments.Count == 0)
        {
            return [];
        }

        var formatted = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            formatted[index] = arguments[index] switch
            {
                null => string.Empty,
                string text => text,
                IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString() ?? string.Empty
            };
        }

        return formatted;
    }
}

/// <summary>
/// A sentence a job produced, stored as CODES AND ARGUMENTS rather than as finished English, so the
/// run history renders in the reader's language whenever it is read (REQ-UI-056 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>Why not a bare resource key.</b> REQ-UI-051's rule is "a service returns a KEY, never a
/// sentence", and for a catalogue table that is enough. It is not enough here, because the sentences
/// this type replaces are PARAMETERIZED and PERSISTED: the rows already on disk read
/// <c>"2 ingested of 2 listed"</c> and <c>"Added to workspace 09ed1034-…"</c>. A key alone cannot
/// reproduce the numbers, so the stored unit has to be the key <i>plus</i> the values its holes take.
/// </para>
/// <para><b>Why a LIST of segments and not one code.</b> Three of the sentences here are genuinely
/// composed — "3 processed · 1 failed · 2 skipped", a connector detail with an "and the budget
/// stopped us" tail, an item list that says it was capped. Enumerating every combination would mean
/// eight codes for the run detail alone, each of which a translator would have to be shown
/// separately. The separator is <c>·</c>, which is punctuation both shipped languages write the same
/// way — the identical judgement <see cref="CronDescriber.JoinReadable"/> already makes about the
/// comma — so joining resolved segments is safe in a way that gluing sentence FRAGMENTS is not.
/// Nothing here ever joins a subject to its verb; each segment is a whole clause.</para>
/// <para><b>Why English is still written to the old column.</b> <see cref="ToInvariantString"/>
/// renders the message in English and the runner stores it beside the codes, in the pre-existing
/// <c>Detail</c> / <c>FailureReason</c> / <c>Reason</c> column. That is deliberate belt-and-braces:
/// the scheduler helper host LOGS the detail line, a support engineer reads these rows in a database
/// browser, and a code retired in a future release would otherwise leave a blank row. It also means
/// <see cref="Render"/>'s fallback is a live path exercised on every install rather than a branch
/// that only legacy data ever takes.</para>
/// <para><b>The legacy contract is permanent, not transitional.</b> Rows written before this type
/// existed have no codes and never will. <see cref="Render"/> prints their stored text verbatim, and
/// that branch is not to be removed — deleting it would blank out a user's run history, which is the
/// one outcome the persisted-English policy exists to prevent.</para>
/// </remarks>
public sealed record JobMessage
{
    /// <summary>
    /// The separator between segments. Punctuation, not vocabulary — see the type remarks.
    /// </summary>
    private const string SegmentSeparator = " · ";

    private static readonly JsonSerializerOptions StorageOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly ResourceManager NeutralResources =
        new("TechieDesk.Resources.AppStrings", typeof(AppStrings).Assembly);

    /// <summary>Initializes a message from its segments.</summary>
    /// <param name="segments">The segments, in reading order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    public JobMessage(IReadOnlyList<JobMessageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = segments;
    }

    /// <summary>Gets the segments, in reading order.</summary>
    public IReadOnlyList<JobMessageSegment> Segments { get; }

    /// <summary>
    /// Gets a localizer that resolves against the NEUTRAL resource set, which is English.
    /// </summary>
    /// <remarks>
    /// <para>Two callers, both of which need English on purpose rather than by omission: the audit
    /// text written beside the codes (see the type remarks), and the action list in
    /// <c>ScheduleInterpreter</c>'s prompt, which is read by a model and not by a person. Anywhere a
    /// PERSON reads the result, the reader's own <see cref="LocalizeText"/> is used instead.</para>
    /// <para>A missing code resolves to the code itself, matching what
    /// <c>IStringLocalizer</c> does, so a typo shows up as a visible token in a test rather than as
    /// an exception on a background timer.</para>
    /// </remarks>
    public static LocalizeText Neutral { get; } = (code, arguments) =>
    {
        var format = NeutralResources.GetString(code, CultureInfo.InvariantCulture) ?? code;
        return arguments.Length == 0
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, arguments);
    };

    /// <summary>Creates a message from one resource code and the values it formats.</summary>
    /// <param name="code">A key present in <c>AppStrings.resx</c>.</param>
    /// <param name="arguments">The values the key's holes take.</param>
    /// <returns>The message.</returns>
    public static JobMessage Of(string code, params object?[] arguments) =>
        new([JobMessageSegment.Coded(code, arguments)]);

    /// <summary>Creates a message carrying text this app did not author.</summary>
    /// <param name="text">The sentence to print as it stands.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// For a library exception, an OS error or anything else whose words are not ours to choose.
    /// A message made only of verbatim segments stores NO codes — <see cref="ToStorage"/> returns
    /// <see langword="null"/> — because the text column already carries it in full.
    /// </remarks>
    public static JobMessage Text(string text) => new([JobMessageSegment.Verbatim(text)]);

    /// <summary>Appends a coded segment, returning a new message.</summary>
    /// <param name="code">A key present in <c>AppStrings.resx</c>.</param>
    /// <param name="arguments">The values the key's holes take.</param>
    /// <returns>The extended message.</returns>
    public JobMessage Then(string code, params object?[] arguments) =>
        new([.. Segments, JobMessageSegment.Coded(code, arguments)]);

    /// <summary>Renders the message in the reader's language.</summary>
    /// <param name="localize">Resolves a resource code into the reader's language.</param>
    /// <returns>The sentence to show.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localize"/> is <see langword="null"/>.</exception>
    public string Resolve(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);
        return string.Join(SegmentSeparator, Segments.Select(segment => segment.Resolve(localize)));
    }

    /// <summary>Renders the message in English, for the audit column and the model's prompt.</summary>
    /// <returns>The English sentence.</returns>
    public string ToInvariantString() => Resolve(Neutral);

    /// <summary>
    /// Projects the message onto the string stored in the <c>…Json</c> column beside the text.
    /// </summary>
    /// <returns>
    /// The JSON form, or <see langword="null"/> when the message carries no code at all and the text
    /// column therefore already says everything there is to say.
    /// </returns>
    public string? ToStorage() =>
        Segments.Any(segment => segment.Code is not null)
            ? JsonSerializer.Serialize(Segments, StorageOptions)
            : null;

    /// <summary>Reads a message back from its stored JSON form.</summary>
    /// <param name="json">The stored value, or <see langword="null"/> for a row written without one.</param>
    /// <returns>The message, or <see langword="null"/> when there is nothing readable to render.</returns>
    /// <remarks>
    /// Never throws. A column hand-edited, truncated or written by a newer build must fall back to
    /// the stored English beside it, not take down the run-history dialog that is trying to paint it.
    /// </remarks>
    public static JobMessage? FromStorage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var segments = JsonSerializer.Deserialize<List<JobMessageSegment>>(json, StorageOptions);
            return segments is { Count: > 0 } ? new JobMessage(segments) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Renders a stored run-history value: the codes when the row has them, otherwise the stored
    /// text exactly as it was written.
    /// </summary>
    /// <param name="storedText">The pre-existing text column — <c>Detail</c>, <c>FailureReason</c>, <c>Reason</c>.</param>
    /// <param name="storedJson">The companion <c>…Json</c> column, or <see langword="null"/> for a legacy row.</param>
    /// <param name="localize">Resolves a resource code into the reader's language.</param>
    /// <returns>The sentence to show, or <see langword="null"/> when the row recorded nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localize"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>THIS METHOD IS THE PERSISTED-ENGLISH POLICY.</b> Every screen that paints a stored run
    /// value calls it and nothing else. The <see langword="null"/>-code branch is permanent: it is
    /// what stops an install with two years of history going blank the day the app learned to
    /// translate.
    /// </remarks>
    public static string? Render(string? storedText, string? storedJson, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);
        return FromStorage(storedJson)?.Resolve(localize) ?? storedText;
    }
}
