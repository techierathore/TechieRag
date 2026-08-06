#!/usr/bin/env python3
"""Full §4a/§4b sweep of the TechieDesk Mac Catalyst head."""
import json
import os
import re
import subprocess
import sys
import time
import traceback
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import drv          # noqa: E402
import strings      # noqa: E402
import sweep as sw  # noqa: E402
import nav          # noqa: E402

RESULTS = os.path.join(sw.OUT, "sweep-results.json")

# (slug, route, resource-key)
#
# The third column is the RESOURCE KEY of the sidebar link, NOT its English text
# (REQ-NFR-011, 2026-07-31). The sidebar renders `@Localizer[key]` and the breadcrumb's
# page rung renders the SAME key for every route below, so one key drives both the click
# and the arrival assertion in whatever language the app is running. A hardcoded English
# table could not navigate a localized app at all: at REQ-UI-050's 46.3% coverage every
# screen failed with `nav link NOT FOUND` under `AppearanceLanguage='hi'`.
SIDEBAR = [
    ("workspace-chat",      "/workspace/default",                    "NavChat"),
    ("document-library",    "/workspace/default/documents",          "NavDocuments"),
    ("connectors-hub",      "/workspace/default/connectors",         "NavConnectors"),
    ("workspace-agents",    "/workspace/default/agents",             "NavAgents"),
    ("workspace-flows",     "/workspace/default/flows",              "NavFlows"),
    ("workspace-settings",  "/workspace/default/settings",           "NavWorkspaceSettings"),
    ("profile",             "/profile",                              "NavProfile"),
    ("pricing",             "/pricing",                              "NavPricing"),
    ("billing",             "/billing",                              "NavBilling"),
    ("support",             "/support",                              "NavSupport"),
    ("admin-events",        "/admin/events",                         "NavEventLog"),
    ("admin-settings",      "/admin/settings",                       "NavAppSettings"),
    ("automations",         "/automations",                          "NavAutomations"),
    ("data-storage",        "/settings/data",                        "NavDataStorage"),
    ("backup-restore",      "/settings/backup",                      "NavBackupRestore"),
    ("app-updates",         "/settings/updates",                     "NavUpdates"),
    ("qdrant-admin",        "/qdrant-admin",                         "NavQdrantAdmin"),
    ("llm-settings",        "/llm-settings",                         "NavLlmSettings"),
    ("token-usage",         "/token-usage",                          "NavTokenUsage"),
    ("text-ingestion",      "/text-ingestion",                       "NavTextIngestion"),
    ("llm-playground",      "/llm-playground",                       "NavLlmPlayground"),
    ("rag-config",          "/rag-config",                           "NavRagConfiguration"),
    # Chromeless (AuthLayout) — MUST stay last: no sidebar once we are there.
    ("login",               "/login",                                "NavSignIn"),
]


# Routes rendered under AuthLayout: no sidebar and no breadcrumb by design.
# They need a distinguishing on-screen marker instead of a breadcrumb, and the
# sweep must RECOVER afterwards — landing on one strands every later sidebar
# click (observed 2026-07-30: visiting /login made the next 11 screens
# unreachable). Sweep them last and re-enter the app afterwards.
#
# A marker is either a literal or `key:<ResourceKey>`, which resolves through the resx
# exactly like the sidebar labels do.
#
# REQ-UI-052 (2026-08-01) localized the auth screens, which BROKE the two English literals
# that used to live here: "Sign in to your TechieDesk instance" / "Get started with your
# TechieDesk instance" are now `LoginSubheading` / `RegisterSubheading` and render in
# Devanagari under AppearanceLanguage='hi', so the literals matched nothing and the arrival
# gate failed the screen. The keys resolve to those exact English strings in `en`, so this
# is behaviour-preserving there and merely correct in `hi`.
#
# The lesson generalises: localizing a screen silently invalidates any harness that
# identifies it by its English text. Anything here that is a literal is a latent break.
CHROMELESS = {
    "login": "key:LoginSubheading",
    "register": "key:RegisterSubheading",
    "setup": "key:SetupStepIndicator",
}


def _marker_fragments(marker):
    """[[fragment, …], …] — one fragment group per language the marker can render in.

    A group matches only when ALL of its fragments are on screen. Groups exist
    because a resolved resource value may carry `{0}` placeholders
    ("Step {0} of {1} · {2}") that never appear literally; matching the single
    longest run would reduce that marker to "Step", which proves nothing.
    """
    vals = strings.all_candidates(marker[4:]) if marker.startswith("key:") else [marker]
    out = []
    for v in vals:
        if "{" in v:
            frags = [f.strip(" ·|-") for f in re.split(r"\{\d+\}", v)]
            frags = [f for f in frags if len(f) >= 2]
        else:
            frags = [v]
        if frags and frags not in out:
            out.append(frags)
    return out


def chromeless_ok(slug, nodes):
    """Assert arrival on a sidebar-less screen via a distinctive marker."""
    marker = CHROMELESS.get(slug)
    if not marker:
        return False, "not a chromeless route"
    hay = " | ".join((n["label"] or n["value"] or n["title"] or "") for n in nodes).lower()
    groups = _marker_fragments(marker)
    hit = next((g for g in groups if all(f.lower() in hay for f in g)), None)
    return (hit is not None), (" + ".join(hit) if hit
                               else " / ".join(" + ".join(g) for g in groups))


def recover_to_app():
    """Re-enter the main shell after a chromeless screen.

    AuthLayout has no sidebar, so the normal nav is gone. The native Go menu is
    still present (it is a window sibling, not page content), so use it.

    REQ-UI-052: these are RESOURCE KEYS now, not English captions. The menu bar is
    localized, so the old ("Chat", "Home", "Workspace") list would have matched
    nothing in a Hindi app and stranded the sweep on /login. "Workspace" is dropped
    with them — the Go menu has never had such an item, so it was always a no-op
    third try; MenuGoHome is the one that has actually been doing the recovery.
    """
    for key in ("MenuGoHome", "MenuGoChat"):
        if nav.go_menu(key):
            return True
    return False


def arrival_ok(route, crumbs, key=None):
    """Did the click actually land on `route`?

    A click that misses (occluded window, moved control, slow render) leaves the
    PREVIOUS screen up, and the sweep then records that screen's controls under
    the new slug — a confidently WRONG result, which is worse than an error. The
    breadcrumb is the app's own statement of where it is, so make it the gate.

    The breadcrumb is LOCALIZED (`MainLayout.CurrentTrail` renders `Localizer[key]`
    for the page rung), so route-segment matching only ever worked in English — it
    would have failed every screen in Hindi even once the click landed. The primary
    gate is therefore the same resource key that drove the click: `MainLayout` uses
    one key for the sidebar link AND the breadcrumb page rung of each route below,
    so agreement between them is a genuine arrival proof in any language.

    The old route-segment heuristic stays as the fallback, for routes with no key
    and for the case where a rename desynchronises the two.
    """
    if not crumbs:
        return False, "no breadcrumb rendered"
    hay = " / ".join(crumbs).lower()
    if key:
        try:
            for label in strings.all_candidates(key):
                if label.lower() in hay:
                    return True, hay
        except KeyError:
            pass                      # unknown key: fall through to the route heuristic
    tail = [seg for seg in route.strip("/").split("/") if seg and "{" not in seg]
    want = (tail[-1] if tail else "").replace("-", " ")
    if not want:                      # root route: any breadcrumb is acceptable
        return True, hay
    # accept the segment itself or a couple of known display-name aliases
    alias = {"rag config": ["rag configuration", "retrieval"],
             "admin/events": ["event log"], "events": ["event log"],
             "settings": ["settings"], "data": ["data", "storage"],
             "updates": ["updates"], "backup": ["backup"]}
    cands = [want] + alias.get(want, [])
    return (any(c in hay for c in cands), hay)


def both_widths(slug, route, key=None):
    out = {"slug": slug, "route": route, "navKey": key}
    nav.desktop()
    if slug in CHROMELESS:
        ok, hay = chromeless_ok(slug, nav.nodes())
        out["arrivalMarker"] = hay
    else:
        crumbs = nav.breadcrumb()
        ok, hay = arrival_ok(route, crumbs, key)
        out["breadcrumb"] = crumbs
    out["arrived"] = ok
    if not ok:
        # Record the miss and REFUSE to grade — never file another screen's
        # controls under this slug.
        out["error"] = f"MIS-NAVIGATED: expected '{route}', breadcrumb says '{hay}'"
        return out
    d, _ = sw.sweep_current(slug, "d1600")
    out["desktop"] = d
    nav.narrow()
    n, _ = sw.sweep_current(slug, "n1024")
    out["narrow"] = n
    nav.desktop()
    return out


DATA_DIR = os.path.expanduser("~/Library/Application Support/TechieDesk")
LOCKS = ("techiedesk.lock", "techiedesk.instance.json")
WDA = os.environ.get("TD_WDA", "http://127.0.0.1:10100")


def _http_ready(url, timeout=6):
    try:
        with urllib.request.urlopen(url + "/status", timeout=timeout) as r:
            return bool(json.loads(r.read().decode()).get("value", {}).get("ready")), "ready"
    except Exception as e:
        return False, repr(e)


def _running_heads():
    """PIDs of running TechieDesk app processes (not this harness)."""
    try:
        out = subprocess.run(["/usr/bin/pgrep", "-f",
                              "TechieDesk.app/Contents/MacOS/TechieDesk"],
                             capture_output=True, text=True).stdout
    except Exception:
        return []
    return [p for p in out.split() if p.strip()]


def preflight():
    """Refuse ONCE, clearly, instead of failing 21 times identically.

    Every item here has actually produced a full wasted sweep: a dead session
    (2026-07-31, three sweeps), a stale data-directory lock making the app show
    REQ-FN-051's "TechieDesk is already running" refusal window instead of the app,
    and a resource-key rename silently breaking navigation. Returns a list of
    problems; empty means go. Set TD_SKIP_PREFLIGHT=1 to bypass.
    """
    problems, notes = [], []

    ok, why = _http_ready(drv.HUB)
    notes.append(f"appium {drv.HUB}: {'ready' if ok else why}")
    if not ok:
        problems.append(f"Appium is not ready at {drv.HUB} ({why}). Start it: appium --port 4723")

    ok, why = _http_ready(WDA)
    notes.append(f"wda {WDA}: {'ready' if ok else why}")
    if not ok:
        problems.append(
            f"WebDriverAgentMac is not ready at {WDA} ({why}). "
            "pkill -9 testmanagerd, then restart WDA (see README prerequisites).")

    alive, detail = drv.session_alive()
    notes.append(detail)
    if not alive:
        problems.append(detail)

    heads = _running_heads()
    stale = [f for f in LOCKS if os.path.exists(os.path.join(DATA_DIR, f))]
    notes.append(f"heads running: {len(heads)}; lock files: {stale or 'none'}")
    if stale and not heads:
        problems.append(
            f"Stale {', '.join(stale)} in {DATA_DIR} with no TechieDesk running. "
            "REQ-FN-051's single-instance guard will refuse the next launch — delete them.")
    if len(heads) > 1:
        problems.append(
            f"{len(heads)} TechieDesk processes are running (pids {', '.join(heads)}). "
            "The sweep cannot tell which window it is grading — kill all but one.")

    lang = strings.app_language()
    missing = strings.missing_keys([k for _s, _r, k in SIDEBAR])
    notes.append(f"app language: {lang}; nav labels resolve to "
                 f"{strings.candidates(SIDEBAR[0][2])[0]!r} …")
    if missing:
        problems.append(
            f"Resource keys missing from AppStrings*.resx: {missing}. "
            "They were renamed in the product; update SIDEBAR in run_sweep.py.")

    for n in notes:
        print(f"   · {n}")
    return problems


def main(only=None):
    if not os.environ.get("TD_SKIP_PREFLIGHT"):
        print("preflight:")
        bad = preflight()
        if bad:
            print("\n!! REFUSING TO SWEEP — fix these first:")
            for b in bad:
                print(f"   - {b}")
            return 2
    res = {}
    if os.path.exists(RESULTS):
        res = json.load(open(RESULTS))
    todo = SIDEBAR if not only else [r for r in SIDEBAR if r[0] in only]
    for slug, route, key in todo:
        try:
            nav.desktop()
            ok, detail = nav.sidebar_key(key)
            if not ok:
                res[slug] = {"slug": slug, "route": route, "navKey": key,
                             "error": f"sidebar link not found: {detail}"}
                print(f"!! {slug}: nav key '{key}' NOT FOUND — {detail}")
                continue
            r = both_widths(slug, route, key)
            r["navLabel"] = detail
            res[slug] = r
            if slug in CHROMELESS:
                r["recovered"] = recover_to_app()
            if not r.get("arrived"):
                print(f"!! {slug:20s} {r.get('error')}")
                continue
            unsettled = [w for w in ("desktop", "narrow")
                         if not r.get(w, {}).get("settled", True)]
            flag = f"  ⚠ UNSETTLED:{','.join(unsettled)}" if unsettled else ""
            dv, nv = r["desktop"]["visual"], r["narrow"]["visual"]
            print(f"OK {slug:20s} d={r['desktop']['contentNodes']:4d}n "
                  f"ov={dv.get('overlapCountVisible', dv['overlapCount']):2d}"
                  f"/{dv['overlapCount']:2d} | "
                  f"n={r['narrow']['contentNodes']:4d}n "
                  f"ov={nv.get('overlapCountVisible', nv['overlapCount']):2d}"
                  f"/{nv['overlapCount']:2d} "
                  f"icons={len(r['desktop']['iconNotFound'])}{flag}")
        except drv.SessionError as e:
            # Do NOT keep going: every later screen would fail the same way and bury
            # the real cause under 20 identical errors.
            res[slug] = {"slug": slug, "route": route, "navKey": key, "error": str(e)}
            json.dump(res, open(RESULTS, "w"), indent=1)
            print(f"\n!! ABORTING AT {slug}: {e}")
            return 3
        except Exception as e:
            res[slug] = {"slug": slug, "route": route, "navKey": key, "error": repr(e),
                         "tb": traceback.format_exc()[-800:]}
            print(f"!! {slug}: {e!r}")
        json.dump(res, open(RESULTS, "w"), indent=1)
    print("done ->", RESULTS)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:] or None))
