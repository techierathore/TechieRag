using System.Net;
using System.Net.Sockets;
using System.Text;
using TechieRag.Llm;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// REQ-RAG-166 (Lekhak TR-RAG-011): the model list carries, per model, what the service publishes about it: the
/// price per million tokens in and out, whether the service runs the model itself or passes the text on, and
/// whether it reasons; each empty when the service says nothing.
/// </summary>
/// <remarks>
/// The bodies are cut from the services' own public model lists, read on 2026-11-19
/// (https://api.venice.ai/api/v1/models, https://openrouter.ai/api/v1/models), with only the fields that matter
/// kept. Before this, <c>ListModelsAsync</c> returned ids only and there was no call that returned more.
/// </remarks>
public sealed class ModelDetailsTests
{
    private const string VeniceBody = """
        {"object":"list","type":"text","data":[
          {"id":"z-ai-glm-5-3","type":"text","object":"model","owned_by":"venice.ai",
           "model_spec":{"pricing":{"input":{"usd":1.75,"diem":1.75},"cache_input":{"usd":0.325,"diem":0.325},"output":{"usd":5.5,"diem":5.5}},
             "capabilities":{"supportsReasoning":true,"supportsVision":false},"privacy":"private","name":"GLM 5.3"}},
          {"id":"gemini-4-carbon","type":"text","object":"model","owned_by":"venice.ai",
           "model_spec":{"pricing":{"input":{"usd":2.5,"diem":2.5},"output":{"usd":12.5,"diem":12.5}},
             "capabilities":{"supportsReasoning":true},"privacy":"anonymized","name":"Gemini 4 Carbon"}},
          {"id":"venice-uncensored-1-2","type":"text","object":"model","owned_by":"venice.ai",
           "model_spec":{"pricing":{"input":{"usd":0.2},"output":{"usd":0.9}},
             "capabilities":{"supportsReasoning":false},"privacy":"private"}}
        ]}
        """;

    private const string OpenRouterBody = """
        {"data":[
          {"id":"google/gemini-4-neon","name":"Google: Gemini 4 Neon",
           "pricing":{"prompt":"0.000003","completion":"0.000015","internal_reasoning":"0.000015"},
           "supported_parameters":["include_reasoning","max_tokens","reasoning","temperature","tools"]},
          {"id":"meta-llama/llama-3.3-8b-instruct:free","pricing":{"prompt":"0","completion":"0"},
           "supported_parameters":["max_tokens","temperature"]},
          {"id":"openrouter/auto","pricing":{"prompt":"-1","completion":"-1"}}
        ]}
        """;

    /// <summary>
    /// Venice's list gives each model's dollar price per million tokens in and out, "private" as runs it itself,
    /// "anonymized" as passes the text on, and its reasoning flag.
    /// </summary>
    /// <returns>A task that completes when the list is read.</returns>
    [Fact(DisplayName = "REQ-RAG-166 VeniceModelsCarryPriceDataHandlingAndReasoning")]
    public async Task VeniceModelsCarryPriceDataHandlingAndReasoning()
    {
        var models = await LlmModelLister.ListDetailsAsync(LlmConnectorCatalog.Require("venice"), "test-key", new StubHandler(VeniceBody), CancellationToken.None);

        Assert.Equal(["gemini-4-carbon", "venice-uncensored-1-2", "z-ai-glm-5-3"], models.Select(m => m.Id));
        var glm = models.Single(m => m.Id == "z-ai-glm-5-3");
        Assert.Equal(1.75m, glm.InputPricePerMillionUsd);
        Assert.Equal(5.5m, glm.OutputPricePerMillionUsd);
        Assert.Equal(LlmModelDataHandling.RunsItself, glm.DataHandling);
        Assert.True(glm.SupportsReasoning);

        var gemini = models.Single(m => m.Id == "gemini-4-carbon");
        Assert.Equal(LlmModelDataHandling.PassesOn, gemini.DataHandling);
        Assert.Equal(12.5m, gemini.OutputPricePerMillionUsd);

        Assert.False(models.Single(m => m.Id == "venice-uncensored-1-2").SupportsReasoning);
    }

    /// <summary>
    /// OpenRouter's per-token prices are scaled to a million tokens, "0" stays a real price of nothing, "-1"
    /// ("varies") is not known, and a model reasons when <c>supported_parameters</c> names <c>reasoning</c>.
    /// OpenRouter says nothing about who runs a model, so that stays not known.
    /// </summary>
    /// <returns>A task that completes when the list is read.</returns>
    [Fact(DisplayName = "REQ-RAG-166 OpenRouterPerTokenPricesAreScaledToAMillion")]
    public async Task OpenRouterPerTokenPricesAreScaledToAMillion()
    {
        var models = await LlmModelLister.ListDetailsAsync(LlmConnectorCatalog.Require("openrouter"), "test-key", new StubHandler(OpenRouterBody), CancellationToken.None);

        var neon = models.Single(m => m.Id == "google/gemini-4-neon");
        Assert.Equal(3m, neon.InputPricePerMillionUsd);
        Assert.Equal(15m, neon.OutputPricePerMillionUsd);
        Assert.True(neon.SupportsReasoning);
        Assert.Equal(LlmModelDataHandling.NotKnown, neon.DataHandling);

        var free = models.Single(m => m.Id == "meta-llama/llama-3.3-8b-instruct:free");
        Assert.Equal(0m, free.InputPricePerMillionUsd);
        Assert.False(free.SupportsReasoning);

        var auto = models.Single(m => m.Id == "openrouter/auto");
        Assert.Null(auto.InputPricePerMillionUsd);
        Assert.Null(auto.OutputPricePerMillionUsd);
        Assert.Null(auto.SupportsReasoning);
    }

    /// <summary>
    /// A service that lists ids only (OpenAI's shape) gives every value empty, never a guess; Ollama's models are
    /// local.
    /// </summary>
    /// <returns>A task that completes when both lists are read.</returns>
    [Fact(DisplayName = "REQ-RAG-166 SilentServiceLeavesEveryValueEmpty")]
    public async Task SilentServiceLeavesEveryValueEmpty()
    {
        var openAi = await LlmModelLister.ListDetailsAsync(
            LlmConnectorCatalog.Require("openai"), "test-key", new StubHandler("""{"object":"list","data":[{"id":"gpt-6","object":"model","created":1794268800,"owned_by":"openai"}]}"""), CancellationToken.None);
        var ollama = await LlmModelLister.ListDetailsAsync(
            LlmConnectorCatalog.Require("ollama"), null, new StubHandler("""{"models":[{"name":"llama3.2:latest"}]}"""), CancellationToken.None);

        var gpt = Assert.Single(openAi);
        Assert.Equal(new LlmModelDetails { Id = "gpt-6" }, gpt);
        Assert.Null(gpt.InputPricePerMillionUsd);
        Assert.Equal(LlmModelDataHandling.NotKnown, gpt.DataHandling);
        Assert.Null(gpt.SupportsReasoning);
        Assert.Equal(LlmModelDataHandling.Local, Assert.Single(ollama).DataHandling);
    }

    /// <summary>
    /// The public call reaches the endpoint it is given, and its ids are exactly the ids the existing list call
    /// returns for the same answer.
    /// </summary>
    /// <returns>A task that completes when both calls have answered.</returns>
    [Fact(DisplayName = "REQ-RAG-166 PublicCallReturnsTheSameIdsWithDetails")]
    public async Task PublicCallReturnsTheSameIdsWithDetails()
    {
        using var server = new LoopbackServer(VeniceBody);
        var route = ModelRouter.Require("venice/z-ai-glm-5-3");

        var details = await LlmProviderFactory.ListModelDetailsAsync(route, "test-key", server.Endpoint);
        var ids = await LlmProviderFactory.ListModelsAsync(route, "test-key", server.Endpoint);

        Assert.Equal(ids, details.Select(d => d.Id));
        Assert.Equal(1.75m, details.Single(d => d.Id == "z-ai-glm-5-3").InputPricePerMillionUsd);
        Assert.All(server.Requests, request => Assert.Equal("GET /v1/models", request));
    }

    /// <summary>Answers every request with one JSON body.</summary>
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    /// <summary>A loopback HTTP server that answers every request with one JSON body and records "METHOD /path".</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly string body;

        public LoopbackServer(string body)
        {
            this.body = body;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Endpoint = $"http://127.0.0.1:{port}/v1";
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _ = ServeAsync();
        }

        public string Endpoint { get; }

        public List<string> Requests { get; } = [];

        public void Dispose() => listener.Close();

        private async Task ServeAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                lock (Requests)
                {
                    Requests.Add($"{context.Request.HttpMethod} {context.Request.Url?.AbsolutePath}");
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
