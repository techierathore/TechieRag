# TechieRag — decisions I needed from you (answered 2026-11-19)

| | |
|---|---|
| App | TechieRag |
| Written | 2026-11-05; decision 2 added 2026-11-19 (your earlier answers are in `docs/OldDocs/TechieRag-Decision-Request-2026-11-05-gpu-engine.md`) |
| Waiting on | 2 decisions. Everything else of the picture work is built, and everything else of the 1.3.0 feedback round is built. |

## What happened

You chose DirectML, in a separate package, for the graphics-card picture model. When I came to build it, I found that Microsoft stopped publishing the DirectML engine package at version 0.14.1. This library runs engine version 0.16.0, and the two cannot be mixed in one app. From 0.17.0 Microsoft publishes two graphics-card packages instead: WinML (Windows' own machine-learning layer) and CUDA (NVIDIA cards only).

The laptop picture model is built and works on this laptop. Asking for the graphics-card model now refuses before any download, with a message saying why. Only loading it on a graphics card is missing. This laptop has an Intel Iris Xe and no NVIDIA card, so a CUDA build could not be tried here at all.

## What I need you to decide

### 1. How the graphics-card picture model runs, now that DirectML is gone

| Option | What happens | What it costs |
|---|---|---|
| **A — WinML, in a separate package** | The closest thing to DirectML: Windows' own layer, meant for any Windows graphics card | Every local model moves from engine 0.16.0 to 0.17.0 or later, and each needs re-testing. Whether Microsoft's Phi-4 build runs on it is not known yet |
| **B — CUDA, in a separate package** | Fastest on NVIDIA cards, on Windows and Linux | NVIDIA only; the same engine move; nothing here to test it on |
| **C — Leave the graphics-card model for later** | The laptop model ships now. The graphics-card model stays listed and refuses with its clear message | Machines with a graphics card read pictures at laptop speed, about 6 minutes a page here |

**My recommendation: C.** The laptop model already does Sevak's job with nothing leaving the machine. A and B both mean moving every local model to a new engine version, with nothing on this laptop to prove the graphics-card result. That move is better done as its own piece of work.


### 2. Whether the built-in models should run on Intel Macs (MyDiary TR-RAG-003)

MyDiary wants its free on-device assistant on Intel Macs too. Today neither engine we use publishes an Intel Mac build: the one behind the built-in language model has none, and the one behind the built-in search model dropped it this year. So on an Intel Mac neither the built-in assistant nor built-in search works. Every other platform is unaffected.

| Option | What happens | What it costs |
|---|---|---|
| **A — Add a second engine for Intel Macs only** | llama.cpp (the engine we compared in September) runs the models on Intel Macs; every other platform keeps today's engine | Reverses your one-engine decision of 25 September; a second set of model files to download on those Macs; nothing here to build or test it on (it needs an Intel Mac or a paid cloud Mac) |
| **B — Leave Intel Macs out** | Intel Macs keep using a model on another machine (Ollama, LM Studio) or a hosted one; MyDiary's free on-device feature stays off there | Intel Mac owners do not get the built-in assistant |

**My recommendation: B.** Apple stopped selling Intel Macs in 2023, the engine makers have dropped them, and option A doubles the engine work for a shrinking group, with no way to test it here.

## What I do when you answer

1. Record the choice in the Architecture decisions log.
2. For A or B: add the requirement for the engine move, build the package, and test it on a machine that has the card. For C: mark the graphics-card load as deferred, so the phase can close.
3. Decision 2, A: plan the Intel Mac engine as its own piece of work, with a Mac to test on. B: mark REQ-RAG-173 as won't-do and tell MyDiary.

## Copy this back to me

```
TechieRag: decision 1 — go with option C. Ship the laptop picture model now and leave the graphics-card model for a later engine upgrade.
```

```
TechieRag: decision 2 — go with option B. Leave Intel Macs out of the built-in models.
```

## Your answers, 2026-11-19

- Decision 1: option C. The laptop picture model ships; the graphics-card model waits for a later engine upgrade (REQ-RAG-146 set to N/A, BRD-204 deferred).
- Decision 2: option B. Intel Macs are left out of the built-in models (REQ-RAG-173 set to N/A; MyDiary told under its TR-RAG-003).
