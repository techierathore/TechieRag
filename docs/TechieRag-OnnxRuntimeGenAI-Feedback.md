# ONNX Runtime GenAI feedback — found while building TechieRag

| | |
|---|---|
| App | TechieRag |
| Upstream | ONNX Runtime GenAI (microsoft/onnxruntime-genai) |
| Updated | 2026-09-25 |

## Summary

1 entry: 0 blocking now, 1 filed and not blocking, 0 fixed upstream.

Nothing is blocked. `TechieRag.Local` wires the Mac Catalyst library itself until the package does.

## Entries

### ORTGENAI-001 — The NuGet package links nothing on Mac Catalyst, although it carries a Mac Catalyst build

- **Severity:** major
- **Blocks:** no — `TechieRag.Local`'s buildTransitive targets add the missing `NativeReference` (REQ-FN-058); the local model runs in a Mac Catalyst app
- **Repro:** a `net10.0-maccatalyst` app referencing `Microsoft.ML.OnnxRuntimeGenAI` 0.16.0 and calling `new Model(new Config(dir))`
- **Expected:** the package's Mac Catalyst build is linked, as its iOS build is, and the model loads
- **Actual:** `build/net9.0-maccatalyst14.0/` and `buildTransitive/net9.0-maccatalyst14.0/` hold only `_._`, so nothing is linked and the managed `__Internal` P/Invokes have no native code. The Mac Catalyst build exists: `runtimes/ios/native/onnxruntime-genai.xcframework.zip` contains `ios-arm64_x86_64-maccatalyst/`
- **Encountered in:** owner decision 2 (DECISIONS.md 2026-09-25), REQ-FN-058
- **Workaround:** the same `NativeReference` the iOS targets declare (Static, IsCxx, ForceLoad, `-lc++`, weak CoreML), declared for `maccatalyst`; measured on an M4 Max: Qwen2.5 0.5B 360–398 tokens/s, Phi-3 mini 73 tokens/s inside a Catalyst app
- **Suggested fix:** ship `build/` and `buildTransitive/net9.0-maccatalyst14.0/Microsoft.ML.OnnxRuntimeGenAI.targets` with the iOS file's `NativeReference`, pointing at the same xcframework

#### Detail

Checked on nuget.org package 0.16.0 on 2026-09-25. The managed assembly for `net9.0-maccatalyst14.0` already imports `__Internal`, so only the MSBuild wiring is missing. `Microsoft.ML.OnnxRuntime` 1.30.0 has the same gap; that is filed separately in `docs/TechieRag-OnnxRuntime-Feedback.md`, because it is a different repository.

Git and `gh` are manual in this repository, so the owner posts this as an issue at https://github.com/microsoft/onnxruntime-genai/issues. Suggested title: "NuGet: Mac Catalyst targets are an empty placeholder although the iOS xcframework contains a maccatalyst slice". The body is this entry's fields.

## Replies from ONNX Runtime GenAI
