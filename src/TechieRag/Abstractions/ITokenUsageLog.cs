using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// A durable record of every token usage, so a host can ask for today's or this month's usage after a
/// restart (REQ-RAG-162, Sevak TR-RAG-059).
/// </summary>
/// <remarks>
/// <para><b>Built in:</b> <see cref="Persistence.SqliteTokenUsageLog"/> and
/// <see cref="Persistence.PostgresTokenUsageLog"/>, both writing the <c>TrTokenUsage</c> table. The builder
/// creates one in the persistence database whenever <c>WithPersistence</c> is set, unless
/// <see cref="UsageTrackingConfig.PersistUsage"/> is false; <c>WithTokenUsageLog</c> supplies any other.</para>
/// <para><b>Synchronous on purpose.</b> <see cref="ITokenTracker.RecordUsage"/> is synchronous and runs from
/// a provider's completion event, often on a UI thread; a blocking wait on an asynchronous write there
/// can deadlock, a short synchronous insert cannot.</para>
/// </remarks>
public interface ITokenUsageLog
{
    /// <summary>Writes one usage record.</summary>
    /// <param name="usage">The usage; its <see cref="TokenUsage.Timestamp"/> is taken as UTC.</param>
    void Append(TokenUsage usage);

    /// <summary>Reads the usage recorded from <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive).</summary>
    /// <param name="fromUtc">The start, in UTC.</param>
    /// <param name="toUtc">The end, in UTC.</param>
    /// <returns>The records in that range, oldest first, with <see cref="TokenUsage.Timestamp"/> in UTC.</returns>
    IReadOnlyList<TokenUsage> Read(DateTime fromUtc, DateTime toUtc);
}
