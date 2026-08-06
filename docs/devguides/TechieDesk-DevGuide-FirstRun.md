# TechieDesk DevGuide — First run

*Generated 2026-07-28 · reflects code as built.* [← index](./TechieDesk-DevGuide.md)

> ✅ **Runtime-verified 2026-07-29** — re-swept on the live **Mac Catalyst** head over Appium (`mac2`), bound by `appPath` to the universal Release bundle, driving **28 of the 30 screens** at **1600×1240** and **1024×720** (the REQ-UI-041 floor). Every `Observed` line below is what the running app did; `Visual (§4b)` is the overlap / zero-size / off-viewport geometry check plus a human look at the screenshot. Screens that could not be reached say so and are **not** claimed as verified. Screenshots: `test-results/ui-verify/`.


1 screen(s) in this area.

## `/setup` — Setup

- **File:** `apps/TechieDesk/Components/Pages/Setup.razor` (555 lines)
- **Reached via:** redirect from / when no workspace exists, and MainLayout.GuardFirstRunAsync:539
- **Observed:** ⚠ **NOT RUNTIME-VERIFIED (2026-07-29)** — `/setup` has no inbound link and `MainLayout.GuardFirstRunAsync` returns early whenever a workspace exists, so the only way in from this install is to delete its single `Default` workspace (3 embedded documents, connectors and run history). That is destructive to the owner's data and was **not** performed. Render-status remains unconfirmed.
- **Visual (§4b):** visual gate not run (2026-07-29) — screen not reached.

### Controls

| Component | Uses | Razor line(s) |
|---|---|---|
| `<Alert>` | 16 | 55, 56, 80, 81… |
| `<LucideIcon>` | 9 | 40, 56, 81, 108… |
| `<Input>` | 9 | 128, 135, 154, 160… |
| `<Button>` | 3 | 243, 248, 262 |
| `<Spinner>` | 2 | 75, 251 |
| `<Card>` | 1 | 26 |
| `<Progress>` | 1 | 28 |
| `<Select>` | 1 | 92 |

### Data lineage — Razor → injected service

| Call | Service (as injected) | Razor line(s) |
|---|---|---|
| `Logger.LogDebug()` | `ILogger<Setup>` | 331 |
| `Logger.LogError()` | `ILogger<Setup>` | 465 |
| `Logger.LogInformation()` | `ILogger<Setup>` | 458 |
| `Logger.LogWarning()` | `ILogger<Setup>` | 517 |
| `Nav.NavigateTo()` | `NavigationManager` | 461 |
| `OllamaProbe.ProbeAsync()` | `IOllamaProbe` | 322 |
| `Rag.ReconfigureAsync()` | `TechieRagManager` | 513 |
| `RagConfig.LoadConfigAsync()` | `TechieRagConfigService` | 474 |
| `RagConfig.SaveConfigAsync()` | `TechieRagConfigService` | 506 |
| `SetupState.MarkCompleteAsync()` | `ISetupStateService` | 456 |
| `Workspaces.CreateWorkspaceAsync()` | `IWorkspaceService` | 534 |
| `Workspaces.EnsureDefaultWorkspaceAsync()` | `IWorkspaceService` | 524 |
| `Workspaces.ListForCurrentUserAsync()` | `IWorkspaceService` | 529, 544 |
| `Workspaces.SlugFor()` | `IWorkspaceService` | 547 |

**Injected services:** `ISetupStateService`, `IOllamaProbe`, `TechieRagConfigService`, `TechieRagManager`, `IWorkspaceService`, `ITechieDeskAuthModeProvider`, `NavigationManager`, `ILogger<Setup>`

**Conditional render guards:** 16 `@if` blocks — the render-truth risk on this screen. First few:

- `@if (state == StepState.Done)` (line 38)
- `@if (currentStep == WizardStep.Defaults)` (line 53)
- `@if (currentStep == WizardStep.AiProvider)` (line 70)

