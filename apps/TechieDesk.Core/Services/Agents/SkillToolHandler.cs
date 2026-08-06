using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Runs this install's skills as library tools, carrying each result's two audiences
/// (REQ-RAG-022 / REQ-UI-059 clause 3).
/// </summary>
/// <remarks>
/// <para><b>Why not the library's <c>ToolRegistry</c>.</b> Its delegate returns a bare
/// <see cref="string"/>, which is exactly enough for a tool result the model reads and one short of
/// what a trace needs. The <c>unavailable: …</c> sentences are written by the PRODUCT, and the UI
/// renders them verbatim to a person — so they must exist in the reader's language while what the
/// model is told stays invariant English. One string cannot be both. This handler is that seam, and
/// it lives here because the audience split is the app's problem, not the library's.</para>
/// <para><b>It is otherwise an ordinary <see cref="IToolHandler"/>.</b> The agent loop dispatches to
/// it unchanged, and the permission model is unchanged too: a skill the workspace forbids was never
/// passed in, so it cannot be called by accident.</para>
/// </remarks>
public sealed class SkillToolHandler : IToolHandler
{
    private readonly Dictionary<string, SkillImplementation> skills;

    /// <summary>Creates a handler over the permitted, implemented skills.</summary>
    /// <param name="implementations">The skills this turn may run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementations"/> is null.</exception>
    public SkillToolHandler(IEnumerable<SkillImplementation> implementations)
    {
        ArgumentNullException.ThrowIfNull(implementations);

        skills = implementations.ToDictionary(
            implementation => implementation.SkillName, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions =>
        skills.Values
            .Select(skill => new ToolDefinition
            {
                Name = skill.SkillName,
                Description = skill.Description,
                ParametersSchema = skill.ParametersSchema
            })
            .ToList();

    /// <summary>Gets how many skills this handler exposes.</summary>
    public int Count => skills.Count;

    /// <summary>Gets whether a named skill is exposed.</summary>
    /// <param name="skillName">The catalogue skill name.</param>
    /// <returns>True when the skill can be called through this handler.</returns>
    public bool Contains(string skillName) => skills.ContainsKey(skillName);

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(
        ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!skills.TryGetValue(toolCall.Name, out var skill))
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                // Wording kept aligned with the library registry this replaced: a skill the workspace
                // does not permit is NOT REGISTERED for this turn, and callers (and tests) key off that.
                ErrorMessage = $"Tool '{toolCall.Name}' is not registered for this agent"
            };
        }

        try
        {
            var outcome = await skill.Invoke(toolCall.ArgumentsJson ?? "{}", cancellationToken)
                .ConfigureAwait(false);

            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                // The model reads this, and it stays invariant English.
                Content = outcome.Text,
                // A person reads this, in their own language, when the trace renders the row.
                Message = outcome.Message
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same contract every other handler keeps: a failure is a message the model can read and
            // work around, not an exception that ends the turn.
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error executing tool: {ex.Message}",
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
