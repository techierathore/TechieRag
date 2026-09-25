using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace TechieRag.Local.Tests.TestDoubles;

/// <summary>
/// One Hugging Face repository served by a <see cref="LocalFileServer"/> the way Hugging Face's public
/// API serves it: <c>api/models/&lt;repo&gt;</c> (commit, gated, card licence, file names),
/// <c>api/models/&lt;repo&gt;/tree/&lt;commit&gt;</c> (sizes and fingerprints: <c>lfs.oid</c> SHA-256 for a
/// large file, <c>oid</c> git blob SHA-1 for a small one) and <c>&lt;repo&gt;/resolve/&lt;commit&gt;/&lt;path&gt;</c>.
/// </summary>
internal sealed class FakeHuggingFaceRepository
{
    /// <summary>A genai_config.json as Arm's Gemma 3 1B carries it (trimmed to what the library reads).</summary>
    internal const string GemmaGenAiConfig = """
        {"model":{"bos_token_id":2,"context_length":4096,"decoder":{"filename":"model.onnx","head_size":256,"hidden_size":1152,
        "num_attention_heads":4,"num_hidden_layers":26,"num_key_value_heads":1},"eos_token_id":[1,106],"type":"gemma3_text"},
        "search":{"max_length":4096}}
        """;

    private readonly LocalFileServer server;
    private readonly Dictionary<string, (byte[] Content, bool Lfs)> files = new(StringComparer.Ordinal);

    /// <summary>Creates a repository with Gemma-like runtime files and non-runtime clutter at its root.</summary>
    /// <param name="server">The server to publish on.</param>
    /// <param name="repository">The repository id.</param>
    public FakeHuggingFaceRepository(LocalFileServer server, string repository)
    {
        this.server = server;
        Repository = repository;
        AddFile("genai_config.json", Encoding.UTF8.GetBytes(GemmaGenAiConfig), lfs: false);
        AddFile("model.onnx", RandomNumberGenerator.GetBytes(30_000), lfs: true);
        AddFile("model.onnx.data", RandomNumberGenerator.GetBytes(120_000), lfs: true);
        AddFile("tokenizer.json", RandomNumberGenerator.GetBytes(40_000), lfs: true);
        AddFile("tokenizer_config.json", Encoding.UTF8.GetBytes("""{"add_bos_token":true}"""), lfs: false);
        AddFile("special_tokens_map.json", Encoding.UTF8.GetBytes("""{"bos_token":"<bos>"}"""), lfs: false);
        AddFile("chat_template.jinja", Encoding.UTF8.GetBytes("{{ bos_token }}"), lfs: false);
        AddFile("README.md", Encoding.UTF8.GetBytes("# readme"), lfs: false);
        AddFile("example.py", Encoding.UTF8.GetBytes("print(1)"), lfs: false);
        AddFile("android_onnx_genai_profile.json", RandomNumberGenerator.GetBytes(20_000), lfs: true);
        AddFile("benchmarks/phone.yaml", Encoding.UTF8.GetBytes("a: 1"), lfs: false);
    }

    /// <summary>Gets the repository id.</summary>
    public string Repository { get; }

    /// <summary>Gets or sets the commit the repository is at.</summary>
    public string Commit { get; set; } = "fcf02f9393f9c2e657b905655668e10fd169f805";

    /// <summary>Gets or sets the <c>gated</c> value: false, "auto" or "manual".</summary>
    public JsonNode Gated { get; set; } = false;

    /// <summary>Gets or sets the model card's licence.</summary>
    public string Licence { get; set; } = "gemma";

    /// <summary>Gets the names of the runtime files, in the order the library sorts them.</summary>
    public static IReadOnlyList<string> RuntimeFiles { get; } =
    [
        "chat_template.jinja", "genai_config.json", "model.onnx", "model.onnx.data",
        "special_tokens_map.json", "tokenizer.json", "tokenizer_config.json"
    ];

    /// <summary>Gets the total size of the runtime files.</summary>
    public long RuntimeBytes => RuntimeFiles.Sum(name => (long)files[name].Content.Length);

    /// <summary>Adds or replaces a file.</summary>
    /// <param name="path">Its path in the repository.</param>
    /// <param name="content">Its bytes.</param>
    /// <param name="lfs">Whether it is in large-file storage (SHA-256 published) rather than git (SHA-1).</param>
    public void AddFile(string path, byte[] content, bool lfs) => files[path] = (content, lfs);

    /// <summary>Publishes the API answers and the files at <see cref="Commit"/>.</summary>
    /// <param name="tamperedPath">A file served with different bytes than the fingerprint the tree lists, or null.</param>
    public void Publish(string? tamperedPath = null)
    {
        var info = new JsonObject
        {
            ["id"] = Repository,
            ["sha"] = Commit,
            ["gated"] = Gated.DeepClone(),
            ["private"] = false,
            ["cardData"] = new JsonObject { ["license"] = Licence },
            ["siblings"] = new JsonArray(files.Keys.Select(k => (JsonNode)new JsonObject { ["rfilename"] = k }).ToArray())
        };
        server.Add($"api/models/{Repository}", Encoding.UTF8.GetBytes(info.ToJsonString()));
        server.Add($"api/models/{Repository}/revision/{Commit}", Encoding.UTF8.GetBytes(info.ToJsonString()));

        var tree = new JsonArray { new JsonObject { ["type"] = "directory", ["path"] = "benchmarks", ["size"] = 0, ["oid"] = new string('a', 40) } };
        foreach (var (path, (content, lfs)) in files.Where(f => !f.Key.Contains('/', StringComparison.Ordinal)))
        {
            var entry = new JsonObject { ["type"] = "file", ["path"] = path, ["size"] = content.Length, ["oid"] = GitBlobSha1(content) };
            if (lfs)
            {
                entry["lfs"] = new JsonObject { ["oid"] = Convert.ToHexStringLower(SHA256.HashData(content)), ["size"] = content.Length, ["pointerSize"] = 131 };
            }

            tree.Add(entry);
        }

        server.Add($"api/models/{Repository}/tree/{Commit}", Encoding.UTF8.GetBytes(tree.ToJsonString()));
        foreach (var (path, (content, _)) in files)
        {
            var served = path == tamperedPath ? Tamper(content) : content;
            server.Add($"{Repository}/resolve/{Commit}/{path}", served);
        }
    }

    /// <summary>Computes git's blob hash of some bytes, as Hugging Face publishes it for a small file.</summary>
    /// <param name="content">The bytes.</param>
    /// <returns>The lower-case hex SHA-1 of the blob header and the bytes.</returns>
    public static string GitBlobSha1(byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. content]));
    }

    private static byte[] Tamper(byte[] content)
    {
        var copy = (byte[])content.Clone();
        copy[^1] ^= 0xFF;
        return copy;
    }
}
