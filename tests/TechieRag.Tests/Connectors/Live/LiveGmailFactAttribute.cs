using Xunit;

namespace TechieRag.Tests.Connectors.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that acts on a real Gmail mailbox over IMAP
/// (REQ-RAG-110 / BRD-169, REQ-RAG-111 / BRD-170).
/// </summary>
/// <remarks>
/// <para><b>A dedicated test account only.</b> The live mail-action test moves, labels and trashes
/// real messages (and puts them back). Point it at an account that exists for this purpose, with at
/// least three messages in INBOX, never at a mailbox anyone reads.</para>
/// <para><b>Skipped, not silently absent.</b> With either variable unset the test reports as skipped
/// with the reason below. The credentials are read from the environment only and never written to a
/// committed file (REQ-NFR-002); the password is a Gmail app password, not the account password.</para>
/// </remarks>
public sealed class LiveGmailFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveGmail";

    /// <summary>Environment variable holding the Gmail address of the test account.</summary>
    public const string UserVariable = "TechieRagTestGmailUser";

    /// <summary>Environment variable holding a Gmail app password for that account.</summary>
    public const string AppPasswordVariable = "TechieRagTestGmailAppPassword";

    /// <summary>Initializes a new instance of the <see cref="LiveGmailFactAttribute"/> class.</summary>
    public LiveGmailFactAttribute()
    {
        if (User.Length == 0 || AppPassword.Length == 0)
        {
            Skip = $"No Gmail test account is configured for this host. Set {UserVariable} and "
                + $"{AppPasswordVariable} (a Gmail app password) for a dedicated test account with at least three messages in INBOX.";
        }
    }

    /// <summary>Gets the test account's address, or an empty string.</summary>
    public static string User => Environment.GetEnvironmentVariable(UserVariable)?.Trim() ?? string.Empty;

    /// <summary>Gets the test account's app password, or an empty string.</summary>
    public static string AppPassword => Environment.GetEnvironmentVariable(AppPasswordVariable)?.Trim() ?? string.Empty;
}
