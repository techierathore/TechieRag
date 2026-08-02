namespace TechieDesk.Services.Agents;

/// <summary>
/// Enforces <see cref="AgentDefinition.ConfirmEgress"/> at the moment a skill would actually leave
/// the machine (REQ-NFR-013).
/// </summary>
/// <remarks>
/// <para><b>The defect this closes.</b> <see cref="AgentDefinition.ConfirmEgress"/> was declared,
/// persisted and bound to the switch <see cref="EgressWording.ConfirmEgressSettingKey"/> labels, and
/// read by no execution path. The switch defaulted ON and promised something the product never did.
/// This type is the reader.</para>
/// <para><b>Keyed off exposure, not off names.</b> The gate asks
/// <see cref="SkillCatalog"/> whether a skill is <see cref="SkillExposure.LeavesMachine"/>. A new
/// egress-capable skill therefore inherits the gate the moment it is added to the catalogue with the
/// right exposure. A hard-coded list of skill names is how this class of defect comes back.</para>
/// <para><b>Enforced at execution, not at registration.</b> A gated skill stays registered and stays
/// visible to the model — it is the OUTBOUND CALL that blocks. Removing it from the registry would
/// silently change what the model can see, and the agent could not then report the skill as
/// unavailable and carry on with the rest of the turn.</para>
/// <para><b>Once per turn.</b> The decision is taken on the first egress attempt and reused for the
/// rest of the turn, in both directions. Re-prompting on every tool call inside one turn trains the
/// user to click through, which is worse than not asking.</para>
/// <para><b>Fail closed.</b> With confirmation required and no <see cref="IEgressConfirmation"/>
/// supplied, every egress skill is denied. A host that forgets to wire the dialog gets a visibly
/// blocked skill, not silent egress.</para>
/// </remarks>
public sealed class EgressGate
{
    private readonly bool isConfirmationRequired;
    private readonly IEgressConfirmation? confirmation;
    private readonly string agentDisplayName;
    private readonly object decisionLock = new();
    private Task<bool>? decision;

    /// <summary>
    /// Creates the gate governing one agent turn.
    /// </summary>
    /// <param name="agent">
    /// The agent answering the turn. Its own <see cref="AgentDefinition.ConfirmEgress"/> decides,
    /// so the per-agent editor value governs exactly as the built-in agent's does.
    /// </param>
    /// <param name="confirmation">
    /// How to ask the user, or null when this host cannot ask — in which case egress is denied
    /// while confirmation is required.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agent"/> is null.</exception>
    public EgressGate(AgentDefinition agent, IEgressConfirmation? confirmation)
    {
        ArgumentNullException.ThrowIfNull(agent);

        isConfirmationRequired = agent.ConfirmEgress;
        this.confirmation = confirmation;
        agentDisplayName = string.IsNullOrWhiteSpace(agent.DisplayName)
            ? "@" + agent.Handle
            : agent.DisplayName;
    }

    /// <summary>Gets whether this turn's agent asks before anything leaves the machine.</summary>
    public bool IsConfirmationRequired => isConfirmationRequired;

    /// <summary>
    /// Gets whether a catalogue skill sends something off this machine.
    /// </summary>
    /// <param name="skillName">The catalogue skill name.</param>
    /// <returns>True when the catalogue marks it <see cref="SkillExposure.LeavesMachine"/>.</returns>
    public static bool LeavesMachine(string? skillName) =>
        SkillCatalog.Find(skillName)?.Exposure == SkillExposure.LeavesMachine;

    /// <summary>
    /// Wraps a turn's skill implementations so the ones that leave the machine ask first.
    /// </summary>
    /// <param name="implementations">The implementations composed for this turn.</param>
    /// <returns>
    /// The same skills in the same order. Local skills are returned untouched; egress skills are
    /// returned with their invoker wrapped. With confirmation off, nothing is wrapped at all — the
    /// agent proceeds silently, which is the whole point of the setting being off.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementations"/> is null.</exception>
    public IReadOnlyList<SkillImplementation> Guard(IEnumerable<SkillImplementation> implementations)
    {
        ArgumentNullException.ThrowIfNull(implementations);

        return implementations.Select(Wrap).ToList();
    }

    /// <summary>
    /// Decides whether this turn may make an outbound request, asking the user at most once.
    /// </summary>
    /// <param name="skillName">The skill that wants to go out, named in the prompt.</param>
    /// <param name="cancellationToken">Token cancelled when the turn's time limit expires.</param>
    /// <returns>True when the call may proceed.</returns>
    public Task<bool> AllowAsync(string skillName, CancellationToken cancellationToken)
    {
        if (!isConfirmationRequired || !LeavesMachine(skillName))
        {
            return Task.FromResult(true);
        }

        if (confirmation is null)
        {
            return Task.FromResult(false);
        }

        // Started under the lock but never awaited under it, so the first caller in the turn owns
        // the prompt and every later caller awaits that same answer rather than raising a second one.
        lock (decisionLock)
        {
            return decision ??= AskAsync(skillName, cancellationToken);
        }
    }

    /// <summary>
    /// Decides whether this turn may call an outbound capability that is not a catalogue skill —
    /// today, a tool hosted by an HTTP MCP server (REQ-RAG-023).
    /// </summary>
    /// <param name="toolName">The tool name as the model sees it, used only for the trace.</param>
    /// <param name="displayName">What to call it in the prompt.</param>
    /// <param name="description">One line explaining what running it will do.</param>
    /// <param name="cancellationToken">Token cancelled when the turn's time limit expires.</param>
    /// <returns>True when the call may proceed.</returns>
    /// <remarks>
    /// <para><b>Why the caller supplies the exposure.</b> <see cref="AllowAsync"/> asks
    /// <see cref="SkillCatalog"/> whether a name leaves the machine, which is right for the six
    /// catalogue skills and useless for an MCP tool — the catalogue does not and should not know
    /// about servers an administrator registered at run time. The exposure fact for an MCP tool is
    /// its server's TRANSPORT: an <c>http</c> server is off this machine by definition. The caller
    /// that knows the transport calls this; nothing here guesses from a name.</para>
    /// <para><b>The decision is the same decision.</b> It is taken once per turn and SHARED with
    /// <see cref="AllowAsync"/>, so a turn that already asked about web search does not ask again
    /// about an MCP tool, in either order. Two prompts in one turn is how a user learns to click
    /// through them.</para>
    /// <para><b>Fail closed, identically.</b> Confirmation required with no
    /// <see cref="IEgressConfirmation"/> supplied denies.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="toolName"/> is blank.</exception>
    public Task<bool> AllowExternalAsync(
        string toolName, string displayName, string description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (!isConfirmationRequired)
        {
            return Task.FromResult(true);
        }

        if (confirmation is null)
        {
            return Task.FromResult(false);
        }

        var request = new EgressConfirmationRequest(
            toolName,
            string.IsNullOrWhiteSpace(displayName) ? toolName : displayName,
            description ?? string.Empty,
            agentDisplayName);

        lock (decisionLock)
        {
            return decision ??= AskAsync(request, cancellationToken);
        }
    }

    /// <summary>Wraps one implementation when, and only when, it needs the gate.</summary>
    /// <param name="implementation">The implementation composed for this turn.</param>
    /// <returns>The original, or a gated copy.</returns>
    private SkillImplementation Wrap(SkillImplementation implementation)
    {
        if (!isConfirmationRequired || !LeavesMachine(implementation.SkillName))
        {
            return implementation;
        }

        var inner = implementation.Invoke;
        var name = implementation.SkillName;

        return implementation with
        {
            Invoke = async (argumentsJson, cancellationToken) =>
            {
                var isAllowed = await AllowAsync(name, cancellationToken).ConfigureAwait(false);

                return isAllowed
                    ? await inner(argumentsJson, cancellationToken).ConfigureAwait(false)
                    : Refusal(name);
            }
        };
    }

    /// <summary>Asks the user once, treating a cancelled prompt as a refusal.</summary>
    /// <param name="skillName">The skill that wants to go out.</param>
    /// <param name="cancellationToken">Token cancelled when the turn's time limit expires.</param>
    /// <returns>The user's answer.</returns>
    private Task<bool> AskAsync(string skillName, CancellationToken cancellationToken)
    {
        var definition = SkillCatalog.Find(skillName);
        return AskAsync(
            new EgressConfirmationRequest(
                skillName,
                skillName,
                string.Empty,
                agentDisplayName,
                definition?.DisplayNameKey,
                definition?.DescriptionKey),
            cancellationToken);
    }

    /// <summary>Asks the user once, treating a cancelled prompt as a refusal.</summary>
    /// <param name="request">What is about to leave the machine, and on whose behalf.</param>
    /// <param name="cancellationToken">Token cancelled when the turn's time limit expires.</param>
    /// <returns>The user's answer.</returns>
    private async Task<bool> AskAsync(
        EgressConfirmationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await confirmation!.ConfirmAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds what a blocked skill hands back to the model.
    /// </summary>
    /// <param name="skillName">The skill that was blocked.</param>
    /// <returns>An <see cref="SkillUnavailable"/> report naming the skill and how to change it.</returns>
    /// <remarks>
    /// <para>It is deliberately the <c>unavailable:</c> channel and not an exception. A throw ends the
    /// turn; this lets the loop record the block in the trace, tell the model the skill could not
    /// run, and finish the answer with whatever it can do locally.</para>
    /// <para><b>REQ-UI-055: this is MODEL-facing text and stays English.</b> It is returned as the
    /// skill's result, so <c>AgentLoopRunner</c> puts it straight into the conversation the LLM
    /// reasons over. Translating it would put a Hindi sentence in an English context — which
    /// measurably degrades tool calling and invites the model to answer in the wrong language — and
    /// would also make the execution trace, which renders a tool result verbatim, stop being a
    /// faithful record of what the model was actually told. The USER-facing half of this control is
    /// the confirmation dialog, which is fully localized through
    /// <see cref="EgressConfirmationRequest.DisplayNameKey"/>.</para>
    /// <para><b>The switch is QUOTED, not re-typed.</b> The label comes from
    /// <see cref="EgressWording.ConfirmEgressSettingKey"/> — the same resource entry the Guardrails
    /// tab binds — resolved in English because the model is the reader. It used to be a second copy
    /// of that sentence typed into this file, which is how a control's promise and the text
    /// describing it drift apart; REQ-NFR-013 was raised over exactly that class of disagreement.</para>
    /// </remarks>
    private string Refusal(string skillName) => SkillUnavailable.Because(
        confirmation is null
            ? $"'{skillName}' sends a request off this machine and there is no way to ask for "
                + "approval here, so nothing was sent. Turn off \""
                + EgressWording.InEnglish(EgressWording.ConfirmEgressSettingKey)
                + "\" for this agent to allow it."
            : $"'{skillName}' sends a request off this machine and approval was declined, so "
                + "nothing was sent. Answer from what is available locally, or say the request was "
                + "not approved.");
}
