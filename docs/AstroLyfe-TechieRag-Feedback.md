# TechieRag feedback — found while building AstroLyfe

| | |
|---|---|
| App | AstroLyfe |
| Upstream | TechieRag |
| Updated | 2026-11-13 (copy created in the TechieRag repository from AstroLyfe's `docs/AstroLyfe-TechieRag-Feedback.md` of 2026-11-04; TR-RAG-001 answered: fixed for TechieRag 1.1.10) |

## Summary

1 entry: 0 blocking, 0 open, 1 fixed upstream and not yet re-checked here. TR-RAG-001 (the "model downloaded" flag) was fixed upstream on 2026-11-13 for TechieRag 1.1.10 (not yet published).

Nothing is blocked.

## Entries

### TR-RAG-001 — IsModelDownloaded() says false while the model is loaded and embedding — **fixed upstream 2026-11-13, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-13 — TechieRag REQ-RAG-163 Verified; ships in 1.1.10, not yet published. (Copied into this file on 2026-11-13 from AstroLyfe's own file.)
- **Severity:** minor
- **Blocks:** no — embedding works; only the health screen's "model downloaded" flag is wrong, and the work carried on.
- **Repro:** start AstroAPI from a fresh copy of its build output (TechieRag.Embedded 1.0.4, `EmbeddedEmbeddingProvider.CreateDefault()`), embed one rule, then call `EmbeddedEmbeddingProvider.IsModelDownloaded()`.
- **Expected:** `true`, because the provider loaded BGE-M3 and produced 1024-dimension vectors.
- **Actual:** `false`. The model files sit in the shared user cache (`~/.cache/techierag-models/bge-m3`), while the app's own `models/bge-m3` folder was created empty. The check appears to look only at the app folder.
- **Encountered in:** build-phase phase 2, verify of the Vector Index health check (test `vectorindex-api.spec.ts`, REQ-RAG-006).
- **Workaround:** none in code; the health check still reports Ready and healthy, only this flag is wrong.
- **Suggested fix:** make `IsModelDownloaded()` and `GetModelDirectory()` look in the same place the provider actually loads from, including the shared cache.

#### Detail

```
health: {"isHealthy":true,"modelStatus":"Ready","modelDownloaded":false,"modelName":"BGE-M3"}
tests/.artifacts/verify/run-6392/models/bge-m3   (empty)
~/.cache/techierag-models/bge-m3/model.onnx, model.onnx_data   (2.2 GB)
```

## Replies from TechieRag

<!-- The upstream team's answers, newest block first. -->

### 2026-11-13 — TR-RAG-001 fixed for 1.1.10

TR-RAG-001 → `TechieRag#REQ-RAG-163`, Verified 2026-11-13, ships in TechieRag 1.1.10 (not yet published; the owner tags and publishes).

- `EmbeddedEmbeddingProvider.IsModelDownloaded()` and `GetModelDirectory()` now answer for the folder the model actually runs from. That is the folder a provider in this process loaded it from (a folder passed to `new EmbeddedEmbeddingProvider(folder)` included), else the folder a provider would load it from now: the model root, or a complete copy an older version left next to the assembly. Before, `GetModelDirectory()` always named `<ModelRoot>/bge-m3`, and nothing counted a model loaded from anywhere else.
- New: `provider.ModelDirectory` (the folder this provider loaded from) and `provider.IsLoaded`. A health check can read them from the provider it holds, which is the most direct answer.
- Proven by `REQ-RAG-163 LoadedModelReadsAsDownloaded`. It points the model root at an empty folder, as a fresh build output has, loads the real bge-m3 from `~/.cache/techierag-models/bge-m3` (all the files the model needs, no `config.json`), embeds one text (1024 values), and checks the flag and the folder. On TechieRag 1.1.9, with the same steps, the model embedded and `GetModelDirectory()` named the empty root, which is your reading. It passes now. Also covered: `REQ-RAG-163 FolderNamesOlderCopyWhenOnlyItIsComplete` and `RemovedFolderStopsReadingAsDownloaded`.
- One thing could not be reproduced: how 1.0.4 came to load from `~/.cache/techierag-models`. The 1.0.4 package has no reference to that folder; it only knows `<build output>/models/bge-m3`. A link or copy from your test setup is the likely cause. From 1.0.7 on, `CreateDefault()` keeps the model under the per-user model root (`<LocalApplicationData>/TechieRag/models/bge-m3`, or `ModelRoot.Set` / `TECHIERAG_MODEL_ROOT`) and still uses a complete copy in the build output's `models/bge-m3`. So on 1.1.10 the flag, the folder and the load always agree.

**Verify from here:** move AstroAPI from TechieRag.Embedded 1.0.4 to 1.1.10. The first start downloads bge-m3 (2.3 GB) to the per-user model root, unless the model root points at a folder that already holds it. Start AstroAPI from a fresh build output, embed one rule, and read the health check. `modelDownloaded` should be `true`, and `EmbeddedEmbeddingProvider.GetModelDirectory()` should name the folder that holds `model.onnx`.
