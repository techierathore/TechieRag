# TechieDesk DevGuide — Shell & landing

*Generated 2026-07-28 · reflects code as built.* [← index](./TechieDesk-DevGuide.md)

> ✅ **Runtime-verified 2026-07-29** — re-swept on the live **Mac Catalyst** head over Appium (`mac2`), bound by `appPath` to the universal Release bundle, driving **28 of the 30 screens** at **1600×1240** and **1024×720** (the REQ-UI-041 floor). Every `Observed` line below is what the running app did; `Visual (§4b)` is the overlap / zero-size / off-viewport geometry check plus a human look at the screenshot. Screens that could not be reached say so and are **not** claimed as verified. Screenshots: `test-results/ui-verify/`.


1 screen(s) in this area.

## `/` — Home

- **File:** `apps/TechieDesk/Components/Pages/Home.razor` (85 lines)
- **Reached via:** app launch (BlazorWebView host page)
- **Observed:** renders ✓ (runtime-confirmed 2026-07-29) — **now observed** (was `not observed` on 2026-07-28). Driven through the native Go menu → *Home* ⌘1. Behaves exactly as documented: `/` is a redirect, and with one workspace present it lands on `/workspace/default` (breadcrumb `Workspace: Default › Chat`). The `Spinner` and failure `Alert` are transient/failure-only and did not appear — correct, not renders-empty.
- **Visual (§4b):** looks-right ✓ (runtime-confirmed 2026-07-29) — the redirect target passes both widths; the redirect itself paints no lasting UI.

### Controls

| Component | Uses | Razor line(s) |
|---|---|---|
| `<Alert>` | 2 | 33, 34 |
| `<Spinner>` | 1 | 28 |
| `<LucideIcon>` | 1 | 34 |

### Data lineage — Razor → injected service

| Call | Service (as injected) | Razor line(s) |
|---|---|---|
| `Logger.LogError()` | `ILogger<Home>` | 80 |
| `Nav.NavigateTo()` | `NavigationManager` | 68, 74 |
| `Workspaces.ListForCurrentUserAsync()` | `IWorkspaceService` | 65 |
| `Workspaces.SlugFor()` | `IWorkspaceService` | 74 |

**Injected services:** `IWorkspaceService`, `NavigationManager`, `ILogger<Home>`

**Conditional render guards:** 1 `@if` blocks — the render-truth risk on this screen. First few:

- `@if (failure is null)` (line 26)

