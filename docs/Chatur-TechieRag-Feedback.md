# TechieRag feedback — found while building Chatur

| | |
|---|---|
| App | Chatur |
| Upstream | TechieRag |
| Updated | 2026-10-29 (TR-RAG-008 and TR-RAG-009 fixed upstream, next release after 1.1.7; thinking extended to every provider) |

## Summary

Nothing is blocked. 9 entries: 0 open, 3 fixed upstream and not yet re-checked here (TR-RAG-006; TR-RAG-008 and TR-RAG-009, fixed 2026-10-29 for the next TechieRag release after 1.1.7), 6 closed (TR-RAG-007 closed 2026-10-24).

Chatur is on 1.1.7 since 2026-10-24 (1.1.5 from 2026-10-11). REQ-FN-071 and REQ-FN-072 are built on `ModelChooser` and Verified (2026-10-11).

## Entries

### TR-RAG-007 — OpenCode Go needs `x-opencode-session`; the `OpenAICompatible` connector does not send it because `LlmConnectorCatalog.All` omits the endpoint

> ✅ **Closed 2026-10-24** — re-checked here: Re-checked on TechieRag 1.1.7 (2026-10-24), live against OpenCode Go with kimi-k2.7-code: LlmConnectorCatalog.All now lists opencode-go with endpoint https://opencode.ai/zen/go/v1 and sessionHeader x-opencode-session; UseOpenAICompatibleLlm with only that catalog header answered 200 (no MissingSessionID), and the opencode-go connector chat answered 200 (was 401). Chatur's SessionHeaderFor fallback removed.

- **Status:** closed 2026-10-24 — fixed upstream by TechieRag 1.1.7; Chatur's `SessionHeaderFor` fallback is removed and the catalog entry is used
- **Severity:** minor
- **Blocks:** no — `TechieRagLlmProviderFactory.SessionHeaderFor` falls back to `x-opencode-session` for `opencode.ai/zen/go`, and the seeded provider uses `OpenAICompatible` with that base URL. We tried TechieRag's own `opencode-go` connector (`UseConnectorLlm`), but its `chat/completions` calls returned 401 while the model list worked, so the fallback path is kept.
- **Repro:** `OpenAICompatible` + `https://opencode.ai/zen/go/v1` without the fallback → HTTP 400 `MissingSessionID`; `opencode-go` connector → model list 200, chat 401.
- **Expected:** `LlmConnectorCatalog.All` lists OpenCode Go, or `UseConnectorLlm("opencode-go")` sends the same auth as `UseOpenAICompatibleLlm`.
- **Actual:** `SessionHeaderFor` finds no catalog entry, so no `x-opencode-session` header is sent on the generic path; the dedicated connector answers model list but returns 401 on chat completions.
- **Encountered in:** REQ-FN-021, REQ-FN-026, REQ-FN-027, REQ-FN-033–036, REQ-UI-021, REQ-UI-022, REQ-UI-032, REQ-UI-034
- **Workaround:** Removed 2026-10-24. `SessionHeaderFor` now reads only `LlmConnectorCatalog.All`; the seeded provider stays `OpenAICompatible` (the `opencode-go` connector would also work now).
- **Suggested fix:** Add OpenCode Go to `LlmConnectorCatalog.All`, or fix `opencode-go` connector auth.

### TR-RAG-008 — TechieRag drops `reasoning_content` deltas, so reasoning models stream no visible text

- **Status:** fixed upstream 2026-10-29 (REQ-RAG-137, Verified: `LlmStreamEventKind.Thinking`, `LlmResponse.Thinking`), ships in the next TechieRag release after 1.1.7 — re-check from Chatur, then close
- **Severity:** blocker on 1.1.5; minor on 1.1.7
- **Blocks:** no on 1.1.7 — the repro now returns the full answer. Yes on 1.1.5.
- **Repro:** Stream `kimi-k2.7-code` with "say hello" through `ChatStreamEventsAsync`.
- **Expected:** Forward `reasoning_content` as a thinking stream and count those tokens.
- **Actual:** On 1.1.7, 9 `TextDelta` events, then `Completed`; the text is the answer only. There is still no reasoning event kind. The raw reply for the same request carries 22 `reasoning_content` chunks, so TechieRag still drops them. `TokenUsage.ReasoningTokens` is now filled (22 of 33 output tokens) and `LlmCompletionOptions.ReasoningEffort` exists. Answers completed with `max_tokens` 64 and 2000, so the starved answer did not show on this prompt.
- **Encountered in:** REQ-FN-021, REQ-FN-026, REQ-FN-027, REQ-FN-033–036, REQ-UI-021, REQ-UI-022, REQ-UI-032, REQ-UI-034
- **Workaround:** None. The 1.1.5 workaround (test model `minimax-m3`) is gone; the web harness seeds `kimi-k2.7-code` and `kimi-k2.5-code`.
- **Suggested fix:** Add a `ReasoningContent`/`Thinking` event kind to the streaming API.

### TR-RAG-009 — OpenCode Go's minimax-m3 returns its reasoning as `<think>` text inside the answer

- **Status:** fixed upstream 2026-10-29 (REQ-RAG-138, Verified: a leading `<think>` block is split into the thinking stream), ships in the next TechieRag release after 1.1.7 — re-check from Chatur, then close
- **Severity:** minor
- **Blocks:** no — Chatur's test model is now `kimi-k2.7-code`.
- **Repro:** TechieRag 1.1.7, `UseOpenAICompatibleLlm("https://opencode.ai/zen/go/v1", key, "minimax-m3", …)`, "say hello", `ChatAsync` and `ChatStreamEventsAsync`, with `ReasoningEffort` unset, `low`, and `max_tokens` 64.
- **Expected:** The answer text holds the answer only.
- **Actual:** Every run starts with `<think>…</think>` and then the answer, in two `TextDelta` events.
- **Encountered in:** REQ-FN-021, REQ-FN-026, REQ-FN-027
- **Workaround:** Test model changed to `kimi-k2.7-code`.
- **Suggested fix:** Split a leading `<think>…</think>` block out of `content` into the reasoning stream, or document that a host must.

### TR-RAG-006 — No small-model router that chooses a model for a piece of work

> ⏳ **Re-checked 2026-10-11, released in 1.1.5** — `TechieRag.dll`/`.xml` 1.1.5 carry `ModelChooser`, `IModelChooser`, `ModelCandidate`, `ModelChoice`, `ModelChoiceSource` and `ModelChooserOptions`. Chatur upgraded; REQ-FN-071 and REQ-FN-072 are built on it and Verified, with unit tests using a fake `IModelChooser`. The "Verify from here" live run (fast vs deep with a greeting) has not been done: no local or cheap small model is set up on this machine yet. Close this entry once it has.
>
> ⏳ **Re-checked 2026-10-09, not yet released** — TechieRag 1.1.3 (the release after 1.1.2 on the feed) carries no `ModelChooser`: neither its reference nor `TechieRag.dll` contains the name. Chatur stays on 1.0.9; REQ-FN-071 and REQ-FN-072 stay Blocked until a release carries it.

- **Status:** fixed upstream 2026-10-09 (REQ-RAG-128, Verified: `ModelChooser`), ships in the next TechieRag release after 1.1.2 — re-check from Chatur, then close
- **Severity:** blocker
- **Blocks:** yes — REQ-FN-071 (automatic routing: "a model chosen by the small model inside TechieRag") cannot be built; REQ-FN-072 (turn it off, tiers and chains used again) has nothing to switch off until 071 exists.
- **Repro:** `.techierag/TechieRag-AI-Reference.md` lists only `ModelRouter.Resolve/Require` (model NAME to connector, no choosing). The 1.0.9 and 1.1.2 packages' `TechieRag.xml` (net10.0) hold no router, classifier or selector type.
- **Expected:** A call such as `Router.ChooseAsync(request, candidates)` that runs a small model (local or cheap) over the request and the candidate models (with tier, strengths, cost) and returns the chosen model, plus the reason, so the host can show and log it.
- **Actual:** Nothing in the public API picks a model from the content of a request.
- **Encountered in:** REQ-FN-071, REQ-FN-072
- **Workaround:** None. Chatur does not imitate it (a keyword or length rule in Chatur is not "the small model inside TechieRag"); both rows stay Blocked and the routing page keeps its disabled "later" switch.
- **Suggested fix:** A public `ModelChooser` (or `UseModelChooser(smallModel)`) in `TechieRag.Llm` taking the request text and a list of `{ModelId, Tier, Notes}` and returning `{ModelId, Reason}`.

### TR-RAG-003 — An OpenAI-compatible model cannot be sent extra request headers

- **Status:** closed 2026-10-01 — fixed upstream in TechieRag 1.0.9 (`UseOpenAICompatibleLlm(…, headers, sessionHeader)`, `LlmCompletionOptions.SessionId`, `opencode-go` connector). Chatur sends `User-Agent: Chatur/<version>` and the conversation id; OpenCode Go replied live.
- **Severity:** blocker
- **Blocks:** yes — the owner chose OpenCode Go (decision 1, option A); every conversation row that
  needs a real reply waits on it (REQ-FN-021, 022, 025–027, 033–036, REQ-UI-021, 022, 032, 033).
  Key-only providers are not affected.
- **Repro:** 1.0.7 and 1.0.8. `UseOpenAICompatibleLlm("https://opencode.ai/zen/go/v1", key, "kimi-k2.7-code")`
  then `ChatAsync` → HTTP 400 `MissingSessionID` ("Request is missing x-opencode-session"). The same
  request by hand with that header answers 200 (2026-09-30, owner's key).
- **Expected:** Extra request headers on an OpenAI-compatible model, including one that stays the
  same for a conversation (https://opencode.ai/docs/go/).
- **Actual:** No header option on `UseOpenAICompatibleLlm` or `LlmConfig`; the `HttpClient`
  constructor of `OpenAICompatibleLlmProvider` is internal; no `opencode` catalog entry.
- **Encountered in:** REQ-FN-025
- **Workaround:** None; the rows stay blocked.
- **Suggested fix:** `LlmConfig.Headers` (like `McpServerConfig.Headers`) plus a per-call session id
  in `LlmCompletionOptions`; or a public `HttpClient` constructor; or an `opencode-go` catalog entry.

### TR-RAG-004 — The ChatGPT subscription connector's name is not public

- **Status:** closed 2026-10-01 — fixed upstream in TechieRag 1.0.9 (`LlmConnectorCatalog.ChatGptSubscriptionName`); Chatur now uses it.
- **Severity:** minor
- **Blocks:** no — Chatur hardcodes the name and reads the terms through the public `LlmConnectorCatalog.Find`. The work carried on.
- **Repro:** TechieRag 1.0.8. `SubscriptionConnectorRows` and its `ChatGptName` are internal.
- **Expected:** A public constant for the connector key, e.g. `LlmConnectorCatalog.ChatGptSubscriptionName`.
- **Actual:** A host must write `"chatgpt-subscription"` itself and hope it never changes.
- **Encountered in:** REQ-FN-016
- **Workaround:** `ProviderActions.ChatGptCatalogName = "chatgpt-subscription"`, named after this entry.
- **Suggested fix:** Make `SubscriptionConnectorRows` (or just its names) public.

### TR-RAG-005 — No clear way to say "the subscription session is gone, sign in again" during a model turn

- **Status:** closed 2026-10-01 — fixed upstream in TechieRag 1.0.9 (`SubscriptionSignInException.CodeSignInRequired`); Chatur turns it into "not signed in any more, sign in again".
- **Severity:** minor
- **Blocks:** no — Chatur's sign-in callback throws `InvalidOperationException` during a turn, and the turn tells the owner to sign in again under Settings ▸ Providers.
- **Repro:** TechieRag 1.0.8. `UseChatGptSubscriptionLlm` needs a sign-in callback; during a model turn a host has no window to show a code in.
- **Expected:** A documented exception (or a documented rule that the callback may throw) meaning "not signed in any more", that the host can catch and turn into a "sign in again" message.
- **Actual:** Nothing documents what TechieRag does when the callback throws.
- **Encountered in:** REQ-FN-016
- **Workaround:** As above; not confirmed against TechieRag's own handling.
- **Suggested fix:** A `SubscriptionSignInException` code such as `CodeSignInRequired`, raised when a stored session is refused and no interactive callback is available.

### TR-RAG-001 — No browser-based subscription sign-in for any connector

- **Status:** closed 2026-09-30 — fixed upstream in TechieRag 1.0.8 (`UseChatGptSubscriptionLlm`, device-code sign-in, `ISubscriptionSessionStore`)
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

- **Status:** closed 2026-09-30 — fixed upstream in TechieRag 1.0.8 (`ChatStreamEventsAsync` yields `TextDelta`, `ToolCall` and `Completed`)
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

### 2026-10-29 (later) — thinking now comes from every provider

This fills the gap noted under "Not covered yet" in the reply below. It ships in the same release, the next one after 1.1.7 (REQ-RAG-139 to REQ-RAG-144). Nothing changes for OpenCode Go.

| Provider | Thinking comes from | Reasoning token count |
|---|---|---|
| `OpenAICompatible` (OpenCode Go), LM Studio, Azure AI Foundry | `reasoning_content` / `reasoning`, or a leading `<think>` block (unchanged from the reply below) | yes |
| Anthropic | its thinking blocks | 0: the service gives no separate figure |
| Gemini | its thought parts | yes, and now included in `OutputTokens` |
| Ollama | its `thinking` field, or a leading `<think>` block | 0: Ollama gives none |
| Local model (`TechieRag.Local`) | a leading `<think>` block, with tools too | counted with the model's tokenizer |
| ChatGPT subscription | its reasoning summary; the service never sends the full reasoning | yes |

The rules are the same for all of them. Thinking never appears in `TextDelta` or `Content`. A call with no thinking sends no `Thinking` event. `Completed` is still last.

**What Chatur may need to do:**
- **Asking for thinking.** Anthropic, Gemini, Ollama and the ChatGPT subscription only think when the call sets `MaxReasoningTokens` or `ReasoningEffort` in `LlmCompletionOptions`. Without either, they behave as before. `DisableReasoning` now also reaches Gemini and Ollama.
- **Anthropic tool turns.** With thinking on, Anthropic refuses a tool-use turn unless the earlier thinking is sent back. TechieRag keeps it in the new `ChatMessage.ProviderState` (also on `LlmResponse.ProviderState` and on the `Completed` event). `ToChatMessage()`, `AgentLoopRunner` and the `TechieRag.Agents` adapter carry it for you. If Chatur stores a conversation and rebuilds the messages itself, copy `ProviderState` with each assistant message; other providers ignore it.
- **Anthropic with thinking on** leaves out `temperature` and `top_p`, because the service refuses them while it thinks.

**Verify from here:** with an Anthropic key, stream a question through `ChatStreamEventsAsync` with `MaxReasoningTokens = 2048`. You should see `Thinking` events, then the answer. Then run a tool-using `AgentLoopRunner` turn with the same options; it should succeed. TechieRag tested every provider against recorded replies (tests named `REQ-RAG-139` to `REQ-RAG-143`). It has not run them against the live services: no keys or running models for them on the build machine on 2026-10-29. The per-provider table is in the AI reference section "Thinking text from reasoning models".

### 2026-10-29 — TR-RAG-008 and TR-RAG-009 fixed

**TR-RAG-008 (thinking while streaming), REQ-RAG-137.** New in TechieRag, shipping in the next release after 1.1.7:
- `LlmStreamEventKind.Thinking`, with the fragment in `LlmStreamEvent.Text`. On `OpenAICompatible` (so OpenCode Go), LM Studio and Azure AI Foundry, every `reasoning_content` (or `reasoning`) fragment now arrives as a `Thinking` event. They come before the answer's `TextDelta` events.
- `ChatAsync` returns the same text in the new `LlmResponse.Thinking`; `Content` is the answer only.
- The count stays where 1.1.7 put it: `Usage.ReasoningTokens` on the `Completed` event.
- `ChatStreamAsync` is unchanged: answer text only.
- If a `switch` over the event kind sends everything except `TextDelta` and `ToolCall` to `default:` as the end of the stream, name `Completed` there instead. Otherwise a thinking event looks like the end.

```csharp
await foreach (var e in provider.ChatStreamEventsAsync(messages, options))
{
    if (e.Kind == LlmStreamEventKind.Thinking) thinking.Append(e.Text);       // show it folded
    else if (e.Kind == LlmStreamEventKind.TextDelta) answer.Append(e.Text);
    else if (e.Kind == LlmStreamEventKind.Completed) reasoningTokens = e.Usage!.ReasoningTokens;
}
```

**TR-RAG-009 (minimax-m3 `<think>` in the answer), REQ-RAG-138.** TechieRag now splits it out for you. A `<think>…</think>` block at the start of the answer goes to `Thinking` events (and `LlmResponse.Thinking`), even when the tags arrive cut across chunks, and the answer starts at its first word. A `<think>` later in the answer is left alone. A block the model never closed (cut off by `max_tokens`) is all thinking. Nothing to do in Chatur.

**Verify from here:** stream "say hello" with `kimi-k2.7-code` through `ChatStreamEventsAsync`. You should see `Thinking` events, then `TextDelta` events holding only the answer. Then do the same with `minimax-m3`: no `<think>` in any `TextDelta`, and the block arrives as `Thinking`. TechieRag tested both against the stream shapes in these entries (tests `REQ-RAG-137 ReasoningContentStreamsAsThinking` and `REQ-RAG-138 LeadingThinkBlockStreamsAsThinking`, on all three OpenAI-dialect providers). It has not yet run them against OpenCode Go itself, because no OpenCode Go key was set on the build machine on 2026-10-29.

**Not covered yet:** Anthropic, Gemini, Ollama, the ChatGPT subscription and the local model do not send `Thinking` events yet.

### 2026-10-09 — TR-RAG-006 fixed

**TR-RAG-006 (small-model router), REQ-RAG-128.** New in `TechieRag.Llm`, shipping in the next TechieRag release after 1.1.2:
- `new ModelChooser(smallModel, options?)` takes any `ILlmProvider` as the small model, local or cheap. It implements `IModelChooser`, so your tests can pass a fixed choice.
- `ChooseAsync(request, candidates)` takes the request text and `ModelCandidate { ModelId, Tier?, Notes?, Cost? }` items. Write the text fields in Chatur's own words.
- It returns a `ModelChoice` with `ModelId`, `Candidate`, `Reason` (one sentence to show and log), `Source` and the small model's `Usage`. `Source` is `ModelChoiceSource.SmallModel`, `OnlyCandidate` or `Fallback`.
- The answer is always one of your candidates. If the reply is not JSON or names a model you did not offer, you get `ModelChooserOptions.FallbackModelId` (else the first candidate) with `Source = Fallback`.
- If the small model itself fails (down, refused, cancelled), the exception is thrown. Catch it to use your tiers and chains. That also covers REQ-FN-072's "turned off" path.
- `ModelChooserOptions.Guidance` adds your own rule. `MaxRequestCharacters` (default 4000) limits how much of the request the small model reads.

```csharp
var chooser = new ModelChooser(LlmProviderFactory.CreateForModel("ollama/llama3.2", null));
var choice = await chooser.ChooseAsync(userText, candidates);   // candidates: List<ModelCandidate>
routingLog.Write(choice.ModelId, choice.Source, choice.Reason);
```

**Verify from here:** once Chatur is on the new package, call `ChooseAsync` with two candidates, a "fast" and a "deep" one, and a one-line greeting. You should get the fast model with `Source = SmallModel` and a reason. TechieRag's tests `REQ-RAG-128 …` (6 of them) pass. A live run on 2026-10-09 with `qwen2.5-0.5b-instruct` as the small model sent a greeting to the fast model and a code review to the deep one.
