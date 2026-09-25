# ONNX Runtime feedback — found while building TechieRag

| | |
|---|---|
| App | TechieRag |
| Upstream | ONNX Runtime (microsoft/onnxruntime) |
| Updated | 2026-09-25 |

## Summary

1 entry: 0 blocking now, 1 filed and not blocking, 0 fixed upstream.

Nothing is blocked. `TechieRag.Embedded` wires the Mac Catalyst library itself until the package does.

## Entries

### ORT-001 — The NuGet package links nothing on Mac Catalyst, although it carries a Mac Catalyst build

- **Severity:** major
- **Blocks:** no — `TechieRag.Embedded`'s buildTransitive targets add the missing `NativeReference` (REQ-FN-055); embeddings run in a Mac Catalyst app
- **Repro:** a `net10.0-maccatalyst` app referencing `Microsoft.ML.OnnxRuntime` 1.30.0 that creates an `InferenceSession`
- **Expected:** the package's Mac Catalyst build is linked, as its iOS build is
- **Actual:** `build/` and `buildTransitive/net9.0-maccatalyst18.0/` hold only `_._`; the link fails on `_RegisterCustomOps` and no native code is present. The Mac Catalyst build exists: `runtimes/ios/native/onnxruntime.xcframework.zip` contains `ios-arm64_x86_64-maccatalyst/`
- **Encountered in:** REQ-FN-055 (first seen with 1.24.1, still so in 1.30.0 on 2026-09-25)
- **Workaround:** the same `NativeReference` the iOS targets declare, declared for `maccatalyst`, plus a Debug-only stub for `RegisterCustomOps`
- **Suggested fix:** ship `buildTransitive/net9.0-maccatalyst18.0/Microsoft.ML.OnnxRuntime.targets` with the iOS file's `NativeReference`, pointing at the same xcframework

#### Detail

The Debug stub is needed because the managed assembly declares a P/Invoke to `RegisterCustomOps`, a symbol no shipped ONNX Runtime binary defines (it lives in onnxruntime-extensions); Release trims the unused P/Invoke, Debug does not. That is worth a second line in the same issue.

Git and `gh` are manual in this repository, so the owner posts this as an issue at https://github.com/microsoft/onnxruntime/issues. Suggested title: "NuGet: Mac Catalyst targets are an empty placeholder although the iOS xcframework contains a maccatalyst slice". The body is this entry's fields.

## Replies from ONNX Runtime
