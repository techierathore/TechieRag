using System.Collections;
using System.Reflection;

namespace TechieRag.DependencyInjection;

/// <summary>
/// Refuses keys found in a bound <c>TechieRag</c> configuration section (REQ-FN-066 / BRD-20, BRD-159).
/// </summary>
/// <remarks>
/// <para><b>Rule:</b> keys and dependencies are injected only through code (owner decision 2026-10-01).
/// A key is every <c>ApiKey</c> property and every <c>Headers</c> dictionary in the configuration tree;
/// walking the tree by reflection means a key field added later is refused without editing this list.</para>
/// <para><b>Why refuse instead of ignore:</b> a key left in appsettings that silently does nothing is
/// harder to find than a startup error naming it. The message names the setting, never its value.</para>
/// </remarks>
internal static class ConfigurationKeyGuard
{
    /// <summary>
    /// Throws when <paramref name="config"/>, bound from <c>IConfiguration</c>, holds any key.
    /// </summary>
    /// <param name="config">The configuration bound from the section.</param>
    /// <exception cref="InvalidOperationException">One or more keys are set; the message lists their paths.</exception>
    internal static void ThrowIfKeysPresent(TechieRagConfig config)
    {
        var found = new List<string>();
        Collect(config, string.Empty, found);
        if (found.Count == 0) return;

        throw new InvalidOperationException(
            $"TechieRag does not read keys from configuration. Remove {string.Join(", ", found)} from the section and pass "
            + "them in code: services.AddTechieRag(section, rag => rag.WithApiKeys(llm: llmKey)) (headers: WithLlmHeaders), "
            + "or build the configuration in code with AddTechieRag(TechieRagConfig).");
    }

    private static void Collect(object? section, string prefix, List<string> found)
    {
        if (section is null) return;

        foreach (var property in section.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;

            var path = prefix + property.Name;
            var value = property.GetValue(section);
            if (property.Name == "ApiKey" && value is string key && !string.IsNullOrEmpty(key))
            {
                found.Add(path);
            }
            else if (property.Name == "Headers" && value is IDictionary { Count: > 0 })
            {
                found.Add(path);
            }
            else if (IsSection(property.PropertyType))
            {
                Collect(value, path + ":", found);
            }
        }
    }

    private static bool IsSection(Type type) =>
        type.IsClass && type.Namespace == "TechieRag" && type.Name.EndsWith("Config", StringComparison.Ordinal);
}
