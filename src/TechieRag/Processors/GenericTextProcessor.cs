using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Generic document processor for text-based files that don't have a specific processor.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Processes any text-based file that isn't handled by specific processors.
/// This includes configuration files, project files, markup files, and other text formats.</para>
/// <para><b>Code Flow:</b>
/// 1) Checks if file has a known binary extension (images, etc.) and skips those
/// 2) Attempts to detect if file content is text-based
/// 3) If text-based, chunks and returns content
/// </para>
/// <para><b>Design:</b> Acts as a fallback processor for any file type. Should be registered
/// last in the processor chain so specific processors take precedence.</para>
/// </remarks>
public class GenericTextProcessor : IDocumentProcessor
{
    /// <summary>
    /// Known binary file extensions that should never be processed as text.
    /// </summary>
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff", ".tif", ".webp", ".svg", ".psd", ".raw",
        // Audio
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a",
        // Video
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        // Archives
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab",
        // Executables/Libraries
        ".exe", ".dll", ".so", ".dylib", ".bin", ".msi", ".app",
        // Office binary formats
        ".xls", ".ppt", ".doc",  // Old binary formats (not .xlsx, .pptx, .docx which are XML-based)
        // Database
        ".db", ".sqlite", ".mdb", ".accdb",
        // Other binary
        ".pdf",  // PDF has its own processor
        ".class", ".jar", ".war", ".ear",
        ".pyc", ".pyo",
        ".o", ".obj", ".lib", ".a",
        ".nupkg", ".snupkg",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".onnx", ".pb", ".h5", ".hdf5", ".pkl", ".model"
    };

    /// <summary>
    /// Additional text-based extensions beyond the basics.
    /// These are explicitly supported as text files.
    /// </summary>
    private static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Project/Solution files
        ".sln", ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".proj", ".props", ".targets",
        ".esproj", ".sqlproj", ".dbproj", ".modelproj", ".wixproj",

        // Config files
        ".config", ".ini", ".cfg", ".conf", ".env", ".properties",
        ".editorconfig", ".gitignore", ".gitattributes", ".dockerignore",
        ".npmrc", ".nvmrc", ".yarnrc", ".babelrc", ".eslintrc", ".prettierrc",

        // Markup/Data
        ".xml", ".xaml", ".xsd", ".xsl", ".xslt", ".dtd",
        ".yaml", ".yml",
        ".csv", ".tsv",
        ".resx", ".resw",
        ".manifest", ".pubxml",

        // Web
        ".css", ".scss", ".sass", ".less", ".styl",
        ".svg",  // SVG is XML-based text
        ".vue", ".svelte", ".astro",
        ".htaccess", ".htpasswd",

        // Scripts/Shells
        ".sh", ".bash", ".zsh", ".fish", ".ksh", ".csh",
        ".ps1", ".psm1", ".psd1", ".ps1xml",
        ".bat", ".cmd",
        ".awk", ".sed",

        // Build/CI
        ".dockerfile", ".containerfile",
        ".makefile", ".mk", ".cmake",
        ".gradle", ".gradle.kts",
        ".sbt",

        // Documentation
        ".rst", ".asciidoc", ".adoc", ".textile", ".org",
        ".tex", ".latex", ".bib",

        // Other languages/formats
        ".r", ".R", ".rmd", ".Rmd",
        ".scala", ".kt", ".kts", ".groovy", ".clj", ".cljs", ".edn",
        ".ex", ".exs", ".erl", ".hrl",
        ".hs", ".lhs", ".cabal",
        ".ml", ".mli", ".fs", ".fsi", ".fsx",
        ".rb", ".rake", ".gemspec",
        ".php", ".phtml",
        ".pl", ".pm", ".pod", ".t",
        ".lua",
        ".nim",
        ".zig",
        ".v", ".sv", ".svh",  // Verilog/SystemVerilog
        ".vhd", ".vhdl",  // VHDL
        ".tcl",
        ".lisp", ".cl", ".el", ".scm", ".ss", ".rkt",
        ".swift",
        ".dart",
        ".pas", ".pp", ".inc",  // Pascal
        ".d",
        ".f90", ".f95", ".f03", ".f08", ".for", ".f",  // Fortran
        ".cob", ".cbl",  // COBOL
        ".sql", ".pgsql", ".mysql", ".plsql",
        ".graphql", ".gql",
        ".proto",  // Protocol Buffers
        ".thrift",
        ".avsc", ".avdl",  // Avro
        ".prisma",
        ".tf", ".tfvars", ".hcl",  // Terraform/HCL
        ".jsonnet", ".libsonnet",
        ".dhall",
        ".nix",
        ".pug", ".jade", ".ejs", ".erb", ".haml", ".slim",
        ".liquid",
        ".twig",
        ".jinja", ".jinja2", ".j2",
        ".mustache", ".hbs", ".handlebars",

        // Lock files (usually text/JSON)
        ".lock",

        // Log files
        ".log",

        // Misc text
        ".txt", ".text", ".rtf",
        ".diff", ".patch",
        ".in", ".sample", ".example", ".template", ".tpl",
        ".map",  // Source maps
        ".snap",  // Jest snapshots
    };

    /// <summary>
    /// Gets the list of file extensions this processor explicitly supports.
    /// </summary>
    /// <remarks>
    /// This processor also handles unknown extensions if they contain text content.
    /// </remarks>
    public IReadOnlyList<string> SupportedExtensions => KnownTextExtensions.ToList();

    /// <summary>
    /// Checks if a file extension is a known binary format.
    /// </summary>
    public static bool IsBinaryExtension(string extension)
    {
        return BinaryExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if a file extension is known to be text-based.
    /// </summary>
    public static bool IsKnownTextExtension(string extension)
    {
        return KnownTextExtensions.Contains(extension);
    }

    /// <summary>
    /// Processes a text file stream and returns text chunks ready for embedding.
    /// </summary>
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

        // Check if it's a known binary extension
        if (IsBinaryExtension(extension))
        {
            throw new NotSupportedException(
                $"Cannot ingest binary file '{fileName}'. File type '{extension}' is not supported for text extraction.");
        }

        // Read content
        using var reader = new StreamReader(content);
        string text;

        try
        {
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            throw new NotSupportedException(
                $"Cannot ingest '{fileName}'. File appears to be binary or uses an unsupported encoding.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        // Check if content appears to be binary (contains null bytes or too many non-printable chars)
        if (LooksLikeBinary(text))
        {
            throw new NotSupportedException(
                $"Cannot ingest '{fileName}'. File content appears to be binary, not text.");
        }

        // Normalize line endings
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var textChunks = TextChunker.ChunkText(
            text,
            options.MaxChunkSize,
            options.ChunkOverlap);

        var chunkIndex = 0;
        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = new TextChunk
            {
                DocumentId = documentId,
                Text = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = CreateMetadata(fileName, extension, options.Metadata)
            };

            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>
    /// Checks if the content appears to be binary rather than text.
    /// </summary>
    private static bool LooksLikeBinary(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        // Check first 8KB for binary indicators
        var checkLength = Math.Min(content.Length, 8192);
        var nullCount = 0;
        var nonPrintableCount = 0;

        for (int i = 0; i < checkLength; i++)
        {
            var c = content[i];

            // Null byte is a strong indicator of binary
            if (c == '\0')
            {
                nullCount++;
                if (nullCount > 1) // More than one null byte = binary
                    return true;
            }

            // Count non-printable characters (excluding common whitespace)
            if (c < 32 && c != '\n' && c != '\r' && c != '\t')
            {
                nonPrintableCount++;
            }
        }

        // If more than 10% non-printable, probably binary
        return nonPrintableCount > checkLength * 0.1;
    }

    /// <summary>
    /// Creates metadata dictionary for a text chunk.
    /// </summary>
    private static Dictionary<string, object> CreateMetadata(
        string fileName,
        string extension,
        Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = fileName,
            ["fileExtension"] = extension,
            ["processorType"] = nameof(GenericTextProcessor)
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
