# ONNX Runtime feedback — found while building TechieRag

| | |
|---|---|
| App | TechieRag |
| Upstream | ONNX Runtime (microsoft/onnxruntime) |
| Updated | 2026-10-07 (ORT-001 rewritten as a ready-to-post GitHub issue; re-checked on 1.30.0) |

## Summary

1 entry: 0 blocking now, 1 open and not blocking, 0 fixed upstream.

Nothing is blocked; a workaround is in place. We do not maintain ONNX Runtime, so each entry ends with a GitHub issue to post as written at https://github.com/microsoft/onnxruntime/issues/new/choose. The issue text uses only the public package and a minimal stand-alone project. It names none of our code, packages or requirement ids.

## Entries

### ORT-001 — The NuGet package links nothing on Mac Catalyst, although it carries a Mac Catalyst build

- **Severity:** major
- **Blocks:** no — a workaround in our own build files adds the missing native reference, and inference runs in a Mac Catalyst app
- **Repro:** a `net10.0-maccatalyst` app that references `Microsoft.ML.OnnxRuntime` 1.30.0 and creates an `InferenceSession`
- **Expected:** the package's Mac Catalyst build is linked, the same way its iOS build is
- **Actual:** `build/net9.0-maccatalyst18.0/` and `buildTransitive/net9.0-maccatalyst18.0/` hold only `_._`, so no native code is linked and the link fails. The Mac Catalyst build exists: `runtimes/ios/native/onnxruntime.xcframework.zip` contains `ios-arm64_x86_64-maccatalyst/`
- **Encountered in:** first seen with 1.24.1; re-checked in the 1.30.0 package on 2026-10-07
- **Workaround:** the iOS targets file's `NativeReference`, declared for `maccatalyst` in the app's project, plus an empty native `RegisterCustomOps` for Debug builds only
- **Suggested fix:** ship `Microsoft.ML.OnnxRuntime.targets` for `net9.0-maccatalyst18.0` (both `build/` and `buildTransitive/`) with the iOS file's `NativeReference`, pointing at the same xcframework

Internal tracking, not part of the issue: REQ-FN-055.

#### GitHub issue (post as written)

Template: **Bug Report**. Title:

```
NuGet: Mac Catalyst targets are an empty placeholder although the iOS xcframework contains a maccatalyst slice
```

Body:

````markdown
### Describe the issue

`Microsoft.ML.OnnxRuntime` 1.30.0 ships a Mac Catalyst build of the native library, but the NuGet package never links it into a Mac Catalyst app.

- `runtimes/ios/native/onnxruntime.xcframework.zip` contains three slices: `ios-arm64`, `ios-arm64_x86_64-simulator` and `ios-arm64_x86_64-maccatalyst`.
- For iOS, `buildTransitive/net9.0-ios18.0/Microsoft.ML.OnnxRuntime.targets` adds that xcframework as a `NativeReference`.
- For Mac Catalyst, `build/net9.0-maccatalyst18.0/` and `buildTransitive/net9.0-maccatalyst18.0/` contain only `_._`. Nothing adds the `NativeReference`.
- The managed assembly for `net9.0-maccatalyst18.0` imports its native functions from `__Internal`, so the app needs the static library linked in.

The Mac Catalyst app therefore has no ONNX Runtime native code. The native link fails on the first unresolved `__Internal` entry point, for example `_RegisterCustomOps`.

First seen with 1.24.1. The 1.30.0 package was checked again on 2026-10-07 and is unchanged.

**Expected:** a Mac Catalyst app links the xcframework's `maccatalyst` slice, the same way an iOS app links its slice, with no extra project settings.

**Suggested fix:** ship `Microsoft.ML.OnnxRuntime.targets` for `net9.0-maccatalyst18.0` in both `build/` and `buildTransitive/`, with the same `NativeReference` the iOS file declares.

**Second, smaller point.** The managed assembly declares a P/Invoke to `RegisterCustomOps` (for `OrtExtensions.RegisterCustomOps`). No ONNX Runtime binary defines that symbol, because it comes from onnxruntime-extensions. A Release build trims the unused P/Invoke and links. A Debug build keeps it and fails with an undefined `_RegisterCustomOps` unless the app also references `Microsoft.ML.OnnxRuntime.Extensions` or defines the symbol itself.

### To reproduce

1. On a Mac with Xcode and the .NET 10 SDK with the `maccatalyst` workload, create a project:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0-maccatalyst</TargetFramework>
       <OutputType>Exe</OutputType>
       <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.30.0" />
     </ItemGroup>
   </Project>
   ```

2. Create any session, for example with a small model file copied to the app bundle:

   ```csharp
   using Microsoft.ML.OnnxRuntime;

   using var session = new InferenceSession(Path.Combine(AppContext.BaseDirectory, "model.onnx"));
   Console.WriteLine(session.InputMetadata.Count);
   ```

3. Run `dotnet build -c Debug` (or `-c Release`).

**Result:** the native link fails with undefined `_RegisterCustomOps` and no ONNX Runtime symbols. You can confirm the package side directly: `build/net9.0-maccatalyst18.0/` and `buildTransitive/net9.0-maccatalyst18.0/` contain only `_._`.

**Workaround that confirms the cause.** Add the iOS targets file's item, conditioned on Mac Catalyst, to the app project. The app then links and runs inference:

```xml
<ItemGroup Condition="'$([MSBuild]::GetTargetPlatformIdentifier($(TargetFramework)))' == 'maccatalyst'">
  <NativeReference Include="$(NuGetPackageRoot)microsoft.ml.onnxruntime/1.30.0/runtimes/ios/native/onnxruntime.xcframework.zip">
    <Kind>Static</Kind>
    <IsCxx>True</IsCxx>
    <SmartLink>True</SmartLink>
    <ForceLoad>True</ForceLoad>
    <LinkerFlags>-lc++</LinkerFlags>
    <WeakFrameworks>CoreML</WeakFrameworks>
  </NativeReference>
</ItemGroup>
```

With this item a Release build links and runs. A Debug build still needs `RegisterCustomOps` defined somewhere, as described above.

### Urgency

Not blocking: the workaround above works. Every Mac Catalyst app that uses the package needs it, though, and the error does not point to the cause.

### Platform

Mac

### OS Version

<!-- fill in: the macOS version of the build Mac, e.g. from `sw_vers` -->

### ONNX Runtime Installation

Released Package

### ONNX Runtime Version or Commit ID

1.30.0 (also seen in 1.24.1)

### ONNX Runtime API

C#

### Architecture

ARM64

### Execution Provider

Default CPU

### Execution Provider Library Version

_No response_
````

Before posting, fill in the macOS version (the single `OS Version` placeholder). Nothing else needs changing.

## Replies from ONNX Runtime
