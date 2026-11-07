# TechieRag — decisions I need from you

| | |
|---|---|
| App | TechieRag |
| Written | 2026-11-05 (your earlier answers today are in `docs/OldDocs/TechieRag-Decision-Request-2026-11-05-gpu-engine.md`) |
| Waiting on | 1 decision. Everything else of the picture work is built. |

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

## What I do when you answer

1. Record the choice in the Architecture decisions log.
2. For A or B: add the requirement for the engine move, build the package, and test it on a machine that has the card. For C: mark the graphics-card load as deferred, so the phase can close.

## Copy this back to me

```
TechieRag: decision 1 — go with option C. Ship the laptop picture model now and leave the graphics-card model for a later engine upgrade.
```
