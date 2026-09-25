namespace TechieRag.Local;

/// <summary>
/// The chat format a model was trained on, which the provider applies before every call
/// (REQ-RAG-059 / BRD-98).
/// </summary>
/// <remarks>
/// The four fixed templates are applied by the provider in managed code, so every platform feeds the
/// model exactly the same text (REQ-RAG-058). <see cref="ModelDefined"/> hands the conversation to the
/// model's own template instead, through ONNX Runtime GenAI, the one engine on every platform.
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
    Gemma,

    /// <summary>
    /// The model's own template, from its <c>chat_template.jinja</c> or <c>tokenizer_config.json</c>, applied
    /// by ONNX Runtime GenAI's tokenizer: the start marker (Gemma's <c>&lt;bos&gt;</c>), the roles and the
    /// turn markers exactly as the model's publisher wrote them. Every <see cref="LocalModel.FromHuggingFace(string, string?, string?)"/>
    /// model uses it (REQ-RAG-108). A system message is sent as the first message; consecutive messages of
    /// one role are joined, since many templates require alternating turns.
    /// </summary>
    ModelDefined
}
