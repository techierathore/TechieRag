using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for source code files.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Processes source code files from various programming languages
/// and splits them into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads source code content from stream
/// 2) Optionally adds language context to the content
/// 3) Uses TextChunker to split code into appropriately sized chunks
/// 4) Creates TextChunk objects with language and file metadata
/// </para>
/// <para><b>Supported Languages:</b> C#, JavaScript, TypeScript, Python, Java, Go,
/// Rust, C++, C, and header files.</para>
/// <para><b>Design:</b> Preserves code structure and adds language metadata to
/// improve embedding quality for code search scenarios.</para>
/// </remarks>
public class CodeProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list of source code file extensions.</value>
    public IReadOnlyList<string> SupportedExtensions =>
    [
        // C# / .NET
        ".cs", ".vb", ".fs", ".fsi", ".fsx",

        // JavaScript ecosystem
        ".js", ".mjs", ".cjs",  // JavaScript (ES modules, CommonJS)
        ".ts", ".mts", ".cts",  // TypeScript (ES modules, CommonJS)
        ".jsx", ".tsx",         // React
        ".vue", ".svelte",      // Vue, Svelte components
        ".astro",               // Astro

        // Python
        ".py", ".pyw", ".pyi",  // Python, Python Windows, stub files
        ".ipynb",               // Jupyter notebooks (treated as code)

        // Java / JVM
        ".java", ".kt", ".kts", ".scala", ".groovy", ".clj", ".cljs",

        // Go
        ".go",

        // Rust
        ".rs",

        // C / C++
        ".c", ".h", ".cpp", ".hpp", ".cc", ".hh", ".cxx", ".hxx", ".c++", ".h++",

        // Ruby
        ".rb", ".rake", ".gemspec", ".erb",

        // PHP
        ".php", ".phtml", ".php3", ".php4", ".php5", ".php7", ".phps",

        // Swift / Objective-C
        ".swift", ".m", ".mm",

        // Dart / Flutter
        ".dart",

        // Shell scripts
        ".sh", ".bash", ".zsh", ".fish",
        ".ps1", ".psm1", ".psd1",  // PowerShell
        ".bat", ".cmd",            // Windows batch

        // Lua
        ".lua",

        // Perl
        ".pl", ".pm",

        // R
        ".r", ".R", ".rmd", ".Rmd",

        // SQL
        ".sql",

        // Elixir / Erlang
        ".ex", ".exs", ".erl", ".hrl",

        // Haskell
        ".hs", ".lhs",

        // OCaml
        ".ml", ".mli",

        // Nim
        ".nim",

        // Zig
        ".zig",

        // V
        ".v",

        // D
        ".d",

        // Julia
        ".jl",

        // Crystal
        ".cr",

        // Solidity (blockchain)
        ".sol"
    ];

    /// <summary>
    /// Mapping from file extensions to language names for metadata.
    /// </summary>
    private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        // C# / .NET
        [".cs"] = "csharp",
        [".vb"] = "vb.net",
        [".fs"] = "fsharp",
        [".fsi"] = "fsharp",
        [".fsx"] = "fsharp",

        // JavaScript ecosystem
        [".js"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".ts"] = "typescript",
        [".mts"] = "typescript",
        [".cts"] = "typescript",
        [".jsx"] = "javascript-react",
        [".tsx"] = "typescript-react",
        [".vue"] = "vue",
        [".svelte"] = "svelte",
        [".astro"] = "astro",

        // Python
        [".py"] = "python",
        [".pyw"] = "python",
        [".pyi"] = "python",
        [".ipynb"] = "jupyter",

        // Java / JVM
        [".java"] = "java",
        [".kt"] = "kotlin",
        [".kts"] = "kotlin",
        [".scala"] = "scala",
        [".groovy"] = "groovy",
        [".clj"] = "clojure",
        [".cljs"] = "clojurescript",

        // Go
        [".go"] = "go",

        // Rust
        [".rs"] = "rust",

        // C / C++
        [".c"] = "c",
        [".h"] = "c-header",
        [".cpp"] = "cpp",
        [".hpp"] = "cpp-header",
        [".cc"] = "cpp",
        [".hh"] = "cpp-header",
        [".cxx"] = "cpp",
        [".hxx"] = "cpp-header",
        [".c++"] = "cpp",
        [".h++"] = "cpp-header",

        // Ruby
        [".rb"] = "ruby",
        [".rake"] = "ruby",
        [".gemspec"] = "ruby",
        [".erb"] = "erb",

        // PHP
        [".php"] = "php",
        [".phtml"] = "php",
        [".php3"] = "php",
        [".php4"] = "php",
        [".php5"] = "php",
        [".php7"] = "php",
        [".phps"] = "php",

        // Swift / Objective-C
        [".swift"] = "swift",
        [".m"] = "objective-c",
        [".mm"] = "objective-cpp",

        // Dart
        [".dart"] = "dart",

        // Shell scripts
        [".sh"] = "shell",
        [".bash"] = "bash",
        [".zsh"] = "zsh",
        [".fish"] = "fish",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".psd1"] = "powershell",
        [".bat"] = "batch",
        [".cmd"] = "batch",

        // Lua
        [".lua"] = "lua",

        // Perl
        [".pl"] = "perl",
        [".pm"] = "perl",

        // R
        [".r"] = "r",
        [".R"] = "r",
        [".rmd"] = "r-markdown",
        [".Rmd"] = "r-markdown",

        // SQL
        [".sql"] = "sql",

        // Elixir / Erlang
        [".ex"] = "elixir",
        [".exs"] = "elixir",
        [".erl"] = "erlang",
        [".hrl"] = "erlang",

        // Haskell
        [".hs"] = "haskell",
        [".lhs"] = "haskell",

        // OCaml
        [".ml"] = "ocaml",
        [".mli"] = "ocaml",

        // Nim
        [".nim"] = "nim",

        // Zig
        [".zig"] = "zig",

        // V
        [".v"] = "v",

        // D
        [".d"] = "d",

        // Julia
        [".jl"] = "julia",

        // Crystal
        [".cr"] = "crystal",

        // Solidity
        [".sol"] = "solidity"
    };

    /// <summary>
    /// Processes a source code file stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The source code file content stream.</param>
    /// <param name="fileName">The original file name (used for extension detection and metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the source code file.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Read source code content from stream</description></item>
    /// <item><description>Detect language from file extension</description></item>
    /// <item><description>Chunk code using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with language and chunk index metadata</description></item>
    /// </list>
    /// <para><b>Special Handling:</b> Code files typically have larger chunk sizes than
    /// prose documents to preserve context. Consider using larger MaxChunkSize values
    /// (e.g., 1000-2000 characters) for code files.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new CodeProcessor();
    /// using var stream = File.OpenRead("Program.cs");
    /// var options = new DocumentProcessingOptions { MaxChunkSize = 1000 };
    /// var chunks = await processor.ProcessAsync(stream, "Program.cs", options);
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        options ??= new DocumentProcessingOptions();
        var chunks = new List<TextChunk>();
        var documentId = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var language = GetLanguageFromExtension(extension);

        using var reader = new StreamReader(content);
        var code = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(code))
        {
            return chunks;
        }

        // Normalize line endings but preserve code structure
        code = NormalizeLineEndings(code);

        var textChunks = TextChunker.ChunkText(
            code,
            options.MaxChunkSize,
            options.ChunkOverlap,
            options.Chunker);

        var chunkIndex = 0;
        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = new TextChunk
            {
                DocumentId = documentId,
                Text = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = CreateMetadata(fileName, language, options.Metadata)
            };

            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>
    /// Gets the programming language name from a file extension.
    /// </summary>
    /// <param name="extension">The file extension including the dot (e.g., ".cs").</param>
    /// <returns>The language name, or "unknown" if not recognized.</returns>
    private static string GetLanguageFromExtension(string extension)
    {
        return ExtensionToLanguage.TryGetValue(extension, out var language)
            ? language
            : "unknown";
    }

    /// <summary>
    /// Normalizes line endings to Unix-style (LF) for consistent processing.
    /// </summary>
    /// <param name="code">The source code to normalize.</param>
    /// <returns>Code with normalized line endings.</returns>
    private static string NormalizeLineEndings(string code)
    {
        return code.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>
    /// Creates metadata dictionary for a text chunk.
    /// </summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="language">The detected programming language.</param>
    /// <param name="additionalMetadata">Additional metadata from processing options.</param>
    /// <returns>Dictionary containing chunk metadata.</returns>
    private static Dictionary<string, object> CreateMetadata(
        string fileName,
        string language,
        Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = fileName,
            ["language"] = language,
            ["processorType"] = nameof(CodeProcessor)
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }
}
