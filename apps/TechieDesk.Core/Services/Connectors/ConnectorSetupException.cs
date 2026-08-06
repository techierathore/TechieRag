using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// A run-level connector refusal THIS APP authored, carrying its reason as resource codes rather
/// than as a finished sentence (REQ-UI-056 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>Why a second exception type beside <see cref="ConnectorException"/>.</b> The library's
/// <see cref="ConnectorException"/> is <c>sealed</c> and carries only a <see cref="Exception.Message"/>
/// string, which is right for it: the words in a "404 from api.github.com" are the library's to
/// choose and this app cannot translate them. But the refusals raised HERE — "that connector is no
/// longer saved", "the saved token could not be read" — are ours, they are shown to the operator, and
/// they end up persisted on <c>ScheduleRun.FailureReason</c>. Those need codes. Deriving was not an
/// option, so the two live side by side and every catch site handles both.</para>
/// <para><b>The base message is the English rendering</b>, so a log line, a crash report or any
/// <c>catch (Exception)</c> that only knows about <see cref="Exception.Message"/> still reads
/// sensibly. <see cref="Reason"/> is what the run row and the screen use.</para>
/// </remarks>
public sealed class ConnectorSetupException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConnectorSetupException"/> class.</summary>
    /// <param name="sourceType">The connector's source type, e.g. "repository".</param>
    /// <param name="reason">Why the run cannot proceed, as codes and arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is <see langword="null"/>.</exception>
    public ConnectorSetupException(string sourceType, JobMessage reason)
        : base(Require(reason).ToInvariantString())
    {
        SourceType = sourceType;
        Reason = reason;
    }

    /// <summary>Gets the source type of the connector that failed.</summary>
    public string SourceType { get; }

    /// <summary>Gets why the run cannot proceed, as codes and arguments.</summary>
    public JobMessage Reason { get; }

    /// <summary>
    /// Gets the reason an exception gives for a failed run, coded when this app authored it and
    /// verbatim when it did not.
    /// </summary>
    /// <param name="exception">The exception that ended the run.</param>
    /// <returns>The reason to record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The single place the two exception types are reconciled. A library or transport message goes
    /// through as <see cref="JobMessage.Text"/>, which stores no codes and therefore renders through
    /// the same stored-text branch a pre-REQ-UI-056 row does — deliberately, because inventing a code
    /// per third-party message would be a lie about what is translatable.
    /// </remarks>
    public static JobMessage ReasonFor(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is ConnectorSetupException setup
            ? setup.Reason
            : JobMessage.Text(exception.Message);
    }

    private static JobMessage Require(JobMessage reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return reason;
    }
}
