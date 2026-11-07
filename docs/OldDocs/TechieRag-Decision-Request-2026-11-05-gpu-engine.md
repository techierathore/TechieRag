# TechieRag — decisions I need from you

| | |
|---|---|
| App | TechieRag |
| Written | 2026-11-05 |
| Waiting on | Nothing. Answered 2026-11-05: decision 1 option A, and the four choices below; recorded in the Architecture decisions log. |

## What happened

You asked for the local package to read pictures with a model on the user's own machine, naming one model for a laptop with no graphics card and one for a machine with one. I checked Microsoft's published builds on Hugging Face today. For laptops, Phi-3.5 vision has a build that runs on the processor (about 3.2 GB). For machines with a graphics card, Phi-4 multimodal reads pictures better, but Microsoft publishes it only as a graphics-card build (about 5.1 GB).

The local package today ships only the engine that runs on the processor. A graphics-card build needs a second engine package, and there are two kinds. The choice decides which machines the graphics-card model works on and how big an app's download gets, so it is yours.

## What I need you to decide

### 1. Which graphics-card engine runs the graphics-card picture model

| Option | What happens | What it costs |
|---|---|---|
| **A — DirectML, in a separate opt-in package** | Works on any Windows graphics card: NVIDIA, AMD and Intel. An app that wants it adds one more package; every other app stays the same size | Windows only; a little slower than CUDA on NVIDIA cards |
| **B — CUDA, in a separate opt-in package** | Fastest on NVIDIA cards, on Windows and Linux | NVIDIA only, and the user's machine needs NVIDIA's own runtime installed |
| **C — No graphics-card model for now** | Only the laptop model ships; the graphics-card requirement waits | Nothing to build now; machines with a graphics card read pictures more slowly |

**My recommendation: A.** Sevak runs on Windows laptops and desktops of every make, and an opt-in package keeps apps that never read pictures unchanged.

**Your answer (2026-11-05): A.** DirectML, in a separate opt-in package.

**Your other answers, the same day:**

| Question | Your answer |
|---|---|
| Picture reading on phones | Stays out of this phase |
| A default picture model | `UseLocalLlm()` picks Phi-3.5 vision when an app asks for pictures and names no model |
| The calendar-page test | Gated like the other live local-model tests: it runs only when they are switched on |
| The fixed calendar picture | I make it myself |

## What I do when you answer

1. Record the choice in the Architecture decisions log.
2. Build the picture-reading work in phase 3 with `*build-phase TechieRag`, with the graphics-card model on DirectML in its own package.

## Copy this back to me

```
TechieRag: decision 1 — go with option A. The graphics-card picture model runs on DirectML, in a separate opt-in package.
```
