#!/usr/bin/env python3
"""verify-phase §4a render gate + §4b visual-truth gate over the Appium mac2 session.

Black-box: reads the running app only.
"""
import base64
import json
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import drv  # noqa: E402

OUT = "/Users/MyCode/TechieRag/test-results/ui-verify"
os.makedirs(OUT, exist_ok=True)

INTERACTIVE = {
    "XCUIElementTypeLink", "XCUIElementTypeButton", "XCUIElementTypeTab",
    "XCUIElementTypeTextField", "XCUIElementTypeSecureTextField",
    "XCUIElementTypeTextView", "XCUIElementTypePopUpButton",
    "XCUIElementTypeCheckBox", "XCUIElementTypeRadioButton",
    "XCUIElementTypeComboBox", "XCUIElementTypeSlider", "XCUIElementTypeSwitch",
}
TEXTY = {"XCUIElementTypeStaticText", "XCUIElementTypeHeading",
         "XCUIElementTypeImage", "XCUIElementTypeCell"}


def activate():
    """Raise the app under test to the foreground. Occlusion by another window
    makes a pointer click land in the WRONG app — verify-phase §3b window binding."""
    drv.S("/execute/sync", "POST",
          {"script": "macos: activateApp",
           "args": [{"bundleId": "com.techierathore.techiedesk"}]})
    time.sleep(0.6)


def frontmost():
    o = subprocess.check_output(
        ["osascript", "-e",
         'tell application "System Events" to get name of first process '
         'whose frontmost is true']).decode().strip()
    return o


def win_rect():
    o = subprocess.check_output(
        ["osascript", "-e",
         'tell application "System Events" to tell process "TechieDesk" '
         'to get {position, size} of window 1']).decode().strip()
    a = [int(x) for x in o.split(", ")]
    return {"x": a[0], "y": a[1], "width": a[2], "height": a[3]}


def set_win(w, h, x=0, y=60):
    subprocess.run(["osascript", "-e",
                    f'tell application "System Events" to tell process "TechieDesk" '
                    f'to set position of window 1 to {{{x}, {y}}}'], check=False)
    subprocess.run(["osascript", "-e",
                    f'tell application "System Events" to tell process "TechieDesk" '
                    f'to set size of window 1 to {{{w}, {h}}}'], check=False)
    time.sleep(1.2)
    return win_rect()


def parse():
    """Return (root_window_rect, [nodes]) for web content under the window."""
    xml = drv.source()
    root = ET.fromstring(xml)
    win = root.find(".//XCUIElementTypeWindow")
    if win is None:
        return None, [], xml
    wr = {k: int(float(win.get(k, 0))) for k in ("x", "y", "width", "height")}
    nodes = []

    def walk(el, depth):
        for c in el:
            t = c.tag
            try:
                r = {k: int(float(c.get(k, 0))) for k in ("x", "y", "width", "height")}
            except Exception:
                r = {"x": 0, "y": 0, "width": 0, "height": 0}
            nodes.append({
                "type": t, "label": c.get("label", "") or "",
                "value": c.get("value", "") or "", "title": c.get("title", "") or "",
                "id": c.get("identifier", "") or "", "enabled": c.get("enabled") == "true",
                "rect": r, "depth": depth, "nkids": len(c),
            })
            walk(c, depth + 1)
    walk(win, 1)
    # The macOS menu bar is a SIBLING of the window, not a child — walk it too so
    # native "Go" menu navigation is reachable (verify-phase §3b element-scoped input).
    for mb in root.findall("./XCUIElementTypeMenuBar"):
        walk(mb, 1)
    return wr, nodes, xml


def content_nodes(nodes):
    """Leaf-ish nodes that carry text/interaction (i.e. actual WebView content)."""
    out = []
    for n in nodes:
        if n["type"] in INTERACTIVE or n["type"] in TEXTY:
            txt = (n["label"] or n["value"] or n["title"]).strip()
            if txt or n["type"] in INTERACTIVE:
                out.append(n)
    return out


def overlaps(a, b, tol=2):
    ax, ay, aw, ah = a["x"], a["y"], a["width"], a["height"]
    bx, by, bw, bh = b["x"], b["y"], b["width"], b["height"]
    ox = min(ax + aw, bx + bw) - max(ax, bx)
    oy = min(ay + ah, by + bh) - max(ay, by)
    return ox > tol and oy > tol, max(0, ox) * max(0, oy)


def outside_window(r, wr, tol=2):
    """Is this rect wholly (or almost wholly) outside the window's visible area?

    macOS reports elements inside an `overflow`-scrolled container at their
    UNCLIPPED LAYOUT position, not where the pixels are — the sidebar's own
    scrollback and a chat transcript's earlier turns both report far below the
    window floor. Such an element is not hit-testable there and cannot visually
    collide with anything, so an overlap involving it is a PHANTOM.
    """
    return (r["x"] + r["width"] < wr["x"] + tol
            or r["x"] > wr["x"] + wr["width"] - tol
            or r["y"] + r["height"] < wr["y"] + tol
            or r["y"] > wr["y"] + wr["height"] - tol)


def visual_check(wr, cn):
    """§4b geometry: zero-size, out-of-window, interactive overlap.

    Overlaps are REPORTED, never suppressed — the AX tree carries no scroll offset
    or clip rect, so "clipped by a scroll container" cannot be proven from it, only
    inferred from the element lying outside the window. Each overlap therefore
    carries a `clipped` flag and `overlapCountVisible` counts only the pairs where
    BOTH controls are inside the window. Grade `overlapCountVisible`; read the
    clipped ones before dismissing them (2026-07-31: a 980 px² read-aloud/send
    overlap on workspace-chat was a scroll artifact, but a real composer overflow
    sat underneath it — REQ-UI-044).
    """
    zero, oob, ov = [], [], []
    inter = [n for n in cn if n["type"] in INTERACTIVE]
    for n in cn:
        r = n["rect"]
        nm = f'{n["type"].replace("XCUIElementType","")}:{(n["label"] or n["value"] or n["title"])[:40]}'
        if r["width"] <= 0 or r["height"] <= 0:
            zero.append(nm)
        elif (r["x"] + r["width"] < wr["x"] - 2 or r["x"] > wr["x"] + wr["width"] + 2
              or r["y"] + r["height"] < wr["y"] - 2 or r["y"] > wr["y"] + wr["height"] + 2):
            oob.append(nm + f' @{r["x"]},{r["y"]}')
    for i in range(len(inter)):
        for j in range(i + 1, len(inter)):
            a, b = inter[i], inter[j]
            # skip ancestor/descendant containment (nested link inside button etc.)
            ra, rb = a["rect"], b["rect"]
            contained = (ra["x"] <= rb["x"] and ra["y"] <= rb["y"]
                         and ra["x"] + ra["width"] >= rb["x"] + rb["width"]
                         and ra["y"] + ra["height"] >= rb["y"] + rb["height"]) or \
                        (rb["x"] <= ra["x"] and rb["y"] <= ra["y"]
                         and rb["x"] + rb["width"] >= ra["x"] + ra["width"]
                         and rb["y"] + rb["height"] >= ra["y"] + ra["height"])
            if contained:
                continue
            hit, area = overlaps(ra, rb)
            if hit and area > 40:
                clipped = outside_window(ra, wr) or outside_window(rb, wr)
                ov.append((f'{a["type"].replace("XCUIElementType","")}:{(a["label"] or a["title"])[:30]}',
                           f'{b["type"].replace("XCUIElementType","")}:{(b["label"] or b["title"])[:30]}',
                           area, "clipped" if clipped else "visible"))
    vis = [o for o in ov if o[3] == "visible"]
    return {"zeroSize": zero, "offWindow": oob,
            "overlaps": vis[:25] + [o for o in ov if o[3] == "clipped"][:25],
            "overlapCount": len(ov), "overlapCountVisible": len(vis),
            "overlapCountClipped": len(ov) - len(vis)}


def shot(name):
    activate()
    p = os.path.join(OUT, name)
    drv.screenshot(p)
    return p


def crop_window(src, wr, dest):
    """Crop the desktop screenshot to just the app window using sips-free PPM math."""
    try:
        subprocess.run(["/usr/bin/sips", "-c", str(wr["height"]), str(wr["width"]),
                        "--cropOffset", str(wr["y"]), str(wr["x"]), src,
                        "--out", dest], check=True, capture_output=True)
        return dest
    except Exception:
        return src


def snap(slug, width_tag, wr):
    raw = shot(f"{slug}-{width_tag}-full.png")
    return crop_window(raw, wr, os.path.join(OUT, f"{slug}-{width_tag}.png"))


def sidebar_links(nodes):
    return {n["label"].strip(): n for n in nodes
            if n["type"] == "XCUIElementTypeLink" and n["rect"]["x"] < 260
            and n["label"].strip()}


def click_rect(r):
    activate()
    assert frontmost() == "TechieDesk", f"app not frontmost: {frontmost()}"
    drv.pointer_click(r["x"] + r["width"] / 2, r["y"] + r["height"] / 2)
    time.sleep(1.6)


def find_node(nodes, label, types=None, exact=True):
    for n in nodes:
        if types and n["type"] not in types:
            continue
        lab = (n["label"] or n["title"] or "").strip()
        if (lab == label) if exact else (label.lower() in lab.lower()):
            return n
    return None


def report(slug, wr, cn, vis):
    icons = [n for n in cn if "Icon not found" in (n["label"] or n["value"] or "")]
    return {
        "screen": slug, "win": wr, "contentNodes": len(cn),
        "interactive": len([n for n in cn if n["type"] in INTERACTIVE]),
        "texts": sorted({(n["label"] or n["value"] or n["title"]).strip()
                         for n in cn if (n["label"] or n["value"] or n["title"]).strip()}),
        "visual": vis,
        "iconNotFound": [n["label"] or n["value"] for n in icons],
    }


def wait_settled(timeout=12.0, stable_for=2, poll=0.6):
    """Block until the content tree stops changing.

    A fixed sleep after a click is a race: on a slow screen the tree is read
    half-built, which silently under-reports controls (and reads as the shell
    alone — the 2026-07-30 'constant 63 content nodes' false result). Poll the
    content-node count until it repeats `stable_for` times, then proceed.
    Returns (settled, finalCount, samples) — settled=False means it never
    stabilised and the caller should treat the reading as suspect.
    """
    counts, last, repeats = [], None, 0
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            _wr, nodes, _xml = parse()
            n = len(content_nodes(nodes))
        except Exception:
            n = -1
        counts.append(n)
        if n == last and n > 0:
            repeats += 1
            if repeats >= stable_for:
                return True, n, counts
        else:
            repeats = 0
        last = n
        time.sleep(poll)
    return False, (last or 0), counts


def sweep_current(slug, width_tag):
    settled, _n, samples = wait_settled()
    wr, nodes, xml = parse()
    cn = content_nodes(nodes)
    vis = visual_check(wr, cn)
    p = snap(slug, width_tag, wr)
    r = report(slug, wr, cn, vis)
    r["screenshot"] = p
    r["settled"] = settled
    r["settleSamples"] = samples
    if not settled:
        r["warning"] = ("content tree never stabilised within the timeout — this "
                        "reading may be half-rendered; do NOT grade a REQ from it")
    open(os.path.join(OUT, f"{slug}-{width_tag}.json"), "w").write(json.dumps(r, indent=1))
    return r, nodes
