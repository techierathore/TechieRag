using TechieRag.Services;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Executes one skill. The signature matches the library <c>ToolRegistry</c> delegate exactly, so a
/// skill implementation is an ordinary library tool and nothing app-specific leaks into the loop.
/// </summary>
/// <param name="argumentsJson">The tool-call arguments the model produced.</param>
/// <param name="cancellationToken">Token to cancel the execution.</param>
/// <returns>The tool result handed back to the model.</returns>
public delegate Task<string> SkillInvoker(string argumentsJson, CancellationToken cancellationToken);

/// <summary>
/// A skill the running app can actually execute: the catalogue name bound to a real tool.
/// </summary>
/// <param name="SkillName">The catalogue name from <see cref="SkillCatalog"/>.</param>
/// <param name="Description">The tool description the model sees.</param>
/// <param name="ParametersSchema">The JSON Schema for the tool's parameters.</param>
/// <param name="Invoke">The implementation.</param>
public sealed record SkillImplementation(
    string SkillName,
    string Description,
    string ParametersSchema,
    SkillInvoker Invoke);

/// <summary>
/// Turns a permitted skill set plus the implementations this install has into the library
/// <c>ToolRegistry</c> handed to the agent loop (REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>This is the enforcement point.</b> A skill the workspace catalogue forbids is never
/// registered, so it is never even offered to the model — it cannot be called by accident and there
/// is no run-time check to forget. Permission is enforced by absence, not by refusal.</para>
/// <para><b>Permitted and implemented are separate gates.</b> A permitted skill with no
/// implementation simply does not appear; it is reported through
/// <see cref="AgentSkillAvailability.Unavailable"/> in the editor instead of being faked with a
/// stub that returns plausible text.</para>
/// </remarks>
public static class AgentToolPlanner
{
    /// <summary>
    /// Builds the tool handler for one agent turn.
    /// </summary>
    /// <param name="permittedSkills">
    /// The skills the catalogue-and-agent intersection permits, from <see cref="AgentSkillResolver"/>.
    /// </param>
    /// <param name="implementations">The skills this install can execute.</param>
    /// <returns>A registry containing only the permitted, implemented skills.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static ToolRegistry BuildRegistry(
        IReadOnlyCollection<string> permittedSkills,
        IEnumerable<SkillImplementation> implementations)
    {
        ArgumentNullException.ThrowIfNull(permittedSkills);
        ArgumentNullException.ThrowIfNull(implementations);

        var permitted = new HashSet<string>(permittedSkills, StringComparer.OrdinalIgnoreCase);
        var registry = new ToolRegistry();

        foreach (var implementation in implementations)
        {
            if (!permitted.Contains(implementation.SkillName))
            {
                continue;
            }

            registry.Register(
                implementation.SkillName,
                implementation.Description,
                implementation.ParametersSchema,
                (argumentsJson, cancellationToken) =>
                    implementation.Invoke(argumentsJson, cancellationToken));
        }

        return registry;
    }

    /// <summary>
    /// Lists which of the supplied implementations correspond to real catalogue skills, so the UI
    /// can mark the rest <c>Unavailable</c> honestly.
    /// </summary>
    /// <param name="implementations">The skills this install can execute.</param>
    /// <returns>The catalogue names that have an implementation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementations"/> is null.</exception>
    public static IReadOnlyList<string> ImplementedNames(IEnumerable<SkillImplementation> implementations)
    {
        ArgumentNullException.ThrowIfNull(implementations);

        return implementations
            .Select(i => i.SkillName)
            .Where(SkillCatalog.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
