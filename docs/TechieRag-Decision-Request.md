# TechieRag — decisions I need from you

| | |
|---|---|
| App | TechieRag |
| Written | 2026-09-24 |
| Waiting on | 4 decisions. Nothing has been changed yet (decision 4 keeps what is built today). |

## What happened

I built the new `TechieRag.Local` package up to the point where a real inference engine plugs in: the one public provider, the model list, the terms-first download with its SHA-256 check, the memory check, the chat formats, the refusal of over-long prompts, and the hook that lets `local/<model>` work from configuration. All of it is tested against a stand-in engine.

The decision log says the engine for each platform (LLamaSharp or ONNX Runtime GenAI) is chosen from measured numbers before the real engine goes in, and the status page says that choice is yours. So I measured both on your Windows 11 laptop (Mi NoteBook Pro, Core i5-11300H, 16 GB), in Windows and in its Linux subsystem, with the same two models in each engine's own format, and checked on nuget.org which platforms each engine actually ships for. Phones and the Mac need your devices. The raw numbers are in `tests/.artifacts/local-llm-bench/`.

## What I need you to decide

### 1. Which engine runs the local model on Windows, Android and iPhone

On this laptop ONNX Runtime GenAI 0.16.0 answered faster than LLamaSharp 0.27.0 with both models. Small phone model (Qwen2.5 0.5B, a 128-token answer; a token is a word or piece of a word): Windows 45–52 against 25–31 tokens per second, first token after 0.2–0.4 s against 0.4–0.8 s; Linux best runs 86 against 64. Desktop model (Phi-3 mini): Windows 7.5 against 5.9, Linux 8.4 against 6.7. LLamaSharp loads 2–4 times faster (0.4–1.5 s against 1.1–3.3 s for the small model) and used 510 MB against 555 MB for the small model, but 3.6 GB against 3.0–3.3 GB for Phi-3. Both engines count tokens identically on every test sentence. LLamaSharp ships nothing for iPhone; ONNX Runtime GenAI ships for Windows, Android and iPhone.

| Option | What happens | What it costs |
|---|---|---|
| **A — ONNX Runtime GenAI everywhere it ships** | One engine for Windows, Android and iPhone; it shares ONNX Runtime with the embedding package | The embedding package's ONNX Runtime moves from 1.24.1 to 1.30.0; downloads are 0.9 GB and 2.7 GB |
| **B — LLamaSharp on Windows and Android, ONNX Runtime GenAI on iPhone** | Faster loading and smaller downloads (0.5 GB and 2.4 GB) where LLamaSharp ships | Two engines to keep working; slower answers on this laptop |

**My recommendation: A** — it is faster where it matters (answer speed), covers three platforms with one engine, and is the only one that runs on an iPhone.

### 2. What the Mac app does about the local model

Neither engine ships a build for Mac Catalyst, the way a MAUI app runs on a Mac: ONNX Runtime GenAI's package holds an empty placeholder there, LLamaSharp has nothing. I will not build one by hand.

| Option | What happens | What it costs |
|---|---|---|
| **A — Not supported on the Mac for now** | The support table says "not supported"; the Mac app uses a server model such as LM Studio; I file the gap with ONNX Runtime GenAI | Mac users get no in-app model until the vendor ships one |
| **B — Wait on the Mac before shipping anywhere** | Nothing ships until a Mac build exists | Windows, Android and iPhone wait too |

**My recommendation: A** — the other three platforms should not wait on a vendor.

### 3. Where the small model's ONNX files come from

Qwen publishes the small model's LLamaSharp file itself (Apache-2.0). Its ONNX Runtime GenAI version exists only as a copy made by one person (`xiaoyao9184`), with no licence tag on the copy. I pinned the exact files and their fingerprints, so nothing can change under us, but the source is a stranger.

| Option | What happens | What it costs |
|---|---|---|
| **A — Host your own copy** | I convert the model with Microsoft's own tool and you put the files on your download mirror (`TECHIERAG_MODEL_BASE_URL`, or a new default address) | About an hour of work, plus somewhere to host 0.9 GB |
| **B — Keep the pinned copy** | Works today; fingerprints stop a swapped file | The files come from someone we do not know |

**My recommendation: A** — the default download on every phone should come from a source you control.

### 4. Which sign-in id the ChatGPT subscription sign-in uses

`UseChatGptSubscriptionLlm` is built and tested against a stand-in OpenAI server. To sign in, OpenAI's server needs an id that says which program is asking. OpenAI gives these ids to no outside developer. The only one that exists is the id of OpenAI's own Codex tool, and OpenAI staff have said in public that other tools may sign in with it (sources in `DECISIONS.md`, 2026-09-24 research entry). The library uses that id by default, and a host can set its own through `ChatGptSubscriptionOptions.ClientId`. It does not pretend to be Codex in any other way: it names itself `techierag` on every call.

| Option | What happens | What it costs |
|---|---|---|
| **A — Keep the Codex id as the default** | ChatGPT sign-in works for any app that uses the library | It rests on public statements by OpenAI staff, not on a written term; if OpenAI changes its mind, sign-in stops working until you change the id |
| **B — No default id** | Each host app must supply an id itself | In practice nobody can, since OpenAI issues none, so the feature is unusable |
| **C — Remove the ChatGPT sign-in** | Only API-key providers remain | The Chatur request (TR-RAG-001) is not met |

**My recommendation: A** — it is the only option that works today, it is what OpenAI has said other tools may do, and the id can be changed in one line.

## What I do when you answer

1. Write your choices, with these numbers, into `DECISIONS.md` as the recorded comparison.
2. Add the chosen engine's package to `TechieRag.Local`, one runtime class per platform, and its native wiring for each app head (the `buildTransitive` targets).
3. Point the small model's ONNX files at your mirror, if you chose that.
4. Run the conformance suite and the live tests against the real engine here, then the probe's second button on Windows and the Android emulator; you run it on your phone, iPhone and Mac.
5. Update the support table with the per-device numbers.

About one working session for steps 1 to 4 on Windows and Android.

## Copy this back to me

```
TechieRag: decision 1 — go with option A. Use ONNX Runtime GenAI for the local model on Windows, Android and iPhone.
```

```
TechieRag: decision 2 — go with option A. The local model is not supported on the Mac for now; file the gap with ONNX Runtime GenAI.
```

```
TechieRag: decision 3 — go with option A. Convert the small model to ONNX yourself and I will host the files on my mirror.
```

```
TechieRag: decision 4 — go with option A. Keep the Codex sign-in id as the default for the ChatGPT sign-in.
```
