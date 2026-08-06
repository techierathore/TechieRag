using TechieDesk.Services.Scheduling;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// Cron parsing and next-occurrence arithmetic (REQ-FN-028), including the two daylight-saving
/// transitions and the month-end cases.
/// </summary>
public sealed class CronExpressionTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>An expression with the wrong number of fields is rejected, naming the count found.</summary>
    [Fact]
    public void ParseRejectsWrongFieldCount()
    {
        Assert.False(CronExpression.TryParse("0 7 * *", out _, out var error));
        Assert.Equal("CronErrorFieldCount", error!.Key);
        Assert.Equal([4], error.Arguments);

        // REQ-UI-055: the parser returns a KEY, and the sentence a person reads comes from the
        // resources. Both halves are asserted, because a key nobody can resolve is not an error
        // message.
        Assert.Contains("5 fields", error.Resolve(SchedulingText.Localize), StringComparison.Ordinal);
    }

    /// <summary>A value outside a field's range is rejected, naming the range.</summary>
    [Fact]
    public void ParseRejectsOutOfRangeValue()
    {
        Assert.False(CronExpression.TryParse("0 25 * * *", out _, out var error));
        Assert.Equal("CronErrorOutOfRange", error!.Key);

        // The offending token goes through as DATA, never translated.
        Assert.Equal(["25", 0, 23], error.Arguments);
        Assert.Contains("0-23", error.Resolve(SchedulingText.Localize), StringComparison.Ordinal);
    }

    /// <summary>Three-letter day and month names parse, because a model will emit them.</summary>
    [Fact]
    public void ParseAcceptsDayAndMonthNames()
    {
        var expression = CronExpression.Parse("0 6 * JAN MON");
        Assert.Equal([1], expression.Months);
        Assert.Equal([1], expression.DaysOfWeek);
    }

    /// <summary>Day-of-week 7 means Sunday, the same as 0, and is not reported twice.</summary>
    [Fact]
    public void ParseFoldsSevenOntoSunday()
    {
        var expression = CronExpression.Parse("0 6 * * 7");
        Assert.Equal([0], expression.DaysOfWeek);
    }

    /// <summary>A weekday expression fires Monday to Friday and skips the weekend.</summary>
    [Fact]
    public void WeekdayExpressionSkipsTheWeekend()
    {
        var expression = CronExpression.Parse("0 7 * * 1-5");

        // Friday 2026-07-24 07:00 UTC → next is Monday, not Saturday.
        var next = expression.GetNextOccurrenceUtc(new DateTime(2026, 7, 24, 7, 0, 0, DateTimeKind.Utc), Utc);

        Assert.Equal(new DateTime(2026, 7, 27, 7, 0, 0, DateTimeKind.Utc), next);
    }

    /// <summary>A half-hourly expression advances by thirty minutes and never repeats an instant.</summary>
    [Fact]
    public void HalfHourlyExpressionAdvancesByThirtyMinutes()
    {
        var expression = CronExpression.Parse("*/30 * * * *");

        var occurrences = expression.GetNextOccurrencesUtc(
            new DateTime(2026, 7, 27, 10, 5, 0, DateTimeKind.Utc), Utc, 3);

        Assert.Equal(
            [
                new DateTime(2026, 7, 27, 10, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 27, 11, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 27, 11, 30, 0, DateTimeKind.Utc)
            ],
            occurrences);
    }

    /// <summary>The 31st is skipped in months that do not have one, rather than landing on the 1st.</summary>
    [Fact]
    public void MonthEndExpressionSkipsShortMonths()
    {
        var expression = CronExpression.Parse("0 3 31 * *");

        // From 31 January the next 31st is 31 March: February and April have none.
        var next = expression.GetNextOccurrenceUtc(new DateTime(2026, 1, 31, 3, 0, 0, DateTimeKind.Utc), Utc);

        Assert.Equal(new DateTime(2026, 3, 31, 3, 0, 0, DateTimeKind.Utc), next);
    }

    /// <summary>29 February resolves to the next leap year rather than to 1 March.</summary>
    [Fact]
    public void LeapDayExpressionWaitsForTheNextLeapYear()
    {
        var expression = CronExpression.Parse("0 0 29 2 *");

        var next = expression.GetNextOccurrenceUtc(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Utc);

        Assert.Equal(new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc), next);
    }

    /// <summary>The last-day-of-month case still fires on 30-day months when the day is the 30th.</summary>
    [Fact]
    public void ThirtiethFiresInThirtyDayMonths()
    {
        var expression = CronExpression.Parse("0 3 30 * *");

        var next = expression.GetNextOccurrenceUtc(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), Utc);

        Assert.Equal(new DateTime(2026, 4, 30, 3, 0, 0, DateTimeKind.Utc), next);
    }

    /// <summary>
    /// A daily 01:30 job in London keeps its 01:30 wall-clock time either side of the spring-forward
    /// transition, so the UTC instant moves by an hour.
    /// </summary>
    [Fact]
    public void DailyJobKeepsWallClockTimeAcrossSpringForward()
    {
        var expression = CronExpression.Parse("30 1 * * *");

        // London springs forward at 01:00 UTC on 2026-03-29.
        var before = expression.GetNextOccurrenceUtc(
            new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc), London);
        var after = expression.GetNextOccurrenceUtc(
            new DateTime(2026, 3, 29, 12, 0, 0, DateTimeKind.Utc), London);

        Assert.Equal(new DateTime(2026, 3, 28, 1, 30, 0, DateTimeKind.Utc), before);
        Assert.Equal(new DateTime(2026, 3, 30, 0, 30, 0, DateTimeKind.Utc), after);
    }

    /// <summary>
    /// A wall-clock time that the spring-forward transition deletes still fires — at the first
    /// instant that exists again — instead of being silently skipped for the day.
    /// </summary>
    [Fact]
    public void SpringForwardDoesNotSwallowARun()
    {
        // New York jumps 02:00 → 03:00 local on 2026-03-08. A daily 02:30 job has no 02:30 that day.
        var expression = CronExpression.Parse("30 2 * * *");

        var next = expression.GetNextOccurrenceUtc(
            new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc), NewYork);

        // 03:00 EDT on the 8th = 07:00 UTC. The run happens, an hour late, rather than not at all.
        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), next);
    }

    /// <summary>
    /// A wall-clock time the fall-back transition repeats fires once, on the first pass, and is not
    /// run twice.
    /// </summary>
    [Fact]
    public void FallBackFiresOnceNotTwice()
    {
        // New York falls back 02:00 → 01:00 local on 2026-11-01; 01:30 happens twice.
        var expression = CronExpression.Parse("30 1 * * *");

        var first = expression.GetNextOccurrenceUtc(
            new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), NewYork);
        var second = expression.GetNextOccurrenceUtc(first!.Value, NewYork);

        // First pass: 01:30 EDT = 05:30 UTC. The repeat at 01:30 EST (06:30 UTC) is NOT taken; the
        // next occurrence is the following day.
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), first);
        Assert.Equal(new DateTime(2026, 11, 2, 6, 30, 0, DateTimeKind.Utc), second);
    }

    /// <summary>
    /// A half-hourly job runs the repeated hour once across fall-back, not twice, and every
    /// occurrence strictly increases.
    /// </summary>
    [Fact]
    public void HalfHourlyRunsTheRepeatedHourOnce()
    {
        var expression = CronExpression.Parse("*/30 * * * *");

        var occurrences = expression.GetNextOccurrencesUtc(
            new DateTime(2026, 11, 1, 4, 45, 0, DateTimeKind.Utc), NewYork, 6);

        Assert.Equal(occurrences.OrderBy(instant => instant).ToList(), occurrences);
        Assert.Equal(occurrences.Distinct().Count(), occurrences.Count);

        // 05:00–06:00 UTC is the first pass of 01:00–02:00 local. The second pass (06:00, 06:30 UTC)
        // is skipped, and the sequence resumes at 02:00 local = 07:00 UTC.
        Assert.Contains(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), occurrences);
        Assert.DoesNotContain(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), occurrences);
        Assert.Contains(new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc), occurrences);
    }

    /// <summary>
    /// Restricting both day-of-month and day-of-week matches either, per Vixie cron — the rule most
    /// often got wrong.
    /// </summary>
    [Fact]
    public void RestrictingBothDayFieldsMatchesEither()
    {
        var expression = CronExpression.Parse("0 0 13 * 5");

        // Friday 2026-02-13 matches both. Friday the 6th matches on day-of-week alone; the 13th of
        // March 2026 is a Friday too, so use a month where the 13th is not a Friday to prove the OR.
        Assert.True(expression.Matches(new DateTime(2026, 1, 13, 0, 0, 0)));  // Tuesday the 13th
        Assert.True(expression.Matches(new DateTime(2026, 1, 2, 0, 0, 0)));   // Friday the 2nd
        Assert.False(expression.Matches(new DateTime(2026, 1, 5, 0, 0, 0)));  // Monday the 5th
    }

    /// <summary>Restricting only day-of-week leaves day-of-month unconstrained, and vice versa.</summary>
    [Fact]
    public void RestrictingOneDayFieldConstrainsOnlyThatField()
    {
        var weekly = CronExpression.Parse("0 6 * * 1");
        Assert.True(weekly.Matches(new DateTime(2026, 7, 27, 6, 0, 0)));   // Monday
        Assert.False(weekly.Matches(new DateTime(2026, 7, 28, 6, 0, 0)));  // Tuesday

        var monthly = CronExpression.Parse("0 6 1 * *");
        Assert.True(monthly.Matches(new DateTime(2026, 7, 1, 6, 0, 0)));
        Assert.False(monthly.Matches(new DateTime(2026, 7, 2, 6, 0, 0)));
    }

    /// <summary>The next occurrence is strictly after the instant asked about, never equal to it.</summary>
    [Fact]
    public void NextOccurrenceIsStrictlyAfterTheInstantGiven()
    {
        var expression = CronExpression.Parse("0 7 * * *");
        var atSeven = new DateTime(2026, 7, 27, 7, 0, 0, DateTimeKind.Utc);

        Assert.Equal(atSeven.AddDays(1), expression.GetNextOccurrenceUtc(atSeven, Utc));
    }
}
