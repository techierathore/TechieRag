using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechieDesk.Services.Setup;

/// <summary>
/// Default <see cref="IOllamaProbe"/>: a short-timeout HTTP call to <c>/api/tags</c>
/// (REQ-FN-016). Every failure path is swallowed and logged at debug so the wizard
/// can offer the embedded/offline fallback without crashing.
/// </summary>
public sealed class OllamaProbe : IOllamaProbe
{
    private readonly HttpClient httpClient;
    private readonly ILogger<OllamaProbe> logger;

    /// <summary>Initializes the probe.</summary>
    /// <param name="httpClient">The (typed) HTTP client used for the probe.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public OllamaProbe(HttpClient httpClient, ILogger<OllamaProbe> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<OllamaProbeResult> ProbeAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(endpoint) ? IOllamaProbe.DefaultEndpoint : endpoint.Trim();

        try
        {
            var url = $"{target.TrimEnd('/')}/api/tags";

            // A local probe should fail fast — never block the wizard for long.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            using var response = await httpClient.GetAsync(url, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Ollama probe at {Url} returned {Status}", url, (int)response.StatusCode);
                return new OllamaProbeResult(false, target, Array.Empty<string>());
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<OllamaTagsResponse>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                timeout.Token).ConfigureAwait(false);

            var models = payload?.Models?
                .Select(m => m.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList() ?? new List<string>();

            logger.LogInformation("Ollama detected at {Endpoint} with {Count} model(s)", target, models.Count);
            return new OllamaProbeResult(true, target, models);
        }
        catch (Exception ex)
        {
            // Connection refused / timeout / DNS / malformed body all land here — degrade quietly.
            logger.LogDebug(ex, "Ollama not detected at {Endpoint}", target);
            return new OllamaProbeResult(false, target, Array.Empty<string>());
        }
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaTag>? Models { get; set; }
    }

    private sealed class OllamaTag
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
