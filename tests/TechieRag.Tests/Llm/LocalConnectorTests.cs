using TechieRag.Llm;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// The core's side of the in-process local model: the <c>local</c> connector row, the route, and
/// the factory arm when the TechieRag.Local package is absent (REQ-RAG-064 / BRD-104). This suite
/// never references TechieRag.Local, so nothing registers the provider here.
/// </summary>
public class LocalConnectorTests
{
    /// <summary>The <c>local</c> connector exists, points at LlmSource.Local, and has no endpoint and no key.</summary>
    [Fact(DisplayName = "REQ-RAG-064 LocalConnectorNeedsNoEndpointOrKey")]
    public void LocalConnectorNeedsNoEndpointOrKey()
    {
        var connector = LlmConnectorCatalog.Require("local");

        Assert.Equal(LlmSource.Local, connector.Source);
        Assert.Null(connector.Endpoint);
        Assert.False(connector.RequiresApiKey);
        Assert.Empty(connector.ModelPrefixes);
    }

    /// <summary><c>local/&lt;model&gt;</c> routes to the local connector with the model id intact.</summary>
    [Fact]
    public void RouterResolvesLocalRoute()
    {
        var route = ModelRouter.Require("local/qwen2.5-0.5b-instruct");

        Assert.Equal(("local", "qwen2.5-0.5b-instruct"), (route.Connector.Name, route.ModelId));
    }

    /// <summary>Without the TechieRag.Local package registered, the factory says which package and call are missing.</summary>
    [Fact]
    public void FactoryNamesMissingPackage()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => LlmProviderFactory.CreateForModel("local/qwen2.5-0.5b-instruct", apiKey: null));

        Assert.Contains("TechieRag.Local", error.Message);
        Assert.Contains("LocalLlm.Register()", error.Message);
    }

    /// <summary>A bare open-weight name never routes to the local model: it has to be named.</summary>
    [Fact]
    public void BareModelNameNeverRoutesLocal() =>
        Assert.NotEqual(LlmSource.Local, ModelRouter.Resolve("qwen2.5-0.5b-instruct")?.Connector.Source);
}
