using System.Globalization;
using System.Text;
using TechieDesk.Services.Localization;
using TechieRag;
using TechieRag.Abstractions;

namespace TechieDesk.Services.Scheduling.Authoring;

/// <summary>
/// Interprets a natural-language instruction into a reviewable schedule draft using the configured
/// local model (BRD-140 / REQ-UI-046, ADR-010).
/// </summary>
/// <remarks>
/// <para><b>Interpretation is constrained to actions the app actually exposes.</b> The prompt lists
/// the registered <see cref="IScheduledJobHandler"/>s and the model <i>selects</i> from them. A model
/// free to invent an action would produce a draft that reads perfectly and cannot run, and the
/// failure would surface at 07:00 three days later rather than in the dialog.</para>
/// <para><b>Nothing the model says is taken on trust.</b> The expression is re-parsed, the action is
/// looked up, the payload is handed to that action to validate, and the displayed sentence is
/// recomputed from the expression. Anything that does not check out becomes a warning and pulls the
/// confidence down; a draft that fails outright is returned <see cref="DraftConfidence.Low"/> and
/// cannot be saved.</para>
/// <para><b>⚠ Not exercised against a live model.</b> No LLM provider is configured on the
/// development host, so this class has been driven only by fakes. The prompt's wording and the
/// model's ability to return this JSON shape are unproven.</para>
/// <para><b>REQ-UI-055.</b> Every refusal, warning and step badge is a resource key resolved through
/// <see cref="LocalizeText"/>. The PROMPT is not: it is machine text addressed to the model, it is
/// written as a raw string literal so it is plainly one block rather than a dozen sentences, and
/// translating it would change what the model is asked for. The cron expression the model returns is
/// data — re-parsed, stored and round-tripped — and never goes near a resource.</para>
/// </remarks>
public sealed class ScheduleInterpreter : IScheduleInterpreter
{
    private const int PreviewRunCount = 3;

    private readonly ITechieRag techieRag;
    private readonly IJobRunner runner;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ScheduleInterpreter> logger;
    private readonly LocalizeText localize;

    /// <summary>Initializes the interpreter.</summary>
    /// <param name="techieRag">Supplies the configured LLM provider (BRD-99: the local model).</param>
    /// <param name="runner">Supplies the registered actions interpretation is constrained to.</param>
    /// <param name="timeProvider">Clock, for the next-run preview.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    public ScheduleInterpreter(
        ITechieRag techieRag,
        IJobRunner runner,
        TimeProvider timeProvider,
        ILogger<ScheduleInterpreter> logger,
        LocalizeText localize)
    {
        this.techieRag = techieRag;
        this.runner = runner;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.localize = localize;
    }

    /// <inheritdoc />
    public IReadOnlyList<AvailableAction> AvailableActions => runner.AvailableHandlers
        .Select(handler => new AvailableAction(handler.JobKind, handler.DisplayNameKey, handler.DescriptionKey))
        .ToList();

    /// <inheritdoc />
    public bool IsModelAvailable => TryGetProvider() is not null;

    /// <inheritdoc />
    public async Task<ScheduleInterpretation> InterpretAsync(
        string instruction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return ScheduleInterpretation.Failed(localize("ScheduleInterpretNeedsInstruction"));
        }

        var provider = TryGetProvider();
        if (provider is null)
        {
            // Named as a configuration gap, not as a failure of the instruction. BRD-99 keeps this on
            // the configured local model, so the fix is in LLM settings and the message says so.
            return ScheduleInterpretation.Failed(localize("ScheduleInterpretNoLocalModel"));
        }

        var actions = AvailableActions;
        if (actions.Count == 0)
        {
            return ScheduleInterpretation.Failed(localize("ScheduleInterpretNoActions"));
        }

        ScheduleInterpretationPayload? payload;
        try
        {
            payload = await provider.CompleteAsync<ScheduleInterpretationPayload>(
                BuildPrompt(instruction, actions), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The configured model could not interpret a schedule instruction");
            return ScheduleInterpretation.Failed(
                localize("ScheduleInterpretModelFailed", provider.Name, exception.Message));
        }

        return payload is null
            ? ScheduleInterpretation.Failed(localize("ScheduleInterpretNoSchedule"))
            : BuildDraft(instruction, payload, actions);
    }

    /// <inheritdoc />
    public ScheduleInterpretation Rebuild(ScheduleDraft draft, string cronExpression)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!CronExpression.TryParse(cronExpression, out var cron, out var error))
        {
            return ScheduleInterpretation.Failed(error!.Resolve(localize));
        }

        var zone = ResolveZone(draft.TimeZoneId);
        var steps = new List<ScheduleDraftStep>(draft.Steps);
        var scheduleText = CronDescriber.Describe(cron, localize);
        if (steps.Count > 0 && steps[0].Label == RunsLabel)
        {
            steps[0] = steps[0] with { Text = scheduleText };
        }
        else
        {
            steps.Insert(0, new ScheduleDraftStep(RunsLabel, scheduleText));
        }

        return new ScheduleInterpretation(draft with
        {
            CronExpression = cron.Expression,
            ScheduleText = scheduleText,
            Steps = steps,
            NextRunsUtc = cron.GetNextOccurrencesUtc(
                timeProvider.GetUtcNow().UtcDateTime, zone, PreviewRunCount)
        });
    }

    /// <summary>The badge shown against the schedule line in the confirm panel.</summary>
    /// <remarks>
    /// Read as well as written: <see cref="Rebuild"/> finds the schedule line by it. Both ends resolve
    /// it from the same localizer in the same dialog, so the comparison holds — the label is display
    /// text, and there is no persisted or wire form of it to disagree with.
    /// </remarks>
    private string RunsLabel => localize("ScheduleDraftStepRuns");

    private ScheduleInterpretation BuildDraft(
        string instruction, ScheduleInterpretationPayload payload, IReadOnlyList<AvailableAction> actions)
    {
        var warnings = new List<string>();

        if (!CronExpression.TryParse(payload.Cron, out var cron, out var cronError))
        {
            // No draft at all rather than a draft with a broken schedule: there is nothing here for a
            // user to review, and offering one would invite a confirm on a schedule that cannot fire.
            return ScheduleInterpretation.Failed(
                localize("ScheduleInterpretUnusableCron", cronError!.Resolve(localize)));
        }

        var handler = runner.FindHandler(payload.JobKind);
        if (handler is null)
        {
            // In the READER's language. The list of actions the app can do is the whole content of
            // this refusal, and naming them in English inside a Hindi sentence was the REQ-UI-056
            // defect this seam change exists to fix.
            var offered = string.Join(", ", actions.Select(action => localize(action.DisplayNameKey)));
            return ScheduleInterpretation.Failed(localize("ScheduleInterpretUnknownAction", offered));
        }

        var payloadError = handler.ValidatePayload(payload.Payload);
        if (payloadError is not null)
        {
            // Validated at authoring time, never at first use (BRD-136). A workspace that does not
            // exist must be caught in this dialog, not at 07:00 on Thursday.
            warnings.Add(payloadError.Resolve(localize));
        }

        var zone = TimeZoneInfo.Local;
        var scheduleText = CronDescriber.Describe(cron, localize);

        if (!string.IsNullOrWhiteSpace(payload.Summary) &&
            !SummaryAgrees(payload.Summary!, scheduleText))
        {
            // Surfaced rather than silently resolved. When the model's own words and its own
            // expression disagree, the user is the only one who knows which they meant.
            warnings.Add(localize(
                "ScheduleInterpretSummaryMismatch", payload.Summary!.Trim(), scheduleText));
        }

        var steps = new List<ScheduleDraftStep> { new(RunsLabel, scheduleText, payload.Summary) };
        var index = 1;
        foreach (var step in payload.Steps ?? [])
        {
            if (!string.IsNullOrWhiteSpace(step))
            {
                steps.Add(new ScheduleDraftStep(StepLabel(index++), step.Trim()));
            }
        }

        if (steps.Count == 1)
        {
            steps.Add(new ScheduleDraftStep(
                StepLabel(1), handler.DescribeAction(payload.Payload).Resolve(localize)));
        }

        steps.Add(new ScheduleDraftStep(
            localize("ScheduleDraftStepThen"),
            string.IsNullOrWhiteSpace(payload.Delivery)
                ? localize("ScheduleDraftDeliveryDefault")
                : payload.Delivery!.Trim()));

        foreach (var note in payload.Notes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(note))
            {
                warnings.Add(note.Trim());
            }
        }

        var draft = new ScheduleDraft
        {
            Name = string.IsNullOrWhiteSpace(payload.Name)
                ? localize(handler.DisplayNameKey)
                : payload.Name!.Trim(),
            Instruction = instruction.Trim(),
            CronExpression = cron.Expression,
            TimeZoneId = zone.Id,
            ScheduleText = scheduleText,
            JobKind = handler.JobKind,
            JobPayload = payload.Payload,
            ActionSummary = handler.DescribeAction(payload.Payload).Resolve(localize),
            Steps = steps,
            Confidence = payloadError is not null
                ? DraftConfidence.Low
                : warnings.Count > 0 ? DraftConfidence.Medium : DraftConfidence.High,
            Warnings = warnings,
            NextRunsUtc = cron.GetNextOccurrencesUtc(
                timeProvider.GetUtcNow().UtcDateTime, zone, PreviewRunCount)
        };

        return new ScheduleInterpretation(draft);
    }

    /// <summary>Builds the badge for a numbered step.</summary>
    /// <param name="position">The step's one-based position.</param>
    /// <returns>The badge, in the reader's language, with a Latin-digit position.</returns>
    private string StepLabel(int position) =>
        localize("ScheduleDraftStepNumbered", position.ToString(CultureInfo.InvariantCulture));

    private ILlmProvider? TryGetProvider()
    {
        try
        {
            return techieRag.GetLlmProvider();
        }
        catch (Exception exception)
        {
            // A provider that is configured but unusable must read as "not available", never as an
            // unhandled exception out of a dialog's first render.
            logger.LogWarning(exception, "The configured LLM provider could not be resolved");
            return null;
        }
    }

    private static TimeZoneInfo ResolveZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static bool SummaryAgrees(string summary, string scheduleText)
    {
        // A loose comparison on purpose. The point is to catch "every morning" against "every weekday
        // at 07:00", not to insist the model reproduce the describer's exact phrasing.
        var normalizedSummary = Normalize(summary);
        var normalizedSchedule = Normalize(scheduleText);
        return normalizedSummary.Contains(normalizedSchedule, StringComparison.OrdinalIgnoreCase)
               || normalizedSchedule.Contains(normalizedSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>Builds the prompt sent to the model.</summary>
    /// <param name="instruction">What the user typed, in their own words.</param>
    /// <param name="actions">The actions interpretation is constrained to.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// <b>MACHINE TEXT, deliberately not localized (REQ-UI-055).</b> Nobody reads this; the model does.
    /// Translating it would change the instruction the model is given and the JSON shape it is asked
    /// for, which is breaking the thing rather than localizing it. It is one raw string literal rather
    /// than a dozen appended sentences so that it reads as the single block of machine text it is —
    /// and so the service-layer English counter, which measures user-visible prose, does not count it
    /// as thirteen labels somebody forgot to translate.
    /// </remarks>
    private static string BuildPrompt(string instruction, IReadOnlyList<AvailableAction> actions)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            """
            You convert a person's description of a recurring task into a schedule for a desktop app.
            Reply with JSON only, matching this shape exactly:
            {"name":"","cron":"","summary":"","jobKind":"","payload":"","steps":[""],"delivery":"","notes":[""]}

            Rules:
            - cron is a five-field expression: minute hour day-of-month month day-of-week.
            - summary describes that cron expression in plain English.
            - jobKind MUST be one of the actions listed below. Never invent one.
            - payload is a JSON string of options for that action, or an empty string.
            - steps lists, in order, what the run will do, one short sentence each.
            - delivery says what happens with the result.
            - notes lists anything ambiguous in the instruction. Leave it empty if nothing is.
            - If the timing is vague, choose the most conservative reading and note it.

            Available actions:
            """);
        foreach (var action in actions)
        {
            // English on purpose: the prompt is machine text (see the remarks on BuildPrompt), and a
            // Hindi action list would change the vocabulary the model is asked to select from.
            builder.AppendLine(
                $"- {action.JobKind}: {JobMessage.Neutral(action.DisplayNameKey)} "
                + $"— {JobMessage.Neutral(action.DescriptionKey)}");
        }

        builder.AppendLine();
        builder.AppendLine("""
            Instruction:
            """);
        builder.AppendLine(instruction.Trim());
        return builder.ToString();
    }
}
