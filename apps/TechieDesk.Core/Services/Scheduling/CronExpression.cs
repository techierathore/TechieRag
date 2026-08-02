using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Why a cron expression could not be parsed, as a resource KEY plus the offending values
/// (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="Key">A key present in <c>AppStrings.resx</c>.</param>
/// <param name="Arguments">
/// The values the key's holes take. Every one of them is DATA — a cron field, a token, a bound — and
/// is carried through verbatim, never translated.
/// </param>
/// <remarks>
/// <para>
/// The parse error is user-facing: a natural-language draft that produced an unparseable expression
/// has to say <i>what</i> was wrong with it (REQ-UI-046), because the user never wrote the cron and
/// cannot be expected to debug it. It is also produced by a static parser that has no localizer and
/// must not have one — parsing is the same in every language.
/// </para>
/// <para>
/// So the parser returns a key and the surface resolves it, which is the REQ-UI-051 rule applied to a
/// value rather than to a table. <see cref="Resolve"/> is the one place the two meet.
/// </para>
/// </remarks>
public sealed record CronParseError(string Key, IReadOnlyList<object?> Arguments)
{
    /// <summary>Creates an error from a key and its arguments.</summary>
    /// <param name="key">A key present in <c>AppStrings.resx</c>.</param>
    /// <param name="arguments">The values the key's holes take.</param>
    /// <returns>The error.</returns>
    public static CronParseError From(string key, params object?[] arguments) => new(key, arguments);

    /// <summary>Resolves this error into the reader's language.</summary>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The sentence to show.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localize"/> is <see langword="null"/>.</exception>
    public string Resolve(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);
        return localize(Key, [.. Arguments]);
    }

    /// <summary>Renders the error for a developer — an exception message or a log line.</summary>
    /// <returns>The key and its arguments. Never shown to a user.</returns>
    public override string ToString() =>
        Arguments.Count == 0 ? Key : $"{Key}[{string.Join('|', Arguments)}]";
}

/// <summary>
/// A parsed five-field cron expression (minute, hour, day-of-month, month, day-of-week) able to
/// compute its next occurrence as an absolute UTC instant in a named time zone (REQ-FN-028, BRD-93).
/// </summary>
/// <remarks>
/// <para><b>Why hand-written and not a job framework.</b> BRD-139 rules out a job server, a job
/// database and a third-party job framework. What is needed here is not scheduling infrastructure —
/// it is the arithmetic "given a wall-clock rule and a time zone, when does this next fire", which is
/// a pure function and is unit-testable exactly because it takes the instant as an argument rather
/// than reading the clock.</para>
/// <para><b>Cron is stored, never shown.</b> BRD-140 forbids a cron expression appearing in any grid,
/// list or notification; <see cref="CronDescriber"/> turns this object back into the sentence the
/// user reads. Cron survives only as the machine-checkable form behind the <i>Advanced</i>
/// disclosure.</para>
/// <para><b>Day-of-month and day-of-week are OR-ed, per Vixie cron.</b> When both fields are
/// restricted a day matches if <i>either</i> matches. This is surprising the first time it is met and
/// is the single most common source of a schedule firing on days its author did not intend, so it is
/// implemented explicitly rather than left to fall out of the loop.</para>
/// </remarks>
public sealed class CronExpression
{
    /// <summary>Longest horizon searched for a next occurrence before giving up.</summary>
    /// <remarks>
    /// Eight years covers the worst legitimate case, <c>0 0 29 2 *</c>, across a skipped century leap
    /// year. The search steps whole months and days, not minutes, so the bound costs nothing.
    /// </remarks>
    private const int SearchYears = 8;

    private static readonly string[] MonthNames =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static readonly string[] DayNames =
        ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private readonly bool[] minutes;
    private readonly bool[] hours;
    private readonly bool[] daysOfMonth;
    private readonly bool[] months;
    private readonly bool[] daysOfWeek;

    private CronExpression(
        string expression,
        bool[] minutes,
        bool[] hours,
        bool[] daysOfMonth,
        bool[] months,
        bool[] daysOfWeek,
        bool isDayOfMonthRestricted,
        bool isDayOfWeekRestricted)
    {
        Expression = expression;
        this.minutes = minutes;
        this.hours = hours;
        this.daysOfMonth = daysOfMonth;
        this.months = months;
        this.daysOfWeek = daysOfWeek;
        IsDayOfMonthRestricted = isDayOfMonthRestricted;
        IsDayOfWeekRestricted = isDayOfWeekRestricted;
    }

    /// <summary>Gets the normalized expression text as parsed.</summary>
    public string Expression { get; }

    /// <summary>Gets a value indicating whether the day-of-month field is anything other than <c>*</c>.</summary>
    public bool IsDayOfMonthRestricted { get; }

    /// <summary>Gets a value indicating whether the day-of-week field is anything other than <c>*</c>.</summary>
    public bool IsDayOfWeekRestricted { get; }

    /// <summary>Gets the minutes this expression fires on, ascending.</summary>
    public IReadOnlyList<int> Minutes => Selected(minutes, 0);

    /// <summary>Gets the hours this expression fires on, ascending.</summary>
    public IReadOnlyList<int> Hours => Selected(hours, 0);

    /// <summary>Gets the days of the month this expression fires on, ascending.</summary>
    public IReadOnlyList<int> DaysOfMonth => Selected(daysOfMonth, 0);

    /// <summary>Gets the months this expression fires in, ascending (1 = January).</summary>
    public IReadOnlyList<int> Months => Selected(months, 0);

    /// <summary>Gets the days of the week this expression fires on, ascending (0 = Sunday).</summary>
    public IReadOnlyList<int> DaysOfWeek => Selected(daysOfWeek, 0);

    /// <summary>Parses a five-field cron expression.</summary>
    /// <param name="expression">The expression, for example <c>0 7 * * 1-5</c>.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="FormatException">The expression is not a valid five-field cron expression.</exception>
    /// <remarks>
    /// The thrown message is the error's KEY, not a sentence: an exception here is read by a developer,
    /// and the user-facing wording lives in the resources (REQ-UI-055). Callers that show a person why
    /// their schedule was refused use <see cref="TryParse"/> and <see cref="CronParseError.Resolve"/>.
    /// </remarks>
    public static CronExpression Parse(string expression) =>
        TryParse(expression, out var parsed, out var error)
            ? parsed
            : throw new FormatException(error!.ToString());

    /// <summary>Attempts to parse a five-field cron expression.</summary>
    /// <param name="expression">The expression, for example <c>*/30 * * * *</c>.</param>
    /// <param name="parsed">The parsed expression when this method returns <see langword="true"/>.</param>
    /// <param name="error">Why it failed, as a resource key, when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the expression parsed.</returns>
    /// <remarks>
    /// The error is user-facing. A natural-language draft that produced an unparseable
    /// expression has to say <i>what</i> was wrong with it (REQ-UI-046), because the user never wrote
    /// the cron and cannot be expected to debug it — so it is returned as a
    /// <see cref="CronParseError"/> the surface resolves into the reader's language (REQ-UI-055).
    /// </remarks>
    public static bool TryParse(
        string? expression, [NotNullWhen(true)] out CronExpression? parsed, out CronParseError? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = CronParseError.From("CronErrorExpressionRequired");
            return false;
        }

        var fields = expression.Trim().Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            error = CronParseError.From("CronErrorFieldCount", fields.Length);
            return false;
        }

        if (!TryParseField(fields[0], 0, 59, null, out var minuteSet, out error) ||
            !TryParseField(fields[1], 0, 23, null, out var hourSet, out error) ||
            !TryParseField(fields[2], 1, 31, null, out var dayOfMonthSet, out error) ||
            !TryParseField(fields[3], 1, 12, MonthNames, out var monthSet, out error) ||
            !TryParseField(fields[4], 0, 7, DayNames, out var dayOfWeekSet, out error))
        {
            return false;
        }

        // Cron allows both 0 and 7 for Sunday. Collapsing 7 onto 0 here means every later comparison
        // works on a single representation and cannot disagree with itself.
        if (dayOfWeekSet![7])
        {
            dayOfWeekSet[0] = true;
            dayOfWeekSet[7] = false;
        }

        parsed = new CronExpression(
            string.Join(' ', fields),
            minuteSet!,
            hourSet!,
            dayOfMonthSet!,
            monthSet!,
            dayOfWeekSet,
            fields[2] != "*",
            fields[4] != "*");
        return true;
    }

    /// <summary>Determines whether a wall-clock local time matches this expression.</summary>
    /// <param name="localTime">A local wall-clock time; seconds and below are ignored.</param>
    /// <returns><see langword="true"/> when the expression fires at that wall-clock minute.</returns>
    public bool Matches(DateTime localTime) =>
        months[localTime.Month]
        && DayMatches(localTime)
        && hours[localTime.Hour]
        && minutes[localTime.Minute];

    /// <summary>
    /// Computes the first occurrence strictly after <paramref name="afterUtc"/>, as a UTC instant.
    /// </summary>
    /// <param name="afterUtc">The instant to search from, exclusive. Treated as UTC.</param>
    /// <param name="timeZone">The time zone whose wall clock the expression is written against.</param>
    /// <returns>The next occurrence in UTC, or <see langword="null"/> when none falls inside the search horizon.</returns>
    /// <remarks>
    /// <para><b>Daylight saving is the whole reason this returns UTC.</b> A schedule is written in
    /// wall-clock terms ("07:00") and stored that way, so the absolute instant it means moves twice a
    /// year. Two transitions have to be decided rather than stumbled into:</para>
    /// <para><b>Spring forward — the wall time does not exist.</b> A 02:30 daily job on the morning
    /// the clock jumps 02:00 to 03:00 has no 02:30 to fire at. It fires at the first wall-clock minute
    /// that does exist (03:00), rather than being silently skipped for the day. A skipped run is a
    /// missed sync the user never asked to miss.</para>
    /// <para><b>Fall back — the wall time happens twice.</b> The 01:30 job fires on the FIRST pass
    /// (the still-daylight offset) and not on the second. Firing twice would double-ingest, and
    /// double-ingesting is worse than firing an hour early.</para>
    /// </remarks>
    public DateTime? GetNextOccurrenceUtc(DateTime afterUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var fromUtc = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, timeZone);
        var candidate = new DateTime(
            local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified)
            .AddMinutes(1);
        var limit = candidate.AddYears(SearchYears);

        while (candidate < limit)
        {
            if (!months[candidate.Month])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                    .AddMonths(1);
                continue;
            }

            if (!DayMatches(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!hours[candidate.Hour])
            {
                candidate = candidate.Date.AddHours(candidate.Hour + 1);
                continue;
            }

            if (!minutes[candidate.Minute])
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            if (TryResolveInstant(candidate, timeZone, out var occurrence) && occurrence > fromUtc)
            {
                return occurrence;
            }

            candidate = candidate.AddMinutes(1);
        }

        return null;
    }

    /// <summary>
    /// Enumerates the next occurrences after an instant, for the confirmation preview
    /// ("next three runs", REQ-UI-046).
    /// </summary>
    /// <param name="afterUtc">The instant to search from, exclusive.</param>
    /// <param name="timeZone">The time zone whose wall clock the expression is written against.</param>
    /// <param name="count">How many occurrences to return.</param>
    /// <returns>Up to <paramref name="count"/> occurrences in UTC, ascending.</returns>
    public IReadOnlyList<DateTime> GetNextOccurrencesUtc(DateTime afterUtc, TimeZoneInfo timeZone, int count)
    {
        var occurrences = new List<DateTime>();
        var cursor = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        for (var index = 0; index < count; index++)
        {
            var next = GetNextOccurrenceUtc(cursor, timeZone);
            if (next is null)
            {
                break;
            }

            occurrences.Add(next.Value);
            cursor = next.Value;
        }

        return occurrences;
    }

    private bool DayMatches(DateTime localTime)
    {
        var dayOfMonthMatches = daysOfMonth[localTime.Day];
        var dayOfWeekMatches = daysOfWeek[(int)localTime.DayOfWeek];

        // Vixie-cron semantics: restricting BOTH fields means "either", not "both".
        if (IsDayOfMonthRestricted && IsDayOfWeekRestricted)
        {
            return dayOfMonthMatches || dayOfWeekMatches;
        }

        return dayOfMonthMatches && dayOfWeekMatches;
    }

    private static bool TryResolveInstant(DateTime localCandidate, TimeZoneInfo timeZone, out DateTime instantUtc)
    {
        instantUtc = default;

        if (timeZone.IsInvalidTime(localCandidate))
        {
            // Spring forward: walk to the first wall-clock minute that exists again. The gap is one
            // hour on every zone that has one, so this terminates well inside the bound.
            var probe = localCandidate;
            for (var step = 0; step < 24 * 60 && timeZone.IsInvalidTime(probe); step++)
            {
                probe = probe.AddMinutes(1);
            }

            if (timeZone.IsInvalidTime(probe))
            {
                return false;
            }

            localCandidate = probe;
        }

        if (timeZone.IsAmbiguousTime(localCandidate))
        {
            // Fall back: two instants share this wall time. The larger offset is the earlier instant,
            // which is the first pass — fire then, and never on the repeat.
            var offsets = timeZone.GetAmbiguousTimeOffsets(localCandidate);
            var earliest = offsets[0];
            foreach (var offset in offsets)
            {
                if (offset > earliest)
                {
                    earliest = offset;
                }
            }

            instantUtc = DateTime.SpecifyKind(localCandidate - earliest, DateTimeKind.Utc);
            return true;
        }

        instantUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localCandidate, DateTimeKind.Unspecified), timeZone);
        return true;
    }

    private static IReadOnlyList<int> Selected(bool[] set, int offset)
    {
        var values = new List<int>();
        for (var index = 0; index < set.Length; index++)
        {
            if (set[index])
            {
                values.Add(index + offset);
            }
        }

        return values;
    }

    private static bool TryParseField(
        string field, int min, int max, string[]? names, out bool[]? set, out CronParseError? error)
    {
        set = new bool[max + 1];
        error = null;

        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryApplyPart(part, min, max, names, set, out error))
            {
                set = null;
                return false;
            }
        }

        for (var index = min; index <= max; index++)
        {
            if (set[index])
            {
                return true;
            }
        }

        set = null;
        error = CronParseError.From("CronErrorSelectsNothing", field);
        return false;
    }

    private static bool TryApplyPart(
        string part, int min, int max, string[]? names, bool[] set, out CronParseError? error)
    {
        error = null;
        var step = 1;
        var slash = part.IndexOf('/');
        var range = part;

        if (slash >= 0)
        {
            range = part[..slash];
            if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
            {
                error = CronParseError.From("CronErrorInvalidStep", part);
                return false;
            }
        }

        int from;
        int to;
        if (range == "*")
        {
            from = min;
            to = max;
        }
        else
        {
            var dash = range.IndexOf('-');
            if (dash > 0)
            {
                if (!TryParseValue(range[..dash], min, max, names, out from, out error) ||
                    !TryParseValue(range[(dash + 1)..], min, max, names, out to, out error))
                {
                    return false;
                }
            }
            else
            {
                if (!TryParseValue(range, min, max, names, out from, out error))
                {
                    return false;
                }

                // A bare value with a step means "from here to the end", the same as */n does.
                to = slash >= 0 ? max : from;
            }
        }

        if (from > to)
        {
            // A wrapping range such as FRI-MON. Splitting it is the only reading that does not
            // silently select nothing.
            ApplyRange(set, from, max, step);
            ApplyRange(set, min, to, step);
            return true;
        }

        ApplyRange(set, from, to, step);
        return true;
    }

    private static void ApplyRange(bool[] set, int from, int to, int step)
    {
        for (var value = from; value <= to; value += step)
        {
            set[value] = true;
        }
    }

    private static bool TryParseValue(
        string token, int min, int max, string[]? names, out int value, out CronParseError? error)
    {
        error = null;
        token = token.Trim();

        if (names is not null)
        {
            var index = Array.FindIndex(
                names, name => name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                // Month names are 1-based; day names are 0-based. The array order encodes both.
                value = names == MonthNames ? index + 1 : index;
                return true;
            }
        }

        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            error = CronParseError.From("CronErrorNotANumber", token);
            return false;
        }

        if (value < min || value > max)
        {
            error = CronParseError.From("CronErrorOutOfRange", token, min, max);
            value = 0;
            return false;
        }

        return true;
    }
}
