# TechieRag feedback — found while building Chatur

| | |
|---|---|
| App | Chatur |
| Upstream | TechieRag |
| Updated | 2026-09-22 |

## Summary

2 entries: 1 blocking now, 1 filed and not blocking, 0 fixed upstream.

REQ-FN-016 ("Sign in to a ChatGPT subscription") is blocked. Nothing else is blocked.

## Entries

### TR-RAG-001 — No browser-based subscription sign-in for any connector

- **Severity:** blocker
- **Blocks:** yes — REQ-FN-016, "Sign in to a ChatGPT subscription" (Settings ▸ Model providers)
- **Repro:** `.techierag/TechieRag-AI-Reference.md` — every `TechieRagBuilder` LLM method
  (`UseAnthropicLlm`, `UseOpenAICompatibleLlm`, `UseGeminiLlm`, `UseOllamaLlm`, `UseLmStudioLlm`,
  `UseAzureAIFoundryLlm`) takes a pasted key, a local endpoint, or nothing — none opens a browser for
  an OAuth-style subscription sign-in.
- **Expected:** A way to start a browser sign-in flow for a subscription-based source (e.g. a ChatGPT
  Plus/Pro account) and receive back a token or session TechieRag can then use for completions.
- **Actual:** The only "no key" path is a local server (Ollama/LM Studio) with no sign-in step at
  all. There is no `LlmSource` value, builder method, or documented flow for a hosted subscription.
- **Encountered in:** REQ-FN-016
- **Workaround:** None. `Chatur.Core.Models.ProviderActions.AddSubscriptionAsync` throws
  `NotSupportedException` naming this entry; the checklist row is `Blocked`, not worked around.
- **Suggested fix:** A builder method such as `UseChatGptSubscriptionLlm(deviceCodeCallback)` (or any
  browser/device-code OAuth flow) that yields an `ILlmProvider` once the flow completes, so a host
  application can drive the browser step itself and hand the result to TechieRag.

#### Detail

Chatur's other two provider kinds work today: `AddWithKeyAsync` (pasted key — `UseAnthropicLlm` /
`UseOpenAICompatibleLlm` / `UseGeminiLlm`) and `AddLocalAsync` (`UseOllamaLlm`, no sign-in). Only the
third kind the mockup (`mockups/settings-providers.html`, `data-testid="signin-browser"`) shows is
unreachable through TechieRag. Everything else about Chatur's provider/routing/model layer
(REQ-FN-014, 015, 017–023) is unaffected and built.

### TR-RAG-002 — `ChatStreamAsync` cannot surface a tool call, so a tool-using turn can't stream

- **Severity:** minor
- **Blocks:** no — the agent loop calls `ChatAsync` (non-streaming) whenever tools are offered, so
  every tool call still passes Chatur's guards; the real, finished answer is then paced out to the
  UI in small pieces so the conversation still visibly grows (REQ-FN-027). The work carried on.
- **Repro:** `.techierag/TechieRag-AI-Reference.md` — `ILlmProvider.ChatStreamAsync` returns
  `IAsyncEnumerable<string>` (plain text only); `ToolCalls`/`HasToolCalls` exist only on
  `ChatAsync`'s `LlmResponse`.
- **Expected:** A streaming call that also yields when the model decides to call a tool, so a
  tool-using conversation turn can still stream its assistant text token by token instead of
  only being usable once the whole non-streaming response is back.
- **Actual:** There is no streaming path that reports a tool call at all — a caller must choose
  between real token streaming (`ChatStreamAsync`, no tool calls) or tool calls (`ChatAsync`, no
  streaming), never both in the same turn.
- **Encountered in:** REQ-FN-025, REQ-FN-027, REQ-FN-036
- **Workaround:** `Chatur.Core.Agent.AgentActions.SendAsync` uses `ChatAsync` for every turn so
  tool calls run through the guard pipeline, then hands the finished, real reply text back to the
  caller in small pieces (`PaceReplyAsync`) rather than inventing or altering any of it.
- **Suggested fix:** A `ChatStreamAsync` (or `AskStreamAsync`) overload that yields a small
  discriminated event per piece — a text delta, or a decided tool call — the way OpenAI's own
  streaming chat-completions API reports `tool_calls` deltas alongside content deltas.

## Replies from TechieRag

<!-- The upstream team's answers, newest block first. Left in full: this is the record. -->

### 2026-09-24 — both entries accepted

**TR-RAG-001 (subscription sign-in) — accepted, tracked as TechieRag BRD-112, BRD-113, BRD-114 (checklist `REQ-RAG-069`, `REQ-RAG-070`, `REQ-FN-062`).** The library gets one builder method per vendor whose terms permit it, starting with OpenAI as `UseChatGptSubscriptionLlm(signInCallback)`, exactly the shape you suggested: the library hands your callback the sign-in URL and user code, Chatur opens the browser, the library waits and yields an `ILlmProvider` billed to the user's subscription. A session-store seam lets Chatur persist the session so the user signs in once. Vendor terms are recorded as dated text in `LlmConnectorCatalog` so your Settings screen can show them before sign-in. Two facts you should know now: OpenAI permits this in external tools for personal use; Anthropic prohibits it for Free, Pro and Max plans since April 2026, so there will be no Anthropic subscription method. Google, Groq, xAI and Meta are being researched before build (BRD-114). Your `AddSubscriptionAsync` can stay `NotSupportedException` until the package ships; the mockup's `signin-browser` control needs no change.

**TR-RAG-002 (streaming with tool calls) — accepted, tracked as TechieRag BRD-110 and BRD-111 (checklist `REQ-RAG-067`, `REQ-RAG-068`).** `ILlmProvider` gains an additive streaming method yielding typed events: a text delta, a decided tool call (name, arguments, id), and a final record with usage and finish reason, the way you described OpenAI's own `tool_calls` deltas. `ChatStreamAsync` stays unchanged, so nothing in Chatur breaks. All six providers implement it, `AgentLoopRunner` gets a streaming run over it, and `TechieRag.Local` (the new in-process provider) is written against it from day one. When it lands, `AgentActions.SendAsync` can drop `PaceReplyAsync` and stream the real tokens while every tool call still passes your guards.

Decision record: `docs/TechieRag-Update-Brief.md` (TechieRag repository), decisions 8 and 9.
