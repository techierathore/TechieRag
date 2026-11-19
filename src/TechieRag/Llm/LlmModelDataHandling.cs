namespace TechieRag.Llm;

/// <summary>
/// Who runs a listed model, and so who sees the text sent to it (REQ-RAG-166, Lekhak TR-RAG-011).
/// </summary>
public enum LlmModelDataHandling
{
    /// <summary>The service does not say. The default.</summary>
    NotKnown = 0,

    /// <summary>The service says it runs the model itself (Venice: <c>"privacy": "private"</c>).</summary>
    RunsItself = 1,

    /// <summary>The service says it passes the text on to another company that runs the model (Venice: <c>"privacy": "anonymized"</c>).</summary>
    PassesOn = 2,

    /// <summary>The model runs in a local runtime the host points at (LM Studio, Ollama), not at a hosted service.</summary>
    Local = 3
}
