namespace TechieRag.Local;

/// <summary>
/// The chat format a model was trained on, which the provider applies before every call
/// (REQ-RAG-059 / BRD-98).
/// </summary>
/// <remarks>
/// The template is applied by the provider in managed code, not by the runtime, so every platform
/// feeds the model exactly the same text whatever runtime is underneath (REQ-RAG-058).
/// </remarks>
public enum LocalChatTemplate
{
    /// <summary>ChatML: <c>&lt;|im_start|&gt;role\n…&lt;|im_end|&gt;</c> (Qwen 2 and 2.5, SmolLM and others).</summary>
    ChatMl,

    /// <summary>Phi-3: <c>&lt;|system|&gt;…&lt;|end|&gt;&lt;|user|&gt;…&lt;|end|&gt;&lt;|assistant|&gt;</c>.</summary>
    Phi3,

    /// <summary>Llama 3: <c>&lt;|start_header_id|&gt;role&lt;|end_header_id|&gt;\n\n…&lt;|eot_id|&gt;</c>.</summary>
    Llama3,

    /// <summary>Gemma: <c>&lt;start_of_turn&gt;user\n…&lt;end_of_turn&gt;</c>; a system message is folded into the first user turn.</summary>
    Gemma
}
