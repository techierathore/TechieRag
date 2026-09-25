# TechieRag — decisions I need from you

| | |
|---|---|
| App | TechieRag |
| Written | 2026-09-25 (decisions 1–4 of the same day are answered and recorded in `DECISIONS.md`) |
| Waiting on | Nothing. Answered 2026-09-25: both option A, recorded in `DECISIONS.md` and BRD-166. |

## What happened

You asked why the phone model has to be published under your account, and whether that ties the library down. It does, in one way: `TechieRag.Local` today offers only its two built-in models, or a model the app has already placed in a folder. It cannot download "any ONNX model from Hugging Face by name", as LM Studio can. Separately, `UseLocalLlm()` with no model named needs a default to download on phones; that default is the only reason anything must be published.

I checked what already exists and measured it on the M4 Max Mac. Microsoft and the ONNX Runtime team publish nothing phone-sized in this engine's format. onnx-community's Qwen 0.5B is in a format this engine cannot load. Arm publishes Gemma 3 1B and Llama 3.2 1B in exactly this format, tuned for Android phones, free to download. Hugging Face lists a fingerprint for every file, so a download by name can still be checked.

## What I need you to decide

### 1. Let apps download any ONNX Runtime GenAI model from Hugging Face by name

| Option | What happens | What it costs |
|---|---|---|
| **A — Add it** | An app writes `UseLocalLlm(LocalModel.FromHuggingFace("Arm/gemma-3-1b-instruct-onnx-genai-int4-emb-int8"))` (optionally a folder inside the repository and an exact version). The library lists the files, shows the model's licence for acceptance first, downloads to the app's data folder, and checks each file against the fingerprint Hugging Face publishes | One new requirement and about one build session; the app developer is responsible for the model they name |
| **B — Keep the fixed list** | Apps use the built-in models or a folder they fill themselves | Every other model needs the app to download it by its own code |

**My recommendation: A** — it removes the tie you describe, the same way LM Studio lets you pull any model, and it keeps the tamper check.

### 2. Which model a phone downloads when the app names none

Measured on the M4 Max Mac, 2026-09-25, a 128-token answer and eight short factual questions:

| | Our own Qwen2.5 0.5B | Arm's Gemma 3 1B |
|---|---|---|
| Download | 317 MB | 866 MB |
| Answer speed | 330–360 tokens/s | 150–157 tokens/s |
| Memory at peak | 0.5 GB | 1.7 GB |
| Eight questions | 7 right (says Mars is the largest planet) | 7 right (says 17 × 23 = 491); stray "▁" marks in its JSON spacing that the library would clean |
| Licence | Apache-2.0 | Gemma terms (use restrictions the user accepts) |
| Needs publishing by you | Yes, once | No |

| Option | What happens | What it costs |
|---|---|---|
| **A — Our Qwen, published once on your Hugging Face account** | Smallest, fastest, lightest default; steps in `docs/MODEL-PUBLISHING-GUIDE.md` | 15 minutes of yours, once |
| **B — Arm's Gemma 3 1B** | Nothing to publish; a known publisher | Almost three times the download and memory, half the speed, Gemma's licence terms shown to every user; older phones with 3–4 GB may refuse it |
| **C — No default on phones** | An app must name a model (by name, with 1A, or a folder) | `UseLocalLlm()` with nothing named fails on phones with a message saying so |

**My recommendation: A** — this is also what Ollama and LM Studio do for their recommended models (they publish their own copies), while 1A lets anyone choose something else.

## What I do when you answer

1. Record both choices in `DECISIONS.md`, and fold 1A into the BRD and checklist as a new requirement (`*amend-docs`).
2. Build `LocalModel.FromHuggingFace`, with tests against a stand-in server and one live download of Arm's Gemma on this Mac, the Mac app and the iPhone simulator.
3. For 2A: wait for your published address, then pin it. For 2B: pin Arm's files at their current version. For 2C: make the phone default refuse clearly.

## Copy this back to me

```
TechieRag: decision 1 — go with option A. Let apps download any ONNX Runtime GenAI model from Hugging Face by name, with its licence shown first and every file checked.
```

```
TechieRag: decision 2 — go with option A. The phone default stays our own Qwen conversion; I will publish it on my Hugging Face account.
```
