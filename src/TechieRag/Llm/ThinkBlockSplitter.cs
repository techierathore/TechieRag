using System.Text;

namespace TechieRag.Llm;

/// <summary>
/// Splits a <c>&lt;think&gt;…&lt;/think&gt;</c> block at the start of a model's answer out of the answer
/// (REQ-RAG-138, Chatur TR-RAG-009).
/// </summary>
/// <remarks>
/// <para>Some models (OpenCode Go's minimax-m3, DeepSeek-R1 and Qwen builds on several hosts, on Ollama and
/// run locally) put their reasoning inside the answer text instead of a separate thinking field. Used by the
/// OpenAI-style providers, Ollama (REQ-RAG-141) and <c>TechieRag.Local</c> (REQ-RAG-142).
/// Only a block that opens the answer, after optional white space, is split: a <c>&lt;think&gt;</c> later
/// in the answer is the model's text and is left alone.</para>
/// <para>For streams, one instance follows one call. The tags may be cut across chunks, so text that could
/// still be the start of a tag is held back until the next chunk settles it. A block the model never
/// closed (the token limit hit mid-thought) is all thinking.</para>
/// </remarks>
internal sealed class ThinkBlockSplitter
{
    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    private readonly StringBuilder pending = new();
    private SplitState state = SplitState.Start;

    private enum SplitState
    {
        /// <summary>No answer text seen yet that settles whether a block opens it.</summary>
        Start,

        /// <summary>Inside the opening block.</summary>
        InBlock,

        /// <summary>The block closed; white space before the answer is dropped.</summary>
        AfterBlock,

        /// <summary>Everything from here is answer.</summary>
        Answer
    }

    /// <summary>Splits a whole answer and joins its thinking with any the service sent in its own field.</summary>
    /// <param name="content">The answer text as the service sent it.</param>
    /// <param name="reasoning">The service's <c>reasoning_content</c> (or <c>reasoning</c>) text, if any.</param>
    /// <returns>The thinking text (null when there was none) and the answer without it.</returns>
    public static (string? Thinking, string? Answer) Split(string? content, string? reasoning)
    {
        var (block, answer) = Split(content);
        var thinking = string.IsNullOrEmpty(reasoning) ? block : reasoning + block;
        return (string.IsNullOrEmpty(thinking) ? null : thinking, answer);
    }

    /// <summary>Splits a whole answer.</summary>
    /// <param name="content">The answer text as the service sent it.</param>
    /// <returns>The thinking text (null when there was none) and the answer without it.</returns>
    public static (string? Thinking, string? Answer) Split(string? content)
    {
        if (string.IsNullOrEmpty(content)) return (null, content);

        var splitter = new ThinkBlockSplitter();
        var thinking = new StringBuilder();
        var answer = new StringBuilder();
        foreach (var part in splitter.Push(content).Concat(splitter.Flush()))
        {
            (part.IsThinking ? thinking : answer).Append(part.Text);
        }

        return thinking.Length == 0 ? (null, content) : (thinking.ToString(), answer.ToString());
    }

    /// <summary>Takes the next chunk of answer text.</summary>
    /// <param name="chunk">The chunk as streamed.</param>
    /// <returns>The pieces that are now settled, in order, each marked thinking or answer.</returns>
    public IReadOnlyList<(bool IsThinking, string Text)> Push(string chunk)
    {
        if (chunk.Length == 0) return [];
        if (state == SplitState.Answer) return [(false, chunk)];

        pending.Append(chunk);
        var parts = new List<(bool IsThinking, string Text)>();
        Drain(parts, final: false);
        return parts;
    }

    /// <summary>Settles whatever is held back at the end of the stream.</summary>
    /// <returns>The remaining pieces.</returns>
    public IReadOnlyList<(bool IsThinking, string Text)> Flush()
    {
        var parts = new List<(bool IsThinking, string Text)>();
        Drain(parts, final: true);
        return parts;
    }

    private void Drain(List<(bool IsThinking, string Text)> parts, bool final)
    {
        while (pending.Length > 0)
        {
            var text = pending.ToString();
            switch (state)
            {
                case SplitState.Start:
                    var trimmed = text.TrimStart();
                    if (trimmed.StartsWith(OpenTag, StringComparison.Ordinal))
                    {
                        Take(text.Length - trimmed.Length + OpenTag.Length);
                        state = SplitState.InBlock;
                        continue;
                    }

                    if (!final && OpenTag.StartsWith(trimmed, StringComparison.Ordinal)) return;

                    state = SplitState.Answer;
                    continue;

                case SplitState.InBlock:
                    var close = text.IndexOf(CloseTag, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        AddPart(parts, true, Take(close));
                        Take(CloseTag.Length);
                        state = SplitState.AfterBlock;
                        continue;
                    }

                    var settled = final ? text.Length : text.Length - PartialTagLength(text, CloseTag);
                    AddPart(parts, true, Take(settled));
                    return;

                case SplitState.AfterBlock:
                    var answerStart = text.Length - text.TrimStart().Length;
                    Take(answerStart);
                    if (pending.Length == 0) return;
                    state = SplitState.Answer;
                    continue;

                default:
                    AddPart(parts, false, Take(text.Length));
                    return;
            }
        }
    }

    private string Take(int length)
    {
        var taken = pending.ToString(0, length);
        pending.Remove(0, length);
        return taken;
    }

    private static void AddPart(List<(bool IsThinking, string Text)> parts, bool isThinking, string text)
    {
        if (text.Length > 0) parts.Add((isThinking, text));
    }

    /// <summary>How many characters at the end of <paramref name="text"/> could be the start of <paramref name="tag"/>.</summary>
    private static int PartialTagLength(string text, string tag)
    {
        for (var length = Math.Min(tag.Length - 1, text.Length); length > 0; length--)
        {
            if (text.AsSpan(text.Length - length).SequenceEqual(tag.AsSpan(0, length))) return length;
        }

        return 0;
    }
}
