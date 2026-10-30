using TechieRag.Persistence;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// The stores stamp rows through <see cref="RowClock"/> so rows read back <c>ORDER BY</c> their time
/// keep the order they were written in (CI runs 40914516648 and 41040052568).
/// </summary>
public sealed class RowClockTests
{
    /// <summary>The same clock time asked for twice gives two different, rising times.</summary>
    [Fact]
    public void SameTickGivesRisingTimes()
    {
        var clock = new RowClock();
        var now = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

        var first = clock.Next(now);
        var second = clock.Next(now);

        Assert.Equal(now, first);
        Assert.Equal(now.AddTicks(1), second);
    }

    /// <summary>A clock that steps backwards still gives a time after the last one handed out.</summary>
    [Fact]
    public void BackwardClockStepStillRises()
    {
        var clock = new RowClock();
        var now = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

        var first = clock.Next(now);
        var afterStep = clock.Next(now.AddSeconds(-2));

        Assert.True(afterStep > first);
        Assert.Equal(DateTimeKind.Utc, afterStep.Kind);
    }

    /// <summary>A later time asked for is kept as it is.</summary>
    [Fact]
    public void LaterTimeIsKept()
    {
        var clock = new RowClock();
        var now = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
        clock.Next(now);

        Assert.Equal(now.AddMinutes(1), clock.Next(now.AddMinutes(1)));
    }
}
