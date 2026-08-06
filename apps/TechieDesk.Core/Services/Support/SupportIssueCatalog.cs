using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Support;

/// <summary>
/// The issue types, priorities and statuses the Support screen offers (REQ-UI-032, REQ-UI-047).
/// </summary>
/// <remarks>
/// <para>
/// The owner review of 2026-07-26 rejected a Type and a Priority list carrying one value each, so
/// both full sets live here rather than being spelled inline in the markup — one list the dialog,
/// the change-priority dialog and the badges all read from cannot drift out of step with itself.
/// </para>
/// <para>
/// <b>Wire values vs labels.</b> The AppManager usage guide exemplifies <c>Bug</c> and
/// <c>Feature</c> for <c>issueType</c>, and <c>Low</c>/<c>Medium</c>/<c>High</c>/<c>Critical</c>
/// for <c>priority</c>; it does not publish the complete issue-type vocabulary. The remaining
/// codes here follow the two documented examples' shape (a single PascalCase token). If a live
/// AppManager rejects one, it comes back as <c>VALIDATION_ERROR</c> and the screen shows the
/// server's own message — which is why the type is sent as a code and shown as a label, rather
/// than the sentence being sent as-is.
/// </para>
/// <para>
/// <b>REQ-UI-051 / BRD-91.</b> The <c>Code</c> column is the wire vocabulary and is untouched;
/// the label and qualifier columns are RESOURCE KEYS. The split was already the design — this
/// change only moves the keys from the Support page's own switch into the table, so a seventh
/// issue type cannot ship with a label the page's switch has never heard of.
/// </para>
/// </remarks>
public static class SupportIssueCatalog
{
    /// <summary>Gets the issue types, in the order the New issue dialog lists them.</summary>
    public static IReadOnlyList<SupportIssueOption> Types { get; } =
    [
        new("Bug", "SupportTypeBug", "SupportTypeQualifierBug"),
        new("Feature", "SupportTypeFeature", "SupportTypeQualifierFeature"),
        new("Question", "SupportTypeQuestion", "SupportTypeQualifierQuestion"),
        new("Billing", "SupportTypeBilling", "SupportTypeQualifierBilling"),
        new("Data", "SupportTypeData", "SupportTypeQualifierData"),
        new("Other", "SupportTypeOther", "SupportTypeQualifierOther")
    ];

    /// <summary>Gets the priorities with their plain-language qualifiers, lowest first.</summary>
    public static IReadOnlyList<SupportIssueOption> Priorities { get; } =
    [
        new("Low", "SupportPriorityLow", "SupportPriorityQualifierLow"),
        new("Medium", "SupportPriorityMedium", "SupportPriorityQualifierMedium"),
        new("High", "SupportPriorityHigh", "SupportPriorityQualifierHigh"),
        new("Critical", "SupportPriorityCritical", "SupportPriorityQualifierCritical")
    ];

    /// <summary>Gets the statuses the list filter offers, matching the <c>status</c> values.</summary>
    public static IReadOnlyList<SupportIssueOption> Statuses { get; } =
    [
        new("Open", "SupportStatusOpen", "SupportStatusQualifierOpen"),
        new("InProgress", "SupportStatusInProgress", "SupportStatusQualifierInProgress"),
        new("Resolved", "SupportStatusResolved", "SupportStatusQualifierResolved"),
        new("Closed", "SupportStatusClosed", "SupportStatusQualifierClosed")
    ];

    /// <summary>The priority code a new issue starts on.</summary>
    public const string DefaultPriority = "Medium";

    /// <summary>The issue-type code a new issue starts on.</summary>
    public const string DefaultType = "Bug";

    /// <summary>Resolves the label for an issue-type code.</summary>
    /// <param name="code">The wire code, or null.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The display label.</returns>
    public static string TypeLabel(string? code, LocalizeText localize) => LabelFor(Types, code, localize);

    /// <summary>Resolves the label for a priority code.</summary>
    /// <param name="code">The wire code, or null.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The display label.</returns>
    public static string PriorityLabel(string? code, LocalizeText localize) => LabelFor(Priorities, code, localize);

    /// <summary>Resolves the label for a status code.</summary>
    /// <param name="code">The wire code, or null.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The display label.</returns>
    public static string StatusLabel(string? code, LocalizeText localize) => LabelFor(Statuses, code, localize);

    /// <summary>Resolves the label and qualifier for a priority code, as the pickers present it.</summary>
    /// <param name="code">The wire code, or null.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The label, an em dash and the qualifier.</returns>
    /// <remarks>
    /// The em dash is punctuation rather than prose, so the two halves stay separate resource
    /// strings instead of a <c>"{0} — {1}"</c> key that carries no translatable words. A code the
    /// catalogue does not know has no qualifier to show, so only the label is rendered.
    /// </remarks>
    public static string PriorityLabelWithQualifier(string? code, LocalizeText localize)
    {
        var option = Find(Priorities, code);
        ArgumentNullException.ThrowIfNull(localize);

        return option is null
            ? PriorityLabel(code, localize)
            : $"{localize(option.LabelKey)} — {localize(option.QualifierKey)}";
    }

    /// <summary>Finds the catalogue entry for a code.</summary>
    /// <param name="options">The list to search.</param>
    /// <param name="code">The wire code, or null.</param>
    /// <returns>The option, or null when the code is blank or unknown.</returns>
    public static SupportIssueOption? Find(IReadOnlyList<SupportIssueOption> options, string? code)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        foreach (var option in options)
        {
            if (string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return null;
    }

    /// <summary>Determines whether a status means the issue is finished.</summary>
    /// <param name="status">The wire status code, or null.</param>
    /// <returns>True when the issue is closed.</returns>
    /// <remarks>
    /// Only <c>Closed</c> counts. <c>Resolved</c> deliberately does not: the mockup's own note says
    /// resolved issues are the ones that <i>should</i> be closed, so treating them as already closed
    /// would hide the button that does it.
    /// </remarks>
    public static bool IsClosed(string? status) =>
        string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a label from an option list.</summary>
    /// <param name="options">The list to search.</param>
    /// <param name="code">The wire code, or null.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The matching label, the code when unmatched, or an em dash when blank.</returns>
    /// <remarks>
    /// The two fallbacks are the reason this stays a method rather than a bare <c>LabelKey</c>
    /// property: an em dash is punctuation and needs no translation, and an unrecognised server
    /// value is shown AS THE SERVER SENT IT — a code the catalogue has never heard of has no key,
    /// and inventing one would be the screen quietly lying about what the issue actually is.
    /// </remarks>
    private static string LabelFor(
        IReadOnlyList<SupportIssueOption> options, string? code, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        if (string.IsNullOrWhiteSpace(code))
        {
            return "—";
        }

        return Find(options, code) is { } option ? localize(option.LabelKey) : code;
    }
}

/// <summary>
/// One selectable value: the code that goes on the wire and the keys naming the words the user
/// reads.
/// </summary>
/// <param name="Code">The value sent to and received from AppManager. Culture-invariant.</param>
/// <param name="LabelKey">Resource key for the short display label.</param>
/// <param name="QualifierKey">Resource key for the plain-language explanation beside the label.</param>
public sealed record SupportIssueOption(string Code, string LabelKey, string QualifierKey);
