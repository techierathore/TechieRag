# ONNX Runtime GenAI feedback — found while building TechieRag

| | |
|---|---|
| App | TechieRag |
| Upstream | ONNX Runtime GenAI (microsoft/onnxruntime-genai) |
| Updated | 2026-09-25 |

## Summary

2 entries: 0 blocking now, 2 filed and not blocking, 0 fixed upstream.

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

### ORTGENAI-002 — The Android `.aar` carries a `libmat.so` that is not aligned for 16 KB pages

- **Severity:** minor
- **Blocks:** no — the probe's Android head builds and generates on an Android 12 emulator (REQ-FN-058, 2026-09-25); it becomes blocking when an app must ship to devices or a store that require 16 KB page support (Android 15+ devices with 16 KB pages; Google Play's requirement for new apps targeting Android 15+)
- **Repro:** a `net10.0-android` app referencing `Microsoft.ML.OnnxRuntimeGenAI` 0.16.0 (through `TechieRag.Local`), `dotnet build -f net10.0-android`
- **Expected:** every native library in `runtimes/android/native/onnxruntime-genai.aar` is linked with 16 KB page alignment, as `libonnxruntime-genai.so` already is (no warning for it)
- **Actual:** .NET for Android 36.1.69 warns `XA0141: Android 16 will require 16 KB page sizes, shared library 'libmat.so' does not have a 16 KB page size`, naming `onnxruntime-genai.aar` (arm64-v8a and x86_64); build log `tests/.artifacts/build/20260925T111143Z-build-5133.log`
- **Encountered in:** REQ-FN-058 / REQ-RAG-058 Android probe build, 2026-09-25
- **Workaround:** none needed yet; TechieRag does not rebuild or patch the library
- **Suggested fix:** link `libmat.so` with `-Wl,-z,max-page-size=16384` (NDK r27+ does it by default) and republish the `.aar`

Git and `gh` are manual in this repository, so the owner posts this as an issue at https://github.com/microsoft/onnxruntime-genai/issues. Suggested title: "Android aar: libmat.so is not 16 KB page aligned (XA0141)".

## Replies from ONNX Runtime GenAI
