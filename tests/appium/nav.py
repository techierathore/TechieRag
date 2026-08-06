#!/usr/bin/env python3
"""Navigation helpers for the TechieDesk Mac Catalyst sweep."""
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import drv          # noqa: E402
import strings      # noqa: E402
import sweep as sw  # noqa: E402

DESK = (1600, 1240, 40, 40)   # w, h, x, y  — desktop
NARROW = (1024, 720, 40, 40)  # the REQ-UI-041 enforced floor

SIDEBAR_XMAX = 340            # left column; the sidebar never renders past this x


def desktop():
    return sw.set_win(*DESK)


def narrow():
    return sw.set_win(*NARROW)


def nodes():
    return sw.parse()[1]


def click_label(label, types=None, exact=True, xmax=None, wait=2.0):
    ns = nodes()
    for n in ns:
        if types and n["type"] not in types:
            continue
        lab = (n["label"] or n["title"] or "").strip()
        ok = (lab == label) if exact else (label.lower() in lab.lower())
        if ok and n["rect"]["width"] > 0 and n["rect"]["height"] > 0:
            if xmax is not None and n["rect"]["x"] > xmax:
                continue
            sw.click_rect(n["rect"])
            time.sleep(wait)
            return True
    return False


def sidebar(label, wait=2.2):
    """Click a sidebar nav link by its DISPLAYED label (left column).

    Language-specific by definition — prefer `sidebar_key()`, which resolves the
    displayed label from the product's resources for whatever language the app is
    currently rendering. This stays for ad-hoc use and for links that are not in
    the resource table.
    """
    return click_label(label, types={"XCUIElementTypeLink"}, xmax=SIDEBAR_XMAX, wait=wait)


# REQ-UI-053 — the resource key each screen is known by, mapped to the ROUTE-DERIVED
# `id` MainLayout.razor puts on that sidebar link ("nav-" + the route with slashes
# hyphenated; the per-install workspace slug is dropped). WebKit surfaces a DOM `id`
# as the accessibility `identifier`, so this is a locale-invariant handle: it survives
# a re-translation AND a resource-key rename, which the label path (REQ-NFR-011)
# cannot.
#
# The keys stay as the harness's own vocabulary rather than being replaced by the ids,
# for two reasons: run_sweep.py's SIDEBAR table and its arrival assertion are both keyed
# by resource key (the breadcrumb page rung renders the same key as the link), and the
# label lookup has to stay reachable as the fallback below.
#
# ⚠ This table is one half of a contract with MainLayout.razor. Rename a route there and
# both the `id` and the entry here change together; nothing else in the harness knows
# these strings.
NAV_IDS = {
    "NavChat":              "nav-workspace-chat",
    "NavDocuments":         "nav-workspace-documents",
    "NavConnectors":        "nav-workspace-connectors",
    "NavAgents":            "nav-workspace-agents",
    "NavFlows":             "nav-workspace-flows",
    "NavWorkspaceSettings": "nav-workspace-settings",
    "NavProfile":           "nav-profile",
    "NavPricing":           "nav-pricing",
    "NavBilling":           "nav-billing",
    "NavSupport":           "nav-support",
    "NavSignIn":            "nav-login",
    "NavEventLog":          "nav-admin-events",
    "NavAppSettings":       "nav-admin-settings",
    "NavAutomations":       "nav-automations",
    "NavDataStorage":       "nav-settings-data",
    "NavBackupRestore":     "nav-settings-backup",
    "NavUpdates":           "nav-settings-updates",
    "NavQdrantAdmin":       "nav-qdrant-admin",
    "NavLlmSettings":       "nav-llm-settings",
    "NavTokenUsage":        "nav-token-usage",
    "NavTextIngestion":     "nav-text-ingestion",
    "NavLlmPlayground":     "nav-llm-playground",
    "NavRagConfiguration":  "nav-rag-config",
}


def sidebar_id(nav_id, wait=2.2):
    """Click a sidebar nav link by its `identifier` alone — no resource lookup.

    Returns (clicked, detail). A miss reports the identifiers the sidebar IS
    exposing, which is the difference between "the id was renamed" and "ids do not
    reach the accessibility tree on this build at all".
    """
    for n in nodes():
        if n["type"] != "XCUIElementTypeLink" or n["id"] != nav_id:
            continue
        r = n["rect"]
        if r["x"] > SIDEBAR_XMAX or r["width"] <= 0 or r["height"] <= 0:
            continue
        sw.click_rect(r)
        time.sleep(wait)
        return True, nav_id
    return False, f"no sidebar link with identifier={nav_id!r}; visible ids: {sidebar_ids()}"


def sidebar_ids():
    """Every `identifier` currently exposed by the left column's links.

    Empty means the mechanism is not live on the running head — either it predates
    REQ-UI-053 or WebKit is not mapping the DOM id here — and `sidebar_key()` is
    running on its label fallback. Not a nav failure on its own; see the README.
    """
    seen = []
    for n in nodes():
        if n["type"] != "XCUIElementTypeLink" or n["rect"]["x"] > SIDEBAR_XMAX:
            continue
        if n["id"] and n["id"] not in seen:
            seen.append(n["id"])
    return seen


def sidebar_key(key, wait=2.2):
    """Click a sidebar nav link named by its RESOURCE KEY. Identifier first.

    REQ-UI-053: the link carries a route-derived `id` (see NAV_IDS), which WebKit
    surfaces as the accessibility `identifier`. That is the preferred selector — it
    is the same string in every language and owes nothing to the resource table.

    REQ-NFR-011 remains as the FALLBACK: resolve what the app is rendering right now
    for `key` out of the same AppStrings*.resx the app ships, and click that text.
    It is what keeps the sweep alive against a head built before REQ-UI-053, or a
    link whose id was renamed on one side of the contract only.

    Returns (clicked, detail). `detail` names the selector that worked — an `id`, or
    the label — so a sweep result records WHICH mechanism actually navigated it. On a
    miss it reports both what was wanted and what the sidebar is showing, because
    "link not found" without the observed sidebar is an unanswerable bug report.
    """
    nav_id = NAV_IDS.get(key)
    if nav_id:
        clicked, detail = sidebar_id(nav_id, wait=wait)
        if clicked:
            return True, f"id={detail}"
        id_miss = detail
    else:
        id_miss = f"no id mapped for key={key}"

    try:
        want = strings.all_candidates(key)
    except KeyError as e:
        return False, f"{id_miss}; and {e}"
    for label in want:
        if click_label(label, types={"XCUIElementTypeLink"}, xmax=SIDEBAR_XMAX, wait=wait):
            return True, f"label={label}"
    return False, (f"{id_miss}; and none of {want} (key={key}, "
                   f"lang={strings.app_language()}) in sidebar; "
                   f"visible links: {sidebar_labels()}")


def sidebar_labels():
    """Every link label currently rendered in the left column — a miss diagnostic."""
    seen = []
    for n in nodes():
        if n["type"] != "XCUIElementTypeLink" or n["rect"]["x"] > SIDEBAR_XMAX:
            continue
        lab = (n["label"] or n["title"] or "").strip()
        if lab and lab not in seen:
            seen.append(lab)
    return seen


GO_MENU_KEY = "MenuGo"        # AppStrings key of the Go menu's own title


def menu_labels(kinds=("XCUIElementTypeMenuBarItem", "XCUIElementTypeMenuItem")):
    """Every native menu title currently in the tree — the go_menu miss diagnostic.

    Native menus are window siblings, not page content, so this is the only place
    the harness can see what the menu bar actually says. Printed on a miss because
    "Go menu not found" without the observed titles is an unanswerable bug report —
    it cannot distinguish a renamed key from a menu bar that never got built.
    """
    seen = []
    for n in nodes():
        if n["type"] not in kinds:
            continue
        lab = (n["title"] or n["label"] or "").strip()
        if lab and lab not in seen:
            seen.append(lab)
    return seen


def go_menu(key, wait=2.5):
    """Click an item in the native macOS Go menu, named by its RESOURCE KEY.

    REQ-UI-052: the menu bar used to be hardcoded English, and this function used to
    match it by English text. That worked in a Hindi app only by accident — an
    accident this requirement deliberately removed by localizing
    `MainPage.BuildMenuBar`. So the same fix `sidebar_key()` had for the sidebar
    applies here: resolve what the app is rendering RIGHT NOW for `key` out of the
    same AppStrings*.resx the app ships, and click that.

    This is the CHROMELESS-RECOVERY path — `run_sweep.recover_to_app()` uses it to get
    back into the shell after sweeping `/login`, which has no sidebar at all. If it
    stops working the sweep strands on the auth screens and every route after them is
    reported as a failure, so the coupling to the menu bar is load-bearing, not
    incidental.

    ⚠ Native menu items are NOT web content: they surface as MenuBarItem/MenuItem and,
    unlike WKWebView content, they DO respond to element/click. That is why this clicks
    the rect rather than going through any web driver path.

    Returns a bool, deliberately — `recover_to_app()` writes `if nav.go_menu(...)`, and
    a (clicked, detail) tuple would be truthy on a MISS. The detail goes to stdout.
    """
    try:
        menu = strings.all_candidates(GO_MENU_KEY)
        want = strings.all_candidates(key)
    except KeyError as e:
        print(f"   ! go_menu: {e}")
        return False

    for n in nodes():
        if n["type"] == "XCUIElementTypeMenuBarItem" and \
                (n["title"] or n["label"] or "").strip() in menu:
            sw.click_rect(n["rect"])
            time.sleep(0.8)
            break
    else:
        print(f"   ! go_menu: none of {menu} (key={GO_MENU_KEY}, "
              f"lang={strings.app_language()}) in the menu bar; visible: {menu_labels()}")
        return False

    for n in nodes():
        if n["type"] == "XCUIElementTypeMenuItem" and \
                (n["title"] or n["label"] or "").strip() in want:
            sw.click_rect(n["rect"])
            time.sleep(wait)
            return True

    print(f"   ! go_menu: none of {want} (key={key}, lang={strings.app_language()}) "
          f"under the Go menu; visible: {menu_labels()}")
    return False


def breadcrumb():
    """Read the topbar breadcrumb trail so we know which route we are on."""
    out = []
    for n in nodes():
        r = n["rect"]
        if n["type"] in ("XCUIElementTypeStaticText", "XCUIElementTypeLink") and \
           r["y"] < 160 and r["x"] > 300:
            t = (n["label"] or n["title"]).strip()
            if t and t not in out:
                out.append(t)
    return out


def id_probe():
    """REQ-UI-053 evidence: what the RUNNING head exposes as `identifier` per link.

    Prints one row per sidebar link — its identifier and its label — then which of
    NAV_IDS are present and which are missing. This is the one command that answers
    "did the DOM id actually reach the macOS accessibility tree on this build?", and
    it answers it from the live tree rather than from the markup.
    """
    rows = [n for n in nodes()
            if n["type"] == "XCUIElementTypeLink" and n["rect"]["x"] <= SIDEBAR_XMAX]
    for n in rows:
        print(f'   identifier={n["id"] or "<empty>":28s} label={(n["label"] or n["title"]).strip()!r}')
    live = {n["id"] for n in rows if n["id"]}
    wanted = set(NAV_IDS.values())
    print(f"\n{len(live & wanted)}/{len(wanted)} of the REQ-UI-053 ids are on screen "
          f"({len(rows)} sidebar link(s) rendered — a signed-in shell shows 22 of the 23).")
    missing = sorted(wanted - live)
    if missing:
        print(f"absent: {missing}")
    unexpected = sorted(live - wanted)
    if unexpected:
        print(f"identifiers not in NAV_IDS: {unexpected}")
    return 0 if live & wanted else 1


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "ids"
    if cmd == "ids":
        sys.exit(id_probe())
    if cmd == "click":                       # nav.py click NavQdrantAdmin
        print(sidebar_key(sys.argv[2]))
        print("breadcrumb:", breadcrumb())
    else:
        print(__doc__)
