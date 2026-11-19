using System.Net;
using System.Text;
using System.Xml.Linq;
using TechieRag.Embedding;
using TechieRag.Tests.TestDoubles;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.PublicApi;

/// <summary>
/// REQ-RAG-168 (TrStudio TR-RAG-006): an app that uses only the SQLite store does not carry the PostgreSQL,
/// Qdrant or Azure clients.
/// </summary>
/// <remarks>
/// <para><b>The report.</b> On 1.1.5 a <c>net10.0</c> console app whose only package was TechieRag listed
/// <c>Npgsql</c> 10.0.1, <c>Pgvector</c> 0.3.2, <c>Qdrant.Client</c> 1.16.1 (with <c>Grpc.Net.Client</c>,
/// <c>Grpc.Core.Api</c> and <c>Grpc.Net.Common</c> 2.71.0) and <c>Azure.AI.OpenAI</c> 2.1.0 as hard dependencies:
/// 8 files, about 4 MB, in the output folder.</para>
/// <para><b>The fix.</b> The PostgreSQL stores are in <c>TechieRag.PgVector</c>, the Qdrant store in
/// <c>TechieRag.Qdrant</c>, and the Azure OpenAI embedding provider speaks HTTP itself. The builder methods keep
/// their names and find the package at build time.</para>
/// </remarks>
public sealed class CorePackageCarriesNoServerClientsTests
{
    private static readonly string[] ServerClients =
        ["Npgsql", "Pgvector", "Qdrant.Client", "Grpc.Net.Client", "Grpc.Core.Api", "Grpc.Net.Common", "Azure.AI.OpenAI", "Azure.Core", "OpenAI", "System.ClientModel"];

    /// <summary>
    /// The core project references none of the server clients, and the built core assembly references none of
    /// their assemblies, so none reaches a consumer through TechieRag.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-168 CorePackageCarriesNoServerClients")]
    public void CorePackageCarriesNoServerClients()
    {
        var project = XDocument.Parse(LibraryRepoFiles.Read("src/TechieRag/TechieRag.csproj"));
        var packages = project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include")).ToList();
        var assemblies = typeof(TechieRagBuilder).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToList();

        Assert.All(ServerClients, client => Assert.DoesNotContain(client, packages));
        Assert.All(ServerClients, client => Assert.DoesNotContain(client, assemblies));
    }

    /// <summary>
    /// Each store is a package of its own, carrying its client, packed by both release workflows at the one version
    /// every package shares.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-168 StoresArePackagesOfTheirOwn")]
    public void StoresArePackagesOfTheirOwn()
    {
        var core = XDocument.Parse(LibraryRepoFiles.Read("src/TechieRag/TechieRag.csproj"));
        var version = core.Descendants("Version").Single().Value;
        foreach (var (id, clients) in new[] { ("TechieRag.PgVector", new[] { "Npgsql", "Pgvector" }), ("TechieRag.Qdrant", new[] { "Qdrant.Client" }) })
        {
            var path = $"src/{id}/{id}.csproj";
            var project = XDocument.Parse(LibraryRepoFiles.Read(path));

            Assert.Equal(id, project.Descendants("PackageId").Single().Value);
            Assert.Equal(version, project.Descendants("Version").Single().Value);
            var packages = project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include")).ToList();
            Assert.All(clients, client => Assert.Contains(client, packages));
            Assert.Contains(LibraryRepoFiles.Read(".github/workflows/publish-nuget.yml").Split('\n'), line => line.Contains("dotnet pack") && line.Contains(id.ToUpperInvariant().Replace("TECHIERAG.", string.Empty) + "_PROJECT"));
            Assert.Contains(LibraryRepoFiles.Read(".github/workflows/publish-github-packages.yml").Split('\n'), line => line.Contains("dotnet pack") && line.Contains(path));
        }
    }

    /// <summary>
    /// The builder's own calls still reach both stores: with the package referenced, <c>UsePgVector</c> builds a
    /// <c>PgVectorStore</c> and <c>UseQdrant</c> a <c>QdrantStore</c>, neither connecting yet.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-168 BuilderFindsTheStorePackageByName")]
    public void BuilderFindsTheStorePackageByName()
    {
        var pg = new TechieRagBuilder()
            .UsePgVector("Host=127.0.0.1;Port=1;Database=none;Username=none")
            .CreateVectorStore(new FakeEmbeddingProvider());
        var qdrant = new TechieRagBuilder()
            .UseQdrant("http://127.0.0.1:1")
            .CreateVectorStore(new FakeEmbeddingProvider());

        Assert.Equal("TechieRag.VectorStores.PgVectorStore", pg.GetType().FullName);
        Assert.Equal("TechieRag.PgVector", pg.GetType().Assembly.GetName().Name);
        Assert.Equal("TechieRag.VectorStores.QdrantStore", qdrant.GetType().FullName);
        Assert.Equal("TechieRag.Qdrant", qdrant.GetType().Assembly.GetName().Name);
    }

    /// <summary>An app that asks for a store whose package it does not reference is told which package to add.</summary>
    [Fact(DisplayName = "REQ-RAG-168 MissingPackageIsNamed")]
    public void MissingPackageIsNamed()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => StorePackages.Require<Func<string, int>>(() => null, "TechieRag.NoSuchStore", "The test store"));

        Assert.Contains("TechieRag.NoSuchStore", error.Message);
        Assert.Contains("The test store", error.Message);
    }

    /// <summary>
    /// The Azure OpenAI embedding provider sends what the SDK sent: <c>POST
    /// {endpoint}/openai/deployments/{deployment}/embeddings?api-version=2024-10-21</c> with the key in the
    /// <c>api-key</c> header and the texts as <c>input</c>; it reads the vectors in index order and the reported tokens.
    /// </summary>
    /// <returns>A task that completes when the batch is embedded.</returns>
    [Fact(DisplayName = "REQ-RAG-168 AzureEmbeddingSpeaksHttpItself")]
    public async Task AzureEmbeddingSpeaksHttpItself()
    {
        var handler = new RecordingHandler("""{"object":"list","data":[{"object":"embedding","index":1,"embedding":[0.0,1.0]},{"object":"embedding","index":0,"embedding":[1.0,0.0]}],"model":"text-embedding-3-small","usage":{"prompt_tokens":7,"total_tokens":7}}""");
        using var provider = new AzureOpenAIEmbeddingProvider("https://example.openai.azure.com/", "test-key", "emb-small", 2, handler);
        var tokens = 0;
        provider.OnEmbeddingCompleted += (_, e) => tokens = e.TokenCount;

        var vectors = await provider.EmbedBatchAsync(["first", "second"]);

        Assert.Equal([1f, 0f], vectors[0]);
        Assert.Equal([0f, 1f], vectors[1]);
        Assert.Equal(7, tokens);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://example.openai.azure.com/openai/deployments/emb-small/embeddings?api-version=2024-10-21"), handler.Uri);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Equal("""{"input":["first","second"]}""", handler.Body);
    }

    /// <summary>Records one request and answers it with a fixed body.</summary>
    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public string? ApiKey { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
