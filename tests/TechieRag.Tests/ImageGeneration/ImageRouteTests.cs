using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TechieRag.ImageGeneration;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.ImageGeneration;

/// <summary>
/// The <c>openai-compatible</c> (REQ-RAG-156 / BRD-214) and <c>azure-openai</c> (REQ-RAG-157 / BRD-215) image routes,
/// against a local stand-in answering in OpenAI's image format with a real PNG of the asked size.
/// </summary>
public sealed class ImageRouteTests
{
    private const string Key = "sk-route-test-key-0123456789";

    /// <summary>
    /// An <c>openai-compatible</c> route with an endpoint and key sends OpenAI's image request to that endpoint, with
    /// the route's model and the key as a bearer token, and returns the PNG and usage — whether the endpoint comes as
    /// an <c>@https://</c> suffix on the route or as the argument.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-156 OpenAICompatibleRouteReachesTheGivenEndpoint")]
    public async Task OpenAICompatibleRouteReachesTheGivenEndpoint()
    {
        var size = ImageSize.Square1024;
        var suffixed = new Stub(size);
        var argument = new Stub(size);

        var fromSuffix = ImageGenerationProviderFactory.CreateForModel(suffixed, "openai-compatible/flux-dev@https://images.example.com/v1", Key, null, null);
        var fromArgument = ImageGenerationProviderFactory.CreateForModel(argument, "openai-compatible/flux-dev", Key, "https://other.example.com/api", null);
        var image = await fromSuffix.GenerateImageAsync("a lighthouse", size);
        await fromArgument.GenerateImageAsync("a lighthouse", size);

        Assert.Equal("https://images.example.com/v1/images/generations", suffixed.Url);
        Assert.Equal("https://other.example.com/api/images/generations", argument.Url);
        Assert.Equal("flux-dev", suffixed.Body!["model"]!.GetValue<string>());
        Assert.Equal($"Bearer {Key}", suffixed.Authorization);
        Assert.Equal(("openai-compatible", "flux-dev"), (fromSuffix.Name, fromSuffix.ModelName));
        ImageGenerationTests.AssertPng(image, size);
        Assert.Equal(("openai-compatible", 21), (image.Usage.ProviderName, image.Usage.TotalTokens));
    }

    /// <summary>An <c>openai-compatible</c> route without an endpoint, or without a model, is refused before any request.</summary>
    [Fact(DisplayName = "REQ-RAG-156 OpenAICompatibleWithoutEndpointIsRefused")]
    public void OpenAICompatibleWithoutEndpointIsRefused()
    {
        var noEndpoint = Assert.Throws<InvalidOperationException>(() =>
            ImageGenerationProviderFactory.CreateForModel("openai-compatible/flux-dev", Key));
        Assert.Throws<InvalidOperationException>(() =>
            ImageGenerationProviderFactory.CreateForModel("openai-compatible", Key, endpoint: "https://images.example.com/v1"));

        Assert.Contains("needs an endpoint", noEndpoint.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An <c>azure-openai</c> route with an endpoint and key sends the request to that deployment with the library's
    /// api-version and the key in the <c>api-key</c> header (never as a bearer token, never in the URL), and the body
    /// names no model, because the deployment is in the URL.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-157 AzureRouteReachesTheDeploymentWithTheApiKeyHeader")]
    public async Task AzureRouteReachesTheDeployment()
    {
        var size = new ImageSize(1536, 1024);
        var stub = new Stub(size);
        var provider = ImageGenerationProviderFactory.CreateForModel(stub, "azure-openai/hero-images", Key, "https://my-resource.openai.azure.com", null);

        var image = await provider.GenerateImageAsync("a lighthouse", size, new ImageGenerationOptions { Model = "gpt-image-1" });

        Assert.Equal(
            $"https://my-resource.openai.azure.com/openai/deployments/hero-images/images/generations?api-version={AzureOpenAIImageGenerationProvider.DefaultApiVersion}",
            stub.Url);
        Assert.Equal(Key, stub.ApiKey);
        Assert.Null(stub.Authorization);
        Assert.Null(stub.Body!["model"]);
        Assert.Equal("png", stub.Body["output_format"]!.GetValue<string>());
        Assert.Equal(("azure-openai", "hero-images"), (provider.Name, provider.ModelName));
        ImageGenerationTests.AssertPng(image, size);
    }

    /// <summary>An <c>azure-openai</c> route without an endpoint or a deployment is refused before any request; a named
    /// GPT Image model's size list is applied.</summary>
    [Fact(DisplayName = "REQ-RAG-157 AzureWithoutEndpointOrDeploymentIsRefused")]
    public async Task AzureWithoutEndpointOrDeploymentIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => ImageGenerationProviderFactory.CreateForModel("azure-openai/hero-images", Key));
        Assert.Throws<InvalidOperationException>(() => ImageGenerationProviderFactory.CreateForModel("azure-openai", Key, endpoint: "https://r.openai.azure.com"));

        var stub = new Stub(ImageSize.Square1024);
        var provider = ImageGenerationProviderFactory.CreateForModel(stub, "azure-openai/hero-images@https://r.openai.azure.com", Key, null, null);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GenerateImageAsync("x", new ImageSize(800, 600), new ImageGenerationOptions { Model = "gpt-image-1" }));
        Assert.Equal(0, stub.Calls);
    }

    /// <summary>Answers every request in OpenAI's image format with a PNG of one size, and records the request.</summary>
    /// <param name="size">The PNG's size.</param>
    private sealed class Stub(ImageSize size) : HttpMessageHandler
    {
        /// <summary>Gets how many requests arrived.</summary>
        public int Calls { get; private set; }

        /// <summary>Gets the last URL.</summary>
        public string? Url { get; private set; }

        /// <summary>Gets the last body.</summary>
        public JsonNode? Body { get; private set; }

        /// <summary>Gets the last Authorization header.</summary>
        public string? Authorization { get; private set; }

        /// <summary>Gets the last api-key header.</summary>
        public string? ApiKey { get; private set; }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            ApiKey = request.Headers.TryGetValues("api-key", out var values) ? values.Single() : null;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var answer = new JsonObject
            {
                ["data"] = new JsonArray(new JsonObject { ["b64_json"] = Convert.ToBase64String(ImageGenerationTests.Png(size)) }),
                ["usage"] = new JsonObject { ["input_tokens"] = 5, ["output_tokens"] = 16, ["total_tokens"] = 21 }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
