namespace TechieRag.Persistence;

/// <summary>
/// Hands out UTC times that never repeat and never go backwards, for rows that are read back
/// <c>ORDER BY</c> their time.
/// </summary>
/// <remarks>
/// Two rows saved on the same clock tick, or across a backward clock step (a virtual machine
/// resyncing its time), compared equal or reversed, so a history or a pinned-document list came
/// back out of the order it was written in (CI runs 40914516648 and 41040052568, 2026-10-30).
/// Each time given out is at least one tick after the previous one from the same instance.
/// </remarks>
internal sealed class RowClock
{
    private long lastTicks;

    /// <summary>The later of <paramref name="atLeast"/> and one tick after the last time given out.</summary>
    /// <param name="atLeast">The time the caller would use on its own, normally now.</param>
    /// <returns>A UTC time later than every time this instance gave out before.</returns>
    internal DateTime Next(DateTime atLeast)
    {
        var wanted = atLeast.ToUniversalTime().Ticks;
        while (true)
        {
            var last = Interlocked.Read(ref lastTicks);
            var next = Math.Max(wanted, last + 1);
            if (Interlocked.CompareExchange(ref lastTicks, next, last) == last)
            {
                return new DateTime(next, DateTimeKind.Utc);
            }
        }
    }
}
