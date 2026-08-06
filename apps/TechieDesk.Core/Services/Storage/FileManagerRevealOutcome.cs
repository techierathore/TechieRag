namespace TechieDesk.Services.Storage;

/// <summary>
/// The result of asking the host's file manager to reveal a path (REQ-UI-041).
/// </summary>
/// <param name="Launched">True only when a file-manager process was actually started.</param>
/// <param name="MessageKey">
/// A key in <c>AppStrings.resx</c> describing success or the real failure, resolved by whatever
/// surface reports it (REQ-UI-055 / BRD-91).
/// </param>
/// <param name="Arguments">
/// Format arguments for <paramref name="MessageKey"/> — a path, a launcher name, an OS error. All of
/// them are VALUES rather than words, so they read the same in every culture.
/// </param>
/// <remarks>
/// This carried an English sentence until REQ-UI-055. Six surfaces show it — the data/storage table,
/// the log-folder button on the event log, backup/restore, app updates, the invoice list and the
/// native menu bar — and every one of them would have rendered that sentence untranslated on a Hindi
/// install, because a static class in <c>Services/</c> is invisible to both razor localization
/// counters.
/// </remarks>
public sealed record FileManagerRevealOutcome(bool Launched, string MessageKey, object[] Arguments);
