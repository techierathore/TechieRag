# TechieRag AI Agent Auto-Distribution Guide

Technical reference for the AI Agent skill files, API reference documentation, and the NuGet auto-distribution mechanism that deploys them to consumer projects.

---

## Table of Contents

1. [Overview](#overview)
2. [What Was Implemented](#what-was-implemented)
3. [File Inventory](#file-inventory)
4. [Architecture: How Auto-Distribution Works](#architecture-how-auto-distribution-works)
5. [NuGet Package Internal Structure](#nuget-package-internal-structure)
6. [MSBuild Targets Explained](#msbuild-targets-explained)
7. [csproj Packaging Configuration](#csproj-packaging-configuration)
8. [Consumer Project Experience](#consumer-project-experience)
9. [Maintenance: Updating Files](#maintenance-updating-files)
10. [Verification Steps](#verification-steps)
11. [Troubleshooting](#troubleshooting)
12. [Design Decisions](#design-decisions)

---

## Overview

TechieRag distributes three AI agent files to consumer projects automatically via the NuGet package. When a developer installs the TechieRag NuGet package and builds their project, MSBuild targets copy these files into well-known directories so that AI coding agents (Claude Code, OpenCode) can immediately assist with TechieRag integration.

**Goal:** Any developer who installs TechieRag gets AI agent support out of the box - zero manual setup.

---

## What Was Implemented

Three categories of work:

### A. AI Agent Content Files (3 files)

| File | Purpose | Target Audience |
|------|---------|-----------------|
| `TechieRag-AI-Reference.md` | Complete API reference - interfaces, builder methods, models, configuration, patterns | AI agents (Claude, OpenCode, Copilot) |
| `techierag-claude-command.md` | Claude Code `/techierag` skill - BMAD-style YAML agent persona with 13 commands | Claude Code users |
| `techierag-opencode-command.md` | OpenCode `/techierag` skill - prose-formatted with frontmatter, same capabilities | OpenCode users |

### B. MSBuild Targets File (1 file)

| File | Purpose |
|------|---------|
| `TechieRag.targets` | MSBuild target definitions that auto-deploy the 3 content files to consumer project directories on every build |

### C. NuGet Packaging Configuration (csproj changes)

| File | Change |
|------|--------|
| `TechieRag.csproj` | Added `ItemGroup` entries to pack the targets file and content files into the NuGet package under `buildTransitive/` and `build/` paths |

---

## File Inventory

### Source Files in TechieRag Repository

```
TechieRag/
├── docs/
│   └── TechieRag-AI-Reference.md          ← Source/maintenance copy (965 lines)
│
└── src/TechieRag/
    ├── TechieRag.csproj                    ← Modified (added packaging ItemGroup)
    └── build/
        ├── TechieRag.targets               ← MSBuild targets for auto-deployment
        └── content/
            ├── TechieRag-AI-Reference.md    ← Packaged copy (identical to docs/ version)
            ├── techierag-claude-command.md   ← Claude Code skill file
            └── techierag-opencode-command.md ← OpenCode skill file
```

**Important:** Two copies of `TechieRag-AI-Reference.md` exist:
- `docs/TechieRag-AI-Reference.md` - Source copy for human editing and repo reference
- `src/TechieRag/build/content/TechieRag-AI-Reference.md` - Copy that gets packed into NuGet

These MUST be kept in sync manually (see [Maintenance](#maintenance-updating-files) section).

### Deployed Files in Consumer Projects (after build)

When a consumer project installs TechieRag NuGet and builds:

```
ConsumerProject/
├── .techierag/
│   └── TechieRag-AI-Reference.md    ← API reference for AI agents
├── .claude/
│   └── commands/
│       └── techierag.md              ← Claude Code skill (activates via /techierag)
└── .opencode/
    └── command/
        └── techierag.md              ← OpenCode skill (activates via /techierag)
```

---

## Architecture: How Auto-Distribution Works

The mechanism uses the standard NuGet **buildTransitive targets** pattern:

```
┌─────────────────────────────────────────────────────────┐
│ TechieRag NuGet Package (.nupkg)                        │
│                                                         │
│  buildTransitive/                                       │
│  ├── TechieRag.targets          ← Auto-imported by     │
│  │                                 MSBuild for any      │
│  │                                 project that         │
│  │                                 references this      │
│  │                                 package (direct OR   │
│  │                                 transitive)          │
│  └── content/                                           │
│      ├── TechieRag-AI-Reference.md                      │
│      ├── techierag-claude-command.md                     │
│      └── techierag-opencode-command.md                   │
│                                                         │
│  build/                                                 │
│  └── TechieRag.targets          ← Same file, for       │
│                                    direct references    │
│                                    (redundancy)         │
└─────────────────────────────────────────────────────────┘
          │
          │  NuGet restore places targets in global cache
          │  MSBuild auto-imports TechieRag.targets
          ▼
┌─────────────────────────────────────────────────────────┐
│ Consumer Project Build                                  │
│                                                         │
│  1. NuGet restore → targets file imported into build    │
│  2. After Build target fires:                           │
│     a. MakeDir → creates .techierag/, .claude/commands/,│
│        .opencode/command/ directories                   │
│     b. Copy → copies 3 content files from NuGet cache   │
│        to consumer project directories                  │
│     c. SkipUnchangedFiles=true → skips if already       │
│        up-to-date (fast builds)                         │
└─────────────────────────────────────────────────────────┘
```

### Why `buildTransitive` AND `build`?

- **`buildTransitive/TechieRag.targets`** - Imported when TechieRag is a **transitive** dependency (e.g., ConsumerApp references LibraryX which references TechieRag)
- **`build/TechieRag.targets`** - Imported when TechieRag is a **direct** dependency (e.g., ConsumerApp directly references TechieRag)

Both paths contain the same targets file for complete coverage.

### NuGet Convention

The filename **must** match the PackageId exactly: `TechieRag.targets` matches `<PackageId>TechieRag</PackageId>`. If these don't match, MSBuild won't auto-import the targets.

---

## NuGet Package Internal Structure

After running `dotnet pack`, the `.nupkg` file (which is a ZIP) should contain:

```
TechieRag.1.0.0.nupkg (ZIP contents):
├── TechieRag.nuspec                          ← Package metadata
├── README.md                                 ← Package readme
├── lib/
│   └── net10.0/
│       ├── TechieRag.dll                     ← Compiled library
│       └── TechieRag.xml                     ← XML documentation
├── build/
│   └── TechieRag.targets                     ← MSBuild targets (direct ref)
└── buildTransitive/
    ├── TechieRag.targets                     ← MSBuild targets (transitive ref)
    └── content/
        ├── TechieRag-AI-Reference.md         ← AI reference doc
        ├── techierag-claude-command.md        ← Claude Code skill
        └── techierag-opencode-command.md      ← OpenCode skill
```

### Verifying Package Contents

To inspect what's actually inside a packed `.nupkg`:

```powershell
# Pack the project
dotnet pack src/TechieRag/TechieRag.csproj -c Release -o ./nupkg

# Rename .nupkg to .zip and extract (or use any ZIP tool)
copy ./nupkg/TechieRag.1.0.0.nupkg ./nupkg/TechieRag.1.0.0.zip
# Then open the ZIP to verify contents

# Or use NuGet Package Explorer (GUI tool)
# Or use dotnet CLI:
dotnet nuget locals all --list
```

---

## MSBuild Targets Explained

**File:** `src/TechieRag/build/TechieRag.targets`

```xml
<Project>

  <PropertyGroup>
    <TechieRagContentDir>$(MSBuildThisFileDirectory)content\</TechieRagContentDir>

    <!--
      Resolve the repository/solution root for deploying AI agent files.
      AI tools (Claude Code, OpenCode) expect their skill files at the repo root,
      not inside individual project directories.

      Resolution order:
        1. Git repository root (walk up from project dir looking for .git)
        2. SolutionDir (set when building via .sln or Visual Studio)
        3. ProjectDir (fallback - original behavior)
    -->
    <TechieRagRepoRoot>$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '.git'))</TechieRagRepoRoot>
    <TechieRagRepoRoot Condition="'$(TechieRagRepoRoot)' == '' AND '$(SolutionDir)' != '' AND '$(SolutionDir)' != '*Undefined*'">$(SolutionDir)</TechieRagRepoRoot>
    <TechieRagRepoRoot Condition="'$(TechieRagRepoRoot)' == ''">$(ProjectDir)</TechieRagRepoRoot>
    <TechieRagRepoRoot Condition="!$(TechieRagRepoRoot.EndsWith('\')) AND !$(TechieRagRepoRoot.EndsWith('/'))">$(TechieRagRepoRoot)\</TechieRagRepoRoot>
  </PropertyGroup>

  <!-- Target 1: Deploy AI Reference Doc -->
  <Target Name="TechieRagDeployReferenceDoc" AfterTargets="Build">
    <MakeDir Directories="$(TechieRagRepoRoot).techierag" />
    <Copy SourceFiles="$(TechieRagContentDir)TechieRag-AI-Reference.md"
          DestinationFiles="$(TechieRagRepoRoot).techierag\TechieRag-AI-Reference.md"
          SkipUnchangedFiles="true" />
  </Target>

  <!-- Target 2: Deploy Claude Code Skill -->
  <Target Name="TechieRagDeployClaudeSkill" AfterTargets="Build">
    <MakeDir Directories="$(TechieRagRepoRoot).claude\commands" />
    <Copy SourceFiles="$(TechieRagContentDir)techierag-claude-command.md"
          DestinationFiles="$(TechieRagRepoRoot).claude\commands\techierag.md"
          SkipUnchangedFiles="true" />
  </Target>

  <!-- Target 3: Deploy OpenCode Skill -->
  <Target Name="TechieRagDeployOpenCodeSkill" AfterTargets="Build">
    <MakeDir Directories="$(TechieRagRepoRoot).opencode\command" />
    <Copy SourceFiles="$(TechieRagContentDir)techierag-opencode-command.md"
          DestinationFiles="$(TechieRagRepoRoot).opencode\command\techierag.md"
          SkipUnchangedFiles="true" />
  </Target>

</Project>
```

### Key MSBuild Variables

| Variable | Resolves To | Example |
|----------|-------------|---------|
| `$(MSBuildThisFileDirectory)` | Directory containing the `.targets` file itself (inside NuGet cache) | `~/.nuget/packages/techierag/1.0.0/buildTransitive/` |
| `$(TechieRagContentDir)` | Custom property - path to content files in NuGet cache | `~/.nuget/packages/techierag/1.0.0/buildTransitive/content/` |
| `$(TechieRagRepoRoot)` | Resolved repo/solution root where AI agent files are deployed. Found by walking up from project dir looking for `.git`, then falling back to `$(SolutionDir)`, then `$(ProjectDir)` | `C:\MyApp\` |
| `$(ProjectDir)` | Consumer project's directory (NOT used for deployment - only as last-resort fallback) | `C:\MyApp\src\MyApp\` |

### Behavior

| Aspect | Detail |
|--------|--------|
| **When targets run** | After every successful `Build` (via `AfterTargets="Build"`) |
| **Directory creation** | `MakeDir` creates directories if they don't exist, no-op if they do |
| **Overwrite behavior** | Always overwrites - ensures consumer always has latest version |
| **Performance** | `SkipUnchangedFiles="true"` compares timestamps; only writes when content changed |
| **No Condition checks** | Targets run unconditionally - deliberate design choice for reliability |

---

## csproj Packaging Configuration

**File:** `src/TechieRag/TechieRag.csproj`

The following `ItemGroup` was added to pack the auto-distribution files into the NuGet package:

```xml
<!-- AI Agent Files: Distributed to consumer projects via NuGet -->
<!-- On first build, TechieRag.targets auto-deploys these to the consumer project -->
<ItemGroup>
  <None Include="build\TechieRag.targets" Pack="true" PackagePath="buildTransitive\" />
  <None Include="build\TechieRag.targets" Pack="true" PackagePath="build\" />
  <None Include="build\content\TechieRag-AI-Reference.md" Pack="true" PackagePath="buildTransitive\content\" />
  <None Include="build\content\techierag-claude-command.md" Pack="true" PackagePath="buildTransitive\content\" />
  <None Include="build\content\techierag-opencode-command.md" Pack="true" PackagePath="buildTransitive\content\" />
</ItemGroup>
```

### Explanation of Each Entry

| Include (source path in repo) | PackagePath (path inside .nupkg) | Purpose |
|-------------------------------|----------------------------------|---------|
| `build\TechieRag.targets` | `buildTransitive\` | Targets for transitive package references |
| `build\TechieRag.targets` | `build\` | Targets for direct package references |
| `build\content\TechieRag-AI-Reference.md` | `buildTransitive\content\` | AI reference doc to deploy |
| `build\content\techierag-claude-command.md` | `buildTransitive\content\` | Claude skill to deploy |
| `build\content\techierag-opencode-command.md` | `buildTransitive\content\` | OpenCode skill to deploy |

**Note:** The same `.targets` file is packed into TWO locations (`build\` and `buildTransitive\`). The content files only need to be in `buildTransitive\content\` because the targets file uses `$(MSBuildThisFileDirectory)content\` which resolves relative to whichever location MSBuild loaded it from. Since `build\` doesn't have a `content\` subfolder, the targets from `build\` path would look for content in the `buildTransitive\content\` only if MSBuild resolves it there.

**Potential Issue:** If MSBuild loads from `build\TechieRag.targets` (direct reference) rather than `buildTransitive\`, the `$(MSBuildThisFileDirectory)content\` path would resolve to `build\content\` which doesn't exist in the package. Content files should also be packed under `build\content\` for full robustness. See [Troubleshooting - Content files missing for direct references](#content-files-missing-for-direct-references).

---

## Consumer Project Experience

### Step 1: Install Package

```bash
dotnet add package TechieRag
```

### Step 2: Build

```bash
dotnet build
```

**Build output should include (in verbose mode):** Three copy operations for the agent files. In normal mode, there's no visible output unless there's an error.

### Step 3: Verify Files Exist

After build, these files should exist in the consumer project directory:

```
.techierag/TechieRag-AI-Reference.md
.claude/commands/techierag.md
.opencode/command/techierag.md
```

### Step 4: Use with AI Agents

- **Claude Code:** Type `/techierag` to activate the TechieRag integration skill
- **OpenCode:** Type `/techierag` to activate the TechieRag integration skill
- **Any AI agent:** Point it to `.techierag/TechieRag-AI-Reference.md` for API reference

### Consumer .gitignore Recommendations

Consumer projects should add these to `.gitignore` (these are auto-generated files):

```gitignore
# TechieRag auto-deployed AI agent files
.techierag/
.claude/commands/techierag.md
.opencode/command/techierag.md
```

---

## Maintenance: Updating Files

### When TechieRag API Changes

If the TechieRag API is updated (new methods, new providers, new features), the agent files need updating:

1. **Edit the source copy:** `docs/TechieRag-AI-Reference.md`
2. **Copy to build content:** Copy `docs/TechieRag-AI-Reference.md` to `src/TechieRag/build/content/TechieRag-AI-Reference.md`
3. **Update skill files if needed:** Edit `src/TechieRag/build/content/techierag-claude-command.md` and `techierag-opencode-command.md`
4. **Bump version:** Update `<Version>` in `TechieRag.csproj`
5. **Pack and publish:** `dotnet pack` then push to NuGet feed
6. **Consumer gets update:** When consumer updates NuGet package and builds, new files are auto-deployed

### Keeping docs/ and build/content/ in Sync

Currently, the `TechieRag-AI-Reference.md` exists in two locations:
- `docs/TechieRag-AI-Reference.md` (human-readable source)
- `src/TechieRag/build/content/TechieRag-AI-Reference.md` (NuGet-packaged copy)

**These must be manually kept in sync.** After editing the `docs/` version, always copy it to `build/content/`.

Future improvement: A pre-pack MSBuild target could automate this copy.

### Updating Skill Commands

The skill files (`techierag-claude-command.md` and `techierag-opencode-command.md`) should be updated when:
- New TechieRag commands/features are added
- Builder API methods change
- Provider list changes
- Best practices or common mistakes change

---

## Verification Steps

### After Making Changes (Before Publishing)

#### 1. Verify Build Compiles

```bash
dotnet build src/TechieRag/TechieRag.csproj -c Release
```

Expected: 0 errors.

#### 2. Verify Pack Includes Files

```bash
dotnet pack src/TechieRag/TechieRag.csproj -c Release -o ./nupkg
```

Then inspect the `.nupkg` contents (rename to `.zip` or use NuGet Package Explorer):

- Confirm `buildTransitive/TechieRag.targets` exists
- Confirm `build/TechieRag.targets` exists
- Confirm `buildTransitive/content/TechieRag-AI-Reference.md` exists
- Confirm `buildTransitive/content/techierag-claude-command.md` exists
- Confirm `buildTransitive/content/techierag-opencode-command.md` exists

#### 3. Test in a Consumer Project

```bash
# Create a test project
mkdir TestConsumer && cd TestConsumer
dotnet new console
dotnet add package TechieRag --source /path/to/nupkg

# Build
dotnet build

# Verify deployed files
ls .techierag/
ls .claude/commands/
ls .opencode/command/
```

Expected:
- `.techierag/TechieRag-AI-Reference.md` exists
- `.claude/commands/techierag.md` exists
- `.opencode/command/techierag.md` exists

#### 4. Test Overwrite on Update

```bash
# Modify one of the deployed files in the consumer project
echo "MODIFIED" > .techierag/TechieRag-AI-Reference.md

# Rebuild
dotnet build

# Verify file was overwritten with original content
head -1 .techierag/TechieRag-AI-Reference.md
```

Expected: File should contain the original TechieRag content, not "MODIFIED".

#### 5. Verbose Build Check

```bash
dotnet build -v detailed 2>&1 | grep -i "TechieRag"
```

Expected: You should see the target names (`TechieRagDeployReferenceDoc`, `TechieRagDeployClaudeSkill`, `TechieRagDeployOpenCodeSkill`) and copy operations in the output.

---

## Troubleshooting

### Files Not Being Deployed to Consumer Project

**Symptom:** After `dotnet build`, `.techierag/`, `.claude/commands/`, or `.opencode/command/` directories don't appear.

**Check 1: Is the targets file being imported?**

```bash
dotnet build -v detailed 2>&1 | grep -i "TechieRag.targets"
```

If not found: The `.targets` file isn't in the NuGet package, or the filename doesn't match the PackageId.

**Fix:** Ensure `<PackageId>TechieRag</PackageId>` in the csproj matches the filename `TechieRag.targets` exactly (case-sensitive on Linux/macOS).

**Check 2: Are content files in the NuGet package?**

Extract the `.nupkg` (rename to `.zip`) and verify the `buildTransitive/content/` directory contains all 3 markdown files.

**Fix:** Ensure the `<None Include="..." Pack="true" PackagePath="..." />` entries in the csproj point to files that actually exist at those paths.

**Check 3: NuGet cache issue?**

```bash
# Clear NuGet cache and restore
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

**Check 4: Is it a transitive vs direct reference issue?**

If the consumer project references TechieRag directly, MSBuild uses `build/TechieRag.targets`. If transitively, it uses `buildTransitive/TechieRag.targets`. Ensure both are present.

### Content Files Missing for Direct References

**Symptom:** Targets fire but content copy fails with "file not found".

**Root Cause:** The content files are only packed under `buildTransitive/content/` but not under `build/content/`. When MSBuild imports from `build/TechieRag.targets`, `$(MSBuildThisFileDirectory)content\` resolves to `build/content/` which is empty.

**Fix:** Add content files to `build\content\` as well in the csproj:

```xml
<None Include="build\content\TechieRag-AI-Reference.md" Pack="true" PackagePath="build\content\" />
<None Include="build\content\techierag-claude-command.md" Pack="true" PackagePath="build\content\" />
<None Include="build\content\techierag-opencode-command.md" Pack="true" PackagePath="build\content\" />
```

### Targets File Not Auto-Imported

**Symptom:** Build succeeds but no TechieRag targets run.

**Possible causes:**
1. **Filename mismatch:** `TechieRag.targets` must exactly match `<PackageId>TechieRag</PackageId>`
2. **Wrong PackagePath:** Targets must be at `build/` or `buildTransitive/` root, NOT in a subdirectory
3. **Package not restored:** Run `dotnet restore` first
4. **NuGet cache stale:** Clear with `dotnet nuget locals all --clear`
5. **SDK-style project required:** The `buildTransitive` convention only works with SDK-style `.csproj` files (which is the default for .NET Core/5+)

### Files Deploy But Are Empty or Corrupted

**Symptom:** Files exist but have wrong content or are 0 bytes.

**Check:** Verify the source files in `src/TechieRag/build/content/` are correct:

```bash
wc -l src/TechieRag/build/content/*.md
```

Expected: All files should have non-zero line counts.

### Build Performance Impact

**Symptom:** Builds are slower after adding TechieRag.

The `SkipUnchangedFiles="true"` flag means files are only copied when their timestamps differ. This should add negligible overhead (milliseconds). If builds are noticeably slower:

**Check:** Ensure the content files aren't extremely large (current sizes are ~15-30 KB each, which is fine).

### Consumer Project's .gitignore Blocks Files

**Symptom:** Files are deployed but immediately disappear or aren't visible.

Some projects have aggressive `.gitignore` rules. The deployed directories (`.techierag/`, `.claude/`, `.opencode/`) might be gitignored. This is actually expected behavior - the files don't need to be in git since they're regenerated on every build.

**Note:** The files exist on disk even if gitignored. AI agents read from disk, not from git, so gitignore doesn't affect functionality.

---

## Design Decisions

### Why Overwrite on Every Build?

**Decision:** Files are always overwritten (no `Condition="!Exists(...)"` checks).

**Rationale:** When the TechieRag NuGet package is updated, consumers must get the latest skill files and reference documentation immediately. If we used existence checks, consumers would be stuck with stale files from the first install until they manually deleted them. `SkipUnchangedFiles="true"` handles the performance concern - files are only written when content actually changed.

### Why `buildTransitive` and Not `contentFiles`?

**Alternative considered:** NuGet `contentFiles` which copy files into the project at `dotnet restore` time.

**Why rejected:** `contentFiles` adds files to the project's compilation and can cause conflicts. The `buildTransitive` targets approach gives us full control over where files are placed and doesn't interfere with the consumer's build output.

### Why Separate Skill Files for Claude and OpenCode?

**Rationale:** Claude Code and OpenCode have different skill file formats:
- Claude Code uses YAML-based BMAD agent persona format
- OpenCode uses frontmatter + prose format with different metadata fields

A single file format wouldn't work for both tools.

### Why Two Copies of the Reference Doc?

**Rationale:**
- `docs/TechieRag-AI-Reference.md` - Lives with other documentation, easy to find and edit, reviewable in PRs
- `src/TechieRag/build/content/TechieRag-AI-Reference.md` - Must be inside the `src/TechieRag/` project tree for the csproj `Include` path to work with `Pack="true"`

**Future improvement:** A pre-pack build target that copies from `docs/` to `build/content/` automatically.

### Why AfterTargets="Build" and Not AfterTargets="Restore"?

**Rationale:** `Build` runs more frequently than `Restore` and is the standard hook point for deployment targets. Running after `Restore` could fail if the build itself hasn't completed yet, and `Restore` doesn't always run (it's cached).

---

*Created: 2026-02-18*
*Related: [NUGET-PUBLISHING-GUIDE.md](./NUGET-PUBLISHING-GUIDE.md) | [TechieRag-AI-Reference.md](./TechieRag-AI-Reference.md)*
