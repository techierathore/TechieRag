namespace TechieDesk.Services;

/// <summary>
/// The parts of <c>TechieRagConfig</c> a screen owns, so a save writes only those (REQ-NFR-004).
/// </summary>
/// <remarks>
/// <para><b>The defect this exists to fix: a silent lost update.</b> Four screens write the one
/// <c>techierag-config.json</c> through <c>TechieRagConfigService</c> — App Settings, RAG
/// Configuration, LLM Settings and the first-run wizard — and each loaded the whole config on open
/// and saved the whole config back. Two pairs overlap:</para>
/// <list type="bullet">
/// <item><c>Embedding</c> + <c>VectorStore</c> — written by BOTH App Settings and RAG Configuration</item>
/// <item><c>Llm</c> — written by BOTH App Settings and LLM Settings</item>
/// </list>
/// <para>So changing the embedding provider on App Settings and then saving RAG Configuration from
/// its now-stale copy silently reverted the first change: no conflict, no warning, no log line. The
/// user sees a success toast and the opposite of what they asked for.</para>
/// <para><b>Why a flags enum rather than per-screen merge code.</b> Fixing it in each screen leaves
/// the next screen to reintroduce it — there were already four writers and two independent overlaps
/// before anyone noticed. Declaring ownership makes the merge the service's job and makes each
/// screen's scope reviewable in one line.</para>
/// </remarks>
[Flags]
public enum TechieRagConfigSections
{
    /// <summary>Nothing. A save with no sections is a no-op and is refused as a programming error.</summary>
    None = 0,

    /// <summary>Embedding provider, model, endpoint and key.</summary>
    Embedding = 1 << 0,

    /// <summary>Vector store type and connection.</summary>
    VectorStore = 1 << 1,

    /// <summary>Chunking and processing options.</summary>
    Processing = 1 << 2,

    /// <summary>The telemetry opt-in flag.</summary>
    Telemetry = 1 << 3,

    /// <summary>Primary LLM provider.</summary>
    Llm = 1 << 4,

    /// <summary>Fallback LLM provider.</summary>
    LlmFallback = 1 << 5,

    /// <summary>Prompt templates and options.</summary>
    Prompt = 1 << 6,

    /// <summary>Retry, timeout and circuit-breaker options.</summary>
    Resilience = 1 << 7,

    /// <summary>Token and cost tracking options.</summary>
    UsageTracking = 1 << 8,

    /// <summary>Reranking options.</summary>
    Rerank = 1 << 9,

    /// <summary>Persistence options.</summary>
    Persistence = 1 << 10,

    /// <summary>
    /// Every section — the whole document.
    /// </summary>
    /// <remarks>
    /// Correct for the first-run wizard, which is establishing the entire configuration on an install
    /// that has none, and there is no concurrent editor to lose an update to. It is NOT correct for
    /// an ordinary settings screen: that is the bug this enum exists to prevent.
    /// </remarks>
    All = Embedding | VectorStore | Processing | Telemetry | Llm | LlmFallback | Prompt
        | Resilience | UsageTracking | Rerank | Persistence,
}
