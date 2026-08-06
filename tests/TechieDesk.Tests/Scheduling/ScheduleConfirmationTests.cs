using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Scheduling;
using TechieDesk.Services.Scheduling.Authoring;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// The confirm gate: nothing saves without an explicit confirm showing what will actually run
/// (BRD-140 / ADR-010).
/// </summary>
/// <remarks>
/// The point of these is the misreading case. Natural-language interpretation will sometimes get
/// "every other Tuesday" wrong, and the confirm panel is the only thing standing between that and an
/// automation running unattended — so the guard has to be a property of saving, not of a dialog.
/// </remarks>
public sealed class ScheduleConfirmationTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A draft confirmed with the lines that were displayed is saved.</summary>
    [Fact]
    public async Task ADraftConfirmedWithWhatWasShownIsSaved()
    {
        var harness = new Harness();
        var draft = Draft();

        var saved = await harness.Service.CreateAsync(
            draft, new ScheduleConfirmation(draft.ScheduleText, draft.ActionSummary));

        Assert.Equal("Every weekday at 07:00", saved.ScheduleText);
        Assert.Equal(new DateTime(2026, 7, 27, 7, 0, 0, DateTimeKind.Utc).AddDays(1), saved.NextRunUtc);
        Assert.Single(harness.Schedules.Items);
    }

    /// <summary>
    /// A schedule whose text changed after it was reviewed is refused — the case where the user
    /// confirmed one sentence and a different schedule is about to be stored.
    /// </summary>
    [Fact]
    public async Task ADraftThatChangedAfterReviewIsRefused()
    {
        var harness = new Harness();
        var reviewed = Draft();
        var changed = reviewed with { ScheduleText = "Every day at 03:00", CronExpression = "0 3 * * *" };

        var exception = await Assert.ThrowsAsync<ScheduleNotConfirmedException>(() =>
            harness.Service.CreateAsync(
                changed, new ScheduleConfirmation(reviewed.ScheduleText, reviewed.ActionSummary)));

        Assert.Contains("Every weekday at 07:00", exception.Message);
        Assert.Contains("Every day at 03:00", exception.Message);
        Assert.Empty(harness.Schedules.Items);
    }

    /// <summary>An action that changed after review is refused for the same reason.</summary>
    [Fact]
    public async Task AnActionThatChangedAfterReviewIsRefused()
    {
        var harness = new Harness();
        var reviewed = Draft();
        var changed = reviewed with { ActionSummary = "Delete every document" };

        await Assert.ThrowsAsync<ScheduleNotConfirmedException>(() =>
            harness.Service.CreateAsync(
                changed, new ScheduleConfirmation(reviewed.ScheduleText, reviewed.ActionSummary)));

        Assert.Empty(harness.Schedules.Items);
    }

    /// <summary>A draft that failed validation cannot be saved even with a matching confirmation.</summary>
    [Fact]
    public async Task ADraftThatDidNotValidateCannotBeSaved()
    {
        var harness = new Harness();
        var draft = Draft() with { Confidence = DraftConfidence.Low };

        await Assert.ThrowsAsync<ScheduleNotConfirmedException>(() =>
            harness.Service.CreateAsync(
                draft, new ScheduleConfirmation(draft.ScheduleText, draft.ActionSummary)));

        Assert.Empty(harness.Schedules.Items);
    }

    /// <summary>The instruction the user typed is stored with the schedule.</summary>
    [Fact]
    public async Task TheOriginalInstructionIsStoredWithTheSchedule()
    {
        var harness = new Harness();
        var draft = Draft();

        var saved = await harness.Service.CreateAsync(
            draft, new ScheduleConfirmation(draft.ScheduleText, draft.ActionSummary));

        Assert.Equal("every weekday at 7, sync the mailbox", saved.SourceInstruction);
    }

    /// <summary>
    /// Resuming a schedule paused for a fortnight computes the next run from now, so it does not fire
    /// the instant it is un-paused.
    /// </summary>
    [Fact]
    public async Task ResumingAPausedScheduleDoesNotFireImmediately()
    {
        var harness = new Harness();
        var draft = Draft();
        var saved = await harness.Service.CreateAsync(
            draft, new ScheduleConfirmation(draft.ScheduleText, draft.ActionSummary));

        await harness.Service.SetEnabledAsync(saved.ScheduleId, false);
        harness.Clock.Advance(TimeSpan.FromDays(14));
        await harness.Service.SetEnabledAsync(saved.ScheduleId, true);

        Assert.True(harness.Schedules.Items[0].NextRunUtc > harness.Clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>Pausing clears the next run so nothing is queued while it is paused.</summary>
    [Fact]
    public async Task PausingClearsTheNextRun()
    {
        var harness = new Harness();
        var draft = Draft();
        var saved = await harness.Service.CreateAsync(
            draft, new ScheduleConfirmation(draft.ScheduleText, draft.ActionSummary));

        await harness.Service.SetEnabledAsync(saved.ScheduleId, false);

        Assert.Null(harness.Schedules.Items[0].NextRunUtc);
    }

    private static ScheduleDraft Draft() => new()
    {
        Name = "Sync legal mailbox",
        Instruction = "every weekday at 7, sync the mailbox",
        CronExpression = "0 7 * * 1-5",
        TimeZoneId = TimeZoneInfo.Utc.Id,
        ScheduleText = "Every weekday at 07:00",
        JobKind = "Test",
        ActionSummary = "Test action (no payload)",
        Steps = [new ScheduleDraftStep("Runs", "Every weekday at 07:00")]
    };

    private sealed class Harness
    {
        public Harness()
        {
            Clock = new TestClock(Now);
            Schedules = new FakeScheduleRepository();
            var runs = new FakeScheduleRunRepository();
            var runner = new JobRunner(
                runs, [new FakeJobHandler()], Clock, NullLogger<JobRunner>.Instance);
            Service = new ScheduleService(
                Schedules,
                runs,
                new BackgroundJobService(runner, NullLogger<BackgroundJobService>.Instance),
                Clock,
                NullLogger<ScheduleService>.Instance,
                SchedulingText.Localize);
        }

        public TestClock Clock { get; }

        public FakeScheduleRepository Schedules { get; }

        public ScheduleService Service { get; }
    }
}
