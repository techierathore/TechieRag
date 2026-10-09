using System.Globalization;
using System.Text;
using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>Options for a <see cref="ModelChooser"/>.</summary>
public sealed class ModelChooserOptions
{
    /// <summary>Gets or sets how much of the request the small model reads; the rest is cut. Default 4000 characters.</summary>
    public int MaxRequestCharacters { get; set; } = 4000;

    /// <summary>
    /// Gets or sets the candidate used when the small model's reply names no candidate; null, or an id
    /// that is not a candidate, uses the first candidate.
    /// </summary>
    public string? FallbackModelId { get; set; }

    /// <summary>
    /// Gets or sets the host's own rule for choosing, added to the small model's instructions, such as
    /// "Prefer free models unless the work needs deep reasoning."; null adds nothing.
    /// </summary>
    public string? Guidance { get; set; }

    /// <summary>Gets or sets the most tokens the small model may answer with. Default 200.</summary>
    public int MaxTokens { get; set; } = 200;
}

/// <summary>
/// Lets a small model choose which of several models does a piece of work (REQ-RAG-128 / BRD-188,
/// Chatur feedback TR-RAG-006).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="ModelRouter"/> turns a model <i>name</i> into a service; this
/// class decides <i>which</i> name, from the content of the request. A host passes a cheap or local
/// model and the candidates with their tier, notes and cost, and gets one candidate back with a
/// reason it can show and log.</para>
/// <para><b>Code Flow:</b> one candidate is returned without asking any model. Otherwise the small
/// model gets one chat call at temperature 0 in JSON mode, listing the candidates and the request
/// (cut to <see cref="ModelChooserOptions.MaxRequestCharacters"/>), and answers
/// <c>{"model": "&lt;id&gt;", "reason": "&lt;one sentence&gt;"}</c>. The id is matched to a
/// candidate ignoring case; an id without its <c>connector/</c> prefix matches when exactly one
/// candidate ends with it.</para>
/// <para><b>The answer is always a candidate.</b> A reply that is not JSON, or names a model that was
/// not offered, gives the fallback candidate with <see cref="ModelChoiceSource.Fallback"/> and a
/// reason saying what the small model answered. A failure of the small model itself (unreachable,
/// refused, cancelled) is thrown, so a host never mistakes an outage for a choice; catch it to fall
/// back to the host's own rule.</para>
/// </remarks>
public sealed class ModelChooser : IModelChooser
{
    private const string Instructions =
        "You choose which AI model should handle a request. You are given the candidate models and the request. "
        + "Pick the one candidate that will do this request well at the lowest cost: a simple request needs no strong model. "
        + "Answer with JSON only, in this shape: {\"model\": \"<one candidate id exactly as listed>\", \"reason\": \"<one short sentence>\"}.";

    private readonly ILlmProvider smallModel;
    private readonly ModelChooserOptions options;

    /// <summary>Creates a chooser that asks the given small model.</summary>
    /// <param name="smallModel">The model that chooses; a cheap or local one is enough.</param>
    /// <param name="options">Options; null uses the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="smallModel"/> is null.</exception>
    public ModelChooser(ILlmProvider smallModel, ModelChooserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(smallModel);
        this.smallModel = smallModel;
        this.options = options ?? new ModelChooserOptions();
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The request is blank, or there are no candidates, a blank
    /// id, or the same id twice.</exception>
    public async Task<ModelChoice> ChooseAsync(
        string request,
        IReadOnlyList<ModelCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateCandidates(candidates);

        if (candidates.Count == 1)
        {
            return new ModelChoice(candidates[0], "Only one model was offered.", ModelChoiceSource.OnlyCandidate, null);
        }

        var messages = new[]
        {
            ChatMessage.System(BuildInstructions()),
            ChatMessage.User(BuildPrompt(request, candidates))
        };
        var completion = new LlmCompletionOptions
        {
            Temperature = 0,
            MaxTokens = options.MaxTokens,
            JsonMode = true
        };

        var response = await smallModel.ChatAsync(messages, completion, cancellationToken).ConfigureAwait(false);
        return ReadChoice(response.Content, candidates, response.Usage);
    }

    /// <summary>Turns the small model's reply into a choice, falling back when it names no candidate.</summary>
    /// <param name="reply">The reply text.</param>
    /// <param name="candidates">The candidates offered.</param>
    /// <param name="usage">The call's token usage.</param>
    /// <returns>The choice.</returns>
    private ModelChoice ReadChoice(string? reply, IReadOnlyList<ModelCandidate> candidates, TokenUsage? usage)
    {
        var (modelId, reason) = ParseReply(reply);
        var chosen = modelId is null ? null : MatchCandidate(modelId.Trim(), candidates);

        if (chosen is not null)
        {
            var text = string.IsNullOrWhiteSpace(reason) ? "The small model gave no reason." : reason.Trim();
            return new ModelChoice(chosen, text, ModelChoiceSource.SmallModel, usage);
        }

        var fallback = candidates.FirstOrDefault(c => string.Equals(c.ModelId, options.FallbackModelId, StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];
        var why = modelId is null
            ? "The small model's reply named no model, so the fallback model was used."
            : $"The small model named '{modelId}', which was not offered, so the fallback model was used.";
        return new ModelChoice(fallback, why, ModelChoiceSource.Fallback, usage);
    }

    /// <summary>
    /// Finds the candidate a reply names: the same id ignoring case, else the one candidate whose id is
    /// the reply after a <c>connector/</c> prefix (small models often drop it).
    /// </summary>
    /// <param name="modelId">The id the reply named.</param>
    /// <param name="candidates">The candidates offered.</param>
    /// <returns>The candidate, or null when none or more than one fits.</returns>
    private static ModelCandidate? MatchCandidate(string modelId, IReadOnlyList<ModelCandidate> candidates)
    {
        var exact = candidates.FirstOrDefault(c => string.Equals(c.ModelId.Trim(), modelId, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var suffix = candidates
            .Where(c => c.ModelId.Trim().EndsWith("/" + modelId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return suffix.Count == 1 ? suffix[0] : null;
    }

    private static void ValidateCandidates(IReadOnlyList<ModelCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one candidate model is needed.", nameof(candidates));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.ModelId))
            {
                throw new ArgumentException("Every candidate needs a model id.", nameof(candidates));
            }

            if (!seen.Add(candidate.ModelId.Trim()))
            {
                throw new ArgumentException($"The model id '{candidate.ModelId}' is offered twice.", nameof(candidates));
            }
        }
    }

    private static (string? ModelId, string? Reason) ParseReply(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return (null, null);

        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return (null, null);

        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            return (ReadString(root, "model", "modelId", "model_id"), ReadString(root, "reason"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return null;
    }

    private string BuildInstructions() =>
        string.IsNullOrWhiteSpace(options.Guidance)
            ? Instructions
            : Instructions + " The application's own rule: " + options.Guidance.Trim();

    private string BuildPrompt(string request, IReadOnlyList<ModelCandidate> candidates)
    {
        var builder = new StringBuilder("Candidate models:\n");
        foreach (var candidate in candidates)
        {
            builder.Append("- id: ").Append(candidate.ModelId.Trim());
            AppendField(builder, "tier", candidate.Tier);
            AppendField(builder, "cost", candidate.Cost);
            AppendField(builder, "notes", candidate.Notes);
            builder.Append('\n');
        }

        var limit = Math.Max(1, options.MaxRequestCharacters);
        var text = request.Length <= limit
            ? request
            : request[..limit] + string.Create(CultureInfo.InvariantCulture, $"\n[cut: {request.Length - limit} more characters]");
        builder.Append("\nRequest:\n").Append(text);
        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) builder.Append("; ").Append(name).Append(": ").Append(value.Trim());
    }
}
