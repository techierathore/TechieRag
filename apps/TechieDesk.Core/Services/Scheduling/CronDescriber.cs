using System.Globalization;
using System.Text;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Renders a <see cref="CronExpression"/> as the plain-language sentence the user reads
/// ("Every weekday at 07:00" / "हर कार्यदिवस 07:00 बजे") — BRD-140 / REQ-UI-046, REQ-UI-055.
/// </summary>
/// <remarks>
/// <para><b>This is not a convenience.</b> BRD-140 states that a cron expression shall never be
/// required and shall not appear in any list, grid or notification. That makes this class the only
/// sanctioned rendering of a schedule, and it is why it must never fall back to printing the raw
/// expression: a fallback would put <c>*/30 * * * *</c> in front of a user in exactly the situation
/// the requirement was written about. The general case therefore composes a sentence from the fields
/// rather than giving up.</para>
/// <para>The text produced here is what the user CONFIRMS and is then stored on the schedule, so a
/// later change to this describer never silently rewords an automation somebody already agreed to.</para>
/// <para><b>REQ-UI-055 / BRD-91 — why WHOLE PATTERNS and not fragments.</b> This file used to glue an
/// English sentence together from pieces: a lead, a comma, a day phrase, the word "at", a time. Handing
/// each piece to a translator produces Hindi nobody says, because the pieces do not go in that order —
/// "at 07:00" is a trailing <i>बजे</i>, "in January" comes BEFORE the day phrase, and a date takes no
/// ordinal ending at all. So every join in this file is itself a resource key with numbered holes
/// (<c>CronDaysAtTime</c>, <c>CronDaysInMonths</c>, <c>CronEveryNMinutesOnDays</c> …), which lets a
/// translator move <c>{0}</c> past <c>{1}</c> without touching code. The only things left as bare
/// vocabulary are nouns — day names, month names — where word order cannot be wrong.</para>
/// <para><b>Numbers are Latin digits in every culture.</b> Every count and every clock time is
/// formatted with <see cref="CultureInfo.InvariantCulture"/> before it reaches a resource string, so
/// "07:00" and "30" read the same in Hindi as in English. That matches the rest of the app — .NET's
/// <c>hi</c> culture does not substitute Devanagari digits — and it keeps a time the user reads
/// beside a time the OS prints from disagreeing.</para>
/// <para><b>⚠ The Hindi here is agent-produced and not native-reviewed.</b> The shapes are
/// grammatical; the one known infelicity is a multi-date list, which repeats <i>तारीख़</i> once per
/// date ("हर महीने की 1 तारीख़ और 15 तारीख़ को") where a native speaker would say it once.</para>
/// </remarks>
public static class CronDescriber
{
    /// <summary>Resource keys for the full weekday names, Sunday first.</summary>
    private static readonly string[] DayNameKeys =
    [
        "CronDaySunday", "CronDayMonday", "CronDayTuesday", "CronDayWednesday",
        "CronDayThursday", "CronDayFriday", "CronDaySaturday"
    ];

    /// <summary>Resource keys for the short weekday names, Sunday first.</summary>
    private static readonly string[] ShortDayNameKeys =
    [
        "CronDayShortSunday", "CronDayShortMonday", "CronDayShortTuesday", "CronDayShortWednesday",
        "CronDayShortThursday", "CronDayShortFriday", "CronDayShortSaturday"
    ];

    /// <summary>Resource keys for the month names, January first.</summary>
    private static readonly string[] MonthNameKeys =
    [
        "CronMonthJanuary", "CronMonthFebruary", "CronMonthMarch", "CronMonthApril",
        "CronMonthMay", "CronMonthJune", "CronMonthJuly", "CronMonthAugust",
        "CronMonthSeptember", "CronMonthOctober", "CronMonthNovember", "CronMonthDecember"
    ];

    /// <summary>Describes a cron expression in the reader's language.</summary>
    /// <param name="expression">The parsed expression.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>A sentence such as <c>Every weekday at 07:00</c>. Never contains cron syntax.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// There is deliberately no overload that omits <paramref name="localize"/>. An English default
    /// would put English sentences back into this file, and the caller that forgot to pass a localizer
    /// would render them on a Hindi install with every test still green — the REQ-UI-051 defect,
    /// rebuilt.
    /// </remarks>
    public static string Describe(CronExpression expression, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(localize);

        var minutes = expression.Minutes;
        var hours = expression.Hours;
        var everyMinute = minutes.Count == 60;
        var everyHour = hours.Count == 24;
        var days = DescribeDays(expression, localize);

        if (everyMinute && everyHour)
        {
            return days.IsEveryDay
                ? localize("CronEveryMinute")
                : localize("CronEveryMinuteOnDays", days.Phrase);
        }

        if (everyHour && minutes.Count == 1)
        {
            var past = Padded(minutes[0]);
            return days.IsEveryDay
                ? localize("CronEveryHourAtMinute", past)
                : localize("CronEveryHourAtMinuteOnDays", past, days.Phrase);
        }

        var minuteStep = UniformStep(minutes, 60);
        if (everyHour && minuteStep is > 1)
        {
            var step = Number(minuteStep.Value);
            return days.IsEveryDay
                ? localize("CronEveryNMinutes", step)
                : localize("CronEveryNMinutesOnDays", step, days.Phrase);
        }

        var hourStep = UniformStep(hours, 24);
        if (minutes.Count == 1 && hourStep is > 1)
        {
            var step = Number(hourStep.Value);
            var past = Padded(minutes[0]);
            return days.IsEveryDay
                ? localize("CronEveryNHoursAtMinute", step, past)
                : localize("CronEveryNHoursAtMinuteOnDays", step, past, days.Phrase);
        }

        // From here the day phrase OPENS the sentence, so it is capitalized. Capitalization is a
        // Latin-script concern and is a no-op on Devanagari, which is why it is applied here rather
        // than being duplicated as a second set of resource keys.
        var lead = Capitalize(days.Phrase);
        if (minutes.Count == 1 && hours.Count == 1)
        {
            return localize("CronDaysAtTime", lead, FormatTime(hours[0], minutes[0]));
        }

        var times = new List<string>();
        foreach (var hour in hours)
        {
            foreach (var minute in minutes)
            {
                times.Add(FormatTime(hour, minute));
            }
        }

        // Long lists become unreadable long before they become wrong; past four times the sentence
        // states the count instead, which is still plain language and still not cron.
        return times.Count <= 4
            ? localize("CronDaysAtTime", lead, JoinReadable(times, localize))
            : localize("CronDaysAtTimeCount", lead, Number(times.Count));
    }

    /// <summary>Describes an expression given as text, or reports that it could not be described.</summary>
    /// <param name="expression">A five-field cron expression.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The sentence, or <see langword="null"/> when the expression does not parse.</returns>
    public static string? TryDescribe(string? expression, LocalizeText localize) =>
        CronExpression.TryParse(expression, out var parsed, out _) ? Describe(parsed, localize) : null;

    /// <summary>Formats a wall-clock time, in Latin digits whatever the culture.</summary>
    /// <param name="hour">The hour, 0-23.</param>
    /// <param name="minute">The minute, 0-59.</param>
    /// <returns>An <c>HH:mm</c> time.</returns>
    private static string FormatTime(int hour, int minute) => Padded(hour) + ":" + Padded(minute);

    /// <summary>Formats a number in Latin digits whatever the culture.</summary>
    /// <param name="value">The number.</param>
    /// <returns>Its invariant text.</returns>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a number as two Latin digits whatever the culture.</summary>
    /// <param name="value">The number.</param>
    /// <returns>Its invariant two-digit text.</returns>
    private static string Padded(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    private static int? UniformStep(IReadOnlyList<int> values, int wrap)
    {
        if (values.Count < 2 || wrap % values.Count != 0)
        {
            return null;
        }

        var step = values[1] - values[0];
        if (step < 1 || values[0] != 0)
        {
            return null;
        }

        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] - values[index - 1] != step)
            {
                return null;
            }
        }

        return values.Count * step == wrap ? step : null;
    }

    private static DayDescription DescribeDays(CronExpression expression, LocalizeText localize)
    {
        var months = DescribeMonths(expression, localize);
        var isEveryMonth = months is null;

        if (!expression.IsDayOfMonthRestricted && !expression.IsDayOfWeekRestricted)
        {
            var everyDay = localize("CronDaysEveryDay");
            return isEveryMonth
                ? new DayDescription(everyDay, true)
                : new DayDescription(localize("CronDaysInMonths", everyDay, months!), false);
        }

        var parts = new List<string>();
        if (expression.IsDayOfWeekRestricted)
        {
            parts.Add(DescribeDaysOfWeek(expression.DaysOfWeek, localize));
        }

        if (expression.IsDayOfMonthRestricted)
        {
            parts.Add(DescribeDaysOfMonth(expression.DaysOfMonth, localize));
        }

        // Both restricted is an OR in cron, and saying "or" out loud is the only honest rendering.
        var joined = parts.Count == 2 ? localize("CronDaysOrJoin", parts[0], parts[1]) : parts[0];
        if (!isEveryMonth)
        {
            joined = localize("CronDaysInMonths", joined, months!);
        }

        return new DayDescription(joined, false);
    }

    private static string DescribeDaysOfWeek(IReadOnlyList<int> days, LocalizeText localize)
    {
        if (days.Count == 7)
        {
            return localize("CronDaysEveryDay");
        }

        if (days.Count == 5 && days[0] == 1 && days[4] == 5)
        {
            return localize("CronDaysEveryWeekday");
        }

        if (days.Count == 2 && days[0] == 0 && days[1] == 6)
        {
            return localize("CronDaysEveryWeekendDay");
        }

        if (days.Count == 1)
        {
            return localize("CronDaysNamed", localize(DayNameKeys[days[0]]));
        }

        var names = new List<string>();
        foreach (var day in days)
        {
            names.Add(localize(ShortDayNameKeys[day]));
        }

        return localize("CronDaysNamed", JoinReadable(names, localize));
    }

    private static string DescribeDaysOfMonth(IReadOnlyList<int> days, LocalizeText localize)
    {
        if (days.Count == 31)
        {
            return localize("CronDaysEveryDay");
        }

        var ordinals = new List<string>();
        foreach (var day in days)
        {
            ordinals.Add(Ordinal(day, localize));
        }

        return ordinals.Count <= 4
            ? localize("CronDaysOfMonth", JoinReadable(ordinals, localize))
            : localize("CronDaysOfMonthCount", Number(ordinals.Count));
    }

    private static string? DescribeMonths(CronExpression expression, LocalizeText localize)
    {
        var months = expression.Months;
        if (months.Count == 12)
        {
            return null;
        }

        var names = new List<string>();
        foreach (var month in months)
        {
            names.Add(localize(MonthNameKeys[month - 1]));
        }

        return names.Count <= 4
            ? JoinReadable(names, localize)
            : localize("CronMonthsCount", Number(names.Count));
    }

    /// <summary>
    /// Renders a day of the month the way the reader's language writes a date.
    /// </summary>
    /// <param name="day">The day, 1-31.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>"1st" in English, "1 तारीख़" in Hindi.</returns>
    /// <remarks>
    /// Four keys rather than an English suffix table, because the suffix is not a suffix everywhere:
    /// English needs st/nd/rd/th chosen by the number, and Hindi needs none of them. Selecting the KEY
    /// by the English rule and letting each language decide what to do with the number keeps both
    /// correct without either one having to know about the other.
    /// </remarks>
    private static string Ordinal(int day, LocalizeText localize)
    {
        var key = (day % 100) is >= 11 and <= 13
            ? "CronOrdinalOther"
            : (day % 10) switch
            {
                1 => "CronOrdinalFirst",
                2 => "CronOrdinalSecond",
                3 => "CronOrdinalThird",
                _ => "CronOrdinalOther"
            };

        return localize(key, Number(day));
    }

    /// <summary>Joins values as a readable list.</summary>
    /// <param name="values">The values, already in the reader's language.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>"a, b and c" in English, "a, b और c" in Hindi.</returns>
    /// <remarks>
    /// Only the FINAL conjunction is a resource key. The comma is punctuation both shipped languages
    /// write the same way, and a key whose whole value is "{0}, {1}" would carry no translatable word
    /// at all — which the Hindi resource gate rejects, correctly.
    /// </remarks>
    private static string JoinReadable(IReadOnlyList<string> values, LocalizeText localize)
    {
        if (values.Count == 1)
        {
            return values[0];
        }

        var builder = new StringBuilder();
        for (var index = 0; index < values.Count - 1; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(values[index]);
        }

        return localize("CronListAnd", builder.ToString(), values[^1]);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>The day half of the sentence.</summary>
    /// <param name="Phrase">The day phrase, mid-sentence cased.</param>
    /// <param name="IsEveryDay">Whether it says nothing a reader could not infer, so it can be dropped.</param>
    private readonly record struct DayDescription(string Phrase, bool IsEveryDay);
}
