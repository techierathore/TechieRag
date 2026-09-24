using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechieRag.Abstractions;
using TechieRag.DependencyInjection;
using TechieRag.Models;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.DependencyInjection;

/// <summary>
/// REQ-FN-066 / BRD-159: <c>AddTechieRag(IConfiguration)</c> and <c>AddTechieRag(TechieRagConfig)</c>
/// map every configuration field the builder accepts.
/// </summary>
/// <remarks>
/// <para>The field list is not written out by hand. <see cref="LeafPaths"/> walks every public settable
/// property of <see cref="TechieRagConfig"/> by reflection, <see cref="Populate"/> sets each one to a
/// non-default value, and the registered configuration is compared field by field. A property added to
/// any section later is picked up automatically: it either survives the mapping or fails these tests
/// with its path in the message. A property of a type the walker does not know fails
/// <see cref="EveryConfigurationFieldHasAKnownShape"/> so the walker is extended rather than
/// skipping it.</para>
/// </remarks>
public class TechieRagConfigMappingTests
{
    private static readonly HashSet<Type> LeafTypes =
    [
        typeof(string), typeof(int), typeof(long), typeof(float), typeof(decimal), typeof(bool),
        typeof(Dictionary<string, ModelPricing>)
    ];

    /// <summary>
    /// Every field of the configuration tree is a type the reflection walker can populate, so none is
    /// silently left out of the mapping guard.
    /// </summary>
    [Fact]
    public void EveryConfigurationFieldHasAKnownShape()
    {
        var unknown = LeafPaths()
            .Where(leaf => !IsKnownLeaf(leaf.Property.PropertyType))
            .Select(leaf => $"{leaf.Path} ({leaf.Property.PropertyType.Name})")
            .ToList();

        Assert.True(unknown.Count == 0, "Extend the mapping guard for: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// A fully populated <see cref="TechieRagConfig"/> passed to <c>AddTechieRag(TechieRagConfig)</c>
    /// arrives in the registered configuration with every field intact.
    /// </summary>
    [Fact]
    public void ConfigObjectOverloadMapsEveryField()
    {
        var source = Populate();

        var registered = RegisteredConfig(services => services.AddTechieRag(source));

        AssertEveryFieldEqual(source, registered);
    }

    /// <summary>
    /// The same fully populated configuration, written out as appsettings keys and passed to
    /// <c>AddTechieRag(IConfiguration)</c>, arrives in the registered configuration with every field
    /// intact.
    /// </summary>
    [Fact]
    public void ConfigurationSectionOverloadMapsEveryField()
    {
        var source = Populate();
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(ToSettings(source))
            .Build();

        var registered = RegisteredConfig(services => services.AddTechieRag(section));

        AssertEveryFieldEqual(source, registered);
    }

    /// <summary>
    /// The acceptance case: <c>VectorStore.ApiKey</c> and <c>Prompt.SystemPrompt</c> set in appsettings
    /// reach the built instance, whose Qdrant store carries the key and whose prompt template writes
    /// the configured system prompt.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-066 AppSettingsApiKeyAndSystemPromptReachBuiltInstance")]
    public void AppSettingsApiKeyAndSystemPromptReachBuiltInstance()
    {
        var section = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VectorStore:Type"] = "Qdrant",
            ["VectorStore:ConnectionString"] = "http://localhost:6334",
            ["VectorStore:ApiKey"] = "qdrant-key-from-appsettings",
            ["Prompt:SystemPrompt"] = "You answer only from the Sevak handbook."
        }).Build();
        var services = new ServiceCollection();
        services.AddTechieRag(section);

        using var provider = services.BuildServiceProvider();
        var client = Assert.IsType<TechieRagClient>(provider.GetRequiredService<ITechieRag>());

        var clientConfig = PrivateField<TechieRagConfig>(client, "config");
        var prompt = PrivateField<IPromptTemplate>(client, "promptTemplate").BuildRagPrompt("q", []);
        Assert.IsType<QdrantStore>(PrivateField<IVectorStore>(client, "vectorStore"));
        Assert.Equal("qdrant-key-from-appsettings", clientConfig.VectorStore.ApiKey);
        Assert.Contains("You answer only from the Sevak handbook.", prompt[0].Content);
    }

    /// <summary>
    /// An API reranker configured without its key stays off instead of making the instance throw on
    /// resolution, as it did before the mapping was completed.
    /// </summary>
    [Fact]
    public void RerankerWithoutApiKeyStaysOff()
    {
        var source = new TechieRagConfig { Rerank = new RerankConfig { Enabled = true, Source = RerankSource.Cohere } };

        var registered = RegisteredConfig(services => services.AddTechieRag(source));

        Assert.False(registered.Rerank.Enabled);
    }

    private static TechieRagConfig RegisteredConfig(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<TechieRagConfig>();
    }

    private static void AssertEveryFieldEqual(TechieRagConfig expected, TechieRagConfig actual)
    {
        var leaves = LeafPaths().ToList();
        Assert.True(leaves.Count > 50, $"Only {leaves.Count} fields found; the walker lost a section.");

        foreach (var leaf in leaves)
        {
            var want = Describe(ReadPath(expected, leaf.Path));
            var got = Describe(ReadPath(actual, leaf.Path));
            Assert.True(want == got, $"{leaf.Path} was not mapped: expected {want}, got {got}.");
        }
    }

    private static IEnumerable<(string Path, PropertyInfo Property)> LeafPaths(Type? type = null, string prefix = "")
    {
        foreach (var property in (type ?? typeof(TechieRagConfig)).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.SetMethod?.IsPublic != true)
            {
                continue;
            }

            var path = prefix + property.Name;
            if (IsSection(property.PropertyType))
            {
                foreach (var nested in LeafPaths(property.PropertyType, path + ":"))
                {
                    yield return nested;
                }
                continue;
            }

            yield return (path, property);
        }
    }

    private static bool IsSection(Type type) => type.IsClass && type.Namespace == "TechieRag" && type.Name.EndsWith("Config", StringComparison.Ordinal);

    private static bool IsKnownLeaf(Type type) => type.IsEnum || LeafTypes.Contains(Nullable.GetUnderlyingType(type) ?? type);

    private static TechieRagConfig Populate()
    {
        var config = new TechieRagConfig { LlmFallback = new LlmConfig() };
        var index = 0;
        foreach (var leaf in LeafPaths())
        {
            index++;
            var owner = OwnerOf(config, leaf.Path);
            leaf.Property.SetValue(owner, SentinelFor(leaf.Property, leaf.Property.GetValue(owner), leaf.Path, index));
        }
        return config;
    }

    private static object SentinelFor(PropertyInfo property, object? current, string path, int index)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type switch
        {
            _ when type == typeof(RerankSource) => RerankSource.Cohere,
            _ when type.IsEnum => Enum.GetValues(type).Cast<object>().Last(value => !value.Equals(current)),
            _ when type == typeof(string) => path.Contains("ConnectionString") && path.StartsWith("VectorStore") ? "http://localhost:6334" : $"set-{path.Replace(':', '-')}",
            _ when type == typeof(int) => 1000 + index,
            _ when type == typeof(long) => 500000L + index,
            _ when type == typeof(float) => 0.25f + (index / 100f),
            _ when type == typeof(decimal) => 12.5m + index,
            _ when type == typeof(bool) => !(bool)current!,
            _ when type == typeof(Dictionary<string, ModelPricing>) => new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
            {
                ["sentinel-model"] = new ModelPricing { InputPerMillionUsd = 1.5m, OutputPerMillionUsd = 2.5m }
            },
            _ => throw new InvalidOperationException($"No sentinel for {path} ({type.Name}); extend the mapping guard.")
        };
    }

    private static Dictionary<string, string?> ToSettings(TechieRagConfig config)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var leaf in LeafPaths())
        {
            var value = ReadPath(config, leaf.Path);
            if (value is IDictionary pricing)
            {
                foreach (DictionaryEntry entry in pricing)
                {
                    var price = (ModelPricing)entry.Value!;
                    settings[$"{leaf.Path}:{entry.Key}:InputPerMillionUsd"] = price.InputPerMillionUsd.ToString(CultureInfo.InvariantCulture);
                    settings[$"{leaf.Path}:{entry.Key}:OutputPerMillionUsd"] = price.OutputPerMillionUsd.ToString(CultureInfo.InvariantCulture);
                }
                continue;
            }
            settings[leaf.Path] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        return settings;
    }

    private static object OwnerOf(TechieRagConfig config, string path)
    {
        object owner = config;
        var segments = path.Split(':');
        foreach (var segment in segments[..^1])
        {
            owner = owner.GetType().GetProperty(segment)!.GetValue(owner)!;
        }
        return owner;
    }

    private static object? ReadPath(TechieRagConfig config, string path)
    {
        var owner = OwnerOf(config, path);
        return owner.GetType().GetProperty(path.Split(':')[^1])!.GetValue(owner);
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        IDictionary dictionary => JsonSerializer.Serialize(dictionary),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!
    };

    private static T PrivateField<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{instance.GetType().Name} has no field '{name}'."))
            .GetValue(instance)!;
}
