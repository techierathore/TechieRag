using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Knows which Claude models take the sampling options <c>temperature</c>, <c>top_p</c> and <c>top_k</c>,
/// so <see cref="AnthropicLlmProvider"/> leaves them out for a model that refuses them (REQ-RAG-158, Sevak TR-RAG-063).
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> Claude Fable and Mythos, Claude Opus 4.7 and later, and every Claude model of generation 5
/// and later (Sonnet, Opus, Haiku) answer a request carrying a sampling option with HTTP 400 ("temperature is
/// deprecated for this model"; Sonnet 5.5 and Haiku 5.5 refuse any value but the default). Claude Opus 4.6,
/// Sonnet 4.6, Haiku 4.5 and older take them. A model name this class does not recognise as Claude, such as
/// <c>glm-4.6</c> behind an Anthropic-compatible endpoint, keeps its sampling options.</para>
/// <para><b>Names the rule does not know yet.</b> A newer model that refuses a sampling option is caught by
/// <see cref="IsSamplingRefusal"/>: the provider retries that call once without the options and remembers the
/// model for the rest of the process, so a host can pass one option set to every provider.</para>
/// </remarks>
internal static partial class AnthropicSamplingSupport
{
    /// <summary>The request fields this class governs.</summary>
    internal static readonly string[] SamplingFields = ["temperature", "top_p", "top_k"];

    /// <summary>Models a service refused sampling options for during this process, learned from a 400.</summary>
    private static readonly ConcurrentDictionary<string, bool> LearnedRefusals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns whether a model takes the sampling options.</summary>
    /// <param name="model">The model id as sent, including a Bedrock (<c>anthropic.</c>, <c>us.anthropic.</c>) prefix or a Vertex <c>@date</c> suffix.</param>
    /// <returns><see langword="false"/> for a Claude model that refuses them, or one a service refused them for earlier; otherwise <see langword="true"/>.</returns>
    internal static bool TakesSampling(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return true;
        }

        if (LearnedRefusals.ContainsKey(model))
        {
            return false;
        }

        var match = ClaudeModelPattern().Match(model);
        if (!match.Success)
        {
            return true;
        }

        var family = match.Groups["family"].Value.ToLowerInvariant();
        if (family is "fable" or "mythos")
        {
            return false;
        }

        var major = int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var minor = match.Groups["minor"].Success
            ? int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        if (major >= 5)
        {
            return false;
        }

        return !(major == 4 && family == "opus" && minor >= 7);
    }

    /// <summary>Removes the sampling options from a request a model refuses them for.</summary>
    /// <param name="request">The request body being built.</param>
    /// <returns><see langword="true"/> when at least one option was removed.</returns>
    internal static bool RemoveSampling(Dictionary<string, object> request)
    {
        var removed = false;
        foreach (var field in SamplingFields)
        {
            removed |= request.Remove(field);
        }

        return removed;
    }

    /// <summary>Returns whether a failure is the service refusing a sampling option the request carried.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="request">The request that failed.</param>
    /// <returns><see langword="true"/> for an HTTP 400 whose message names a sampling option present in the request.</returns>
    internal static bool IsSamplingRefusal(LlmHttpException exception, Dictionary<string, object> request)
    {
        if (exception.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        var text = exception.ProviderMessage ?? exception.ResponseBody ?? string.Empty;
        return SamplingFields.Any(field => request.ContainsKey(field) && text.Contains(field, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Records that the service refused sampling options for a model, so later calls leave them out.</summary>
    /// <param name="model">The model id as sent.</param>
    internal static void Learn(string model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            LearnedRefusals[model] = true;
        }
    }

    /// <summary>Forgets every learned refusal. Test seam only.</summary>
    internal static void ForgetLearned() => LearnedRefusals.Clear();

    /// <summary>A Claude model id: <c>claude-{family}-{major}[-{minor}]</c>, anywhere in the name so platform prefixes and date suffixes pass.</summary>
    [GeneratedRegex(@"claude-(?<family>opus|sonnet|haiku|fable|mythos)-(?<major>\d{1,2})(?:-(?<minor>\d{1,2})(?!\d))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeModelPattern();
}
