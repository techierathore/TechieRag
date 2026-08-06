#!/usr/bin/env python3
"""REQ-UI-054: assert that UIKit actually DREW the menu bar `MainPage` declares.

Run it against a live head:

    python3 tests/appium/drv.py new >/dev/null && python3 tests/appium/menu_check.py

Exit `0` = every declared menu, item and key equivalent is in the running menu bar.
Exit `1` = something the app declared is not there, named precisely.
Exit `2` = refused (no session / no head / the source table could not be read).

WHY THIS IS NOT A SOURCE SCAN, AND WHY IT HAD TO EXIST
------------------------------------------------------
`MenuBarLocalizationTests` (REQ-UI-052) reads `MainPage.xaml.cs` and proves every
caption is a resource key that resolves in every shipped language. Its own header
says what it cannot do: *"It does NOT prove UIKit drew them."* On 2026-08-01 that
gap cost a whole menu. The app declared four menus and eighteen items; UIKit drew
three menus and fifteen items, and **every test in the repo was green**, because
`Format ▸ Font ▸ Text Size` already owned ⌘+ and ⌘− and `UIMenuBuilder` discards
the entire menu that re-declares a taken key equivalent — no exception, no log,
no diff in any source file. Nothing readable from disk could ever have caught it.

So this reads the DECLARATION from `MainPage.xaml.cs` (so it can never drift from
the product) and checks it against what macOS is really showing:

* the accessibility tree, for the menu titles and item captions — via the same
  `sweep.parse()` the rest of the harness uses, which walks the menu bar as a
  window sibling; and
* `AXMenuItemCmdChar` / `AXMenuItemCmdModifiers` through System Events, for the
  key equivalents. Those are NOT in the WebDriverAgent tree, and they are the half
  of the contract a screenshot cannot show: REQ-UI-049 put App Settings on ⌘, and
  a rebuild that quietly dropped it would look identical.

LANGUAGE-INDEPENDENT, the same way `nav.py` is: captions are resolved from the
product's own `AppStrings*.resx` by resource key, so this passes in `en` and in
`hi` without a translated string anywhere in this file. Run it in both — a menu
whose title collides with a `UIMenuIdentifier` (English "View", "File", "Help")
takes a completely different code path in `AppDelegate.BuildMenu` from one whose
title does not (Hindi "दृश्य"), and only running both exercises both.

Black-box: reads the running app and the product's resources. Writes nothing.
"""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import drv       # noqa: E402
import strings   # noqa: E402
import sweep     # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MENU_SOURCE = os.path.join(REPO, "apps", "TechieDesk", "MainPage.xaml.cs")

# The same two shapes MenuBarLocalizationTests keys off. Kept in step deliberately:
# if the call shape changes, BOTH guards must be updated, and `read_declaration()`
# refuses rather than silently finding nothing.
MENU_TITLE = re.compile(r'new\s+MenuBarItem\s*\{\s*Text\s*=\s*Text\(\s*"([^"]+)"\s*\)\s*\}')
MENU_ENTRY = re.compile(r'MenuItem\(\s*"([^"]+)"\s*,\s*(?:key:\s*)?(null|"[^"]*")\s*,([^\n]*)')

# A menu bar item the app contributed but that macOS owns the title of: the app's
# items were merged INTO the stock menu, so they are found under its English name.
# Mirrors AppDelegate.StandardMenus.
STANDARD_TITLES = {
    "File": "File", "Edit": "Edit", "View": "View",
    "Window": "Window", "Help": "Help",
}

# AXMenuItemCmdModifiers is a mask over the NON-Command modifiers, with Command
# implied by 0. `MainPage.MenuItem` only ever emits Cmd or Shift+Cmd on macOS.
AX_CMD = 0
AX_SHIFT_CMD = 1


class Refused(RuntimeError):
    """Pre-flight said there is nothing worth checking."""


def read_declaration():
    """[(menuKey, [(itemKey, accelerator|None, shift)])] exactly as declared, in order."""
    if not os.path.exists(MENU_SOURCE):
        raise Refused(f"{MENU_SOURCE} does not exist — this guard is reading the wrong tree.")
    source = open(MENU_SOURCE, encoding="utf-8").read()

    marks = [(m.start(), "menu", m.group(1)) for m in MENU_TITLE.finditer(source)]
    marks += [(m.start(), "item", (m.group(1),
                                   None if m.group(2) == "null" else m.group(2).strip('"'),
                                   "shift: true" in m.group(3)))
              for m in MENU_ENTRY.finditer(source)]
    marks.sort()

    menus, current = [], None
    for _pos, kind, payload in marks:
        if kind == "menu":
            current = (payload, [])
            menus.append(current)
        elif current is not None:
            current[1].append(payload)

    items = sum(len(i) for _k, i in menus)
    if len(menus) < 2 or items < 2:
        raise Refused(
            f"read only {len(menus)} menu(s) and {items} item(s) from {MENU_SOURCE}. "
            "The call shape in BuildMenuBar has changed, so this guard would pass "
            "vacuously — update MENU_TITLE/MENU_ENTRY here AND in MenuBarLocalizationTests.")
    return menus


def caption(key):
    """Every caption the running app could legitimately be showing for this key."""
    try:
        return strings.all_candidates(key)
    except Exception as exception:                                # unknown key
        raise Refused(f"resource key {key!r} is not in AppStrings — the menu bar "
                      f"names a key the product no longer ships ({exception!r})") from exception


def live_menu_bar():
    """{menuTitle: [itemTitle, …]} for every top-level menu except Apple's."""
    _wr, nodes, _xml = sweep.parse()
    bar, menu = {}, None
    for node in nodes:
        title = (node["title"] or node["label"] or "").strip()
        if node["type"] == "XCUIElementTypeMenuBarItem":
            menu = title
            bar.setdefault(menu, [])
        elif node["type"] == "XCUIElementTypeMenuItem" and menu is not None and title:
            bar[menu].append(title)
    bar.pop("Apple", None)
    return bar


def live_accelerators():
    """{(menuTitle, itemTitle): (cmdChar, modifierMask)} — one AppleScript pass.

    The key equivalents are not in the WebDriverAgent tree at all. System Events
    exposes them as AXMenuItemCmdChar/AXMenuItemCmdModifiers, and one submenu level
    is walked because the stock menus nest (Format ▸ Font ▸ …) — the app's own items
    never do, but a regression that pushed one into a submenu should still be found.
    """
    script = '''
tell application "System Events" to tell process "TechieDesk"
  set out to ""
  repeat with m in menu bar items of menu bar 1
    set mn to name of m
    if mn is not "Apple" then
      try
        repeat with i in menu items of menu 1 of m
          try
            set c to value of attribute "AXMenuItemCmdChar" of i
            set d to value of attribute "AXMenuItemCmdModifiers" of i
            if c is not missing value then ¬
              set out to out & mn & tab & (name of i) & tab & c & tab & d & linefeed
          end try
          try
            repeat with j in menu items of menu 1 of i
              try
                set c2 to value of attribute "AXMenuItemCmdChar" of j
                set d2 to value of attribute "AXMenuItemCmdModifiers" of j
                if c2 is not missing value then ¬
                  set out to out & mn & tab & (name of j) & tab & c2 & tab & d2 & linefeed
              end try
            end repeat
          end try
        end repeat
      end try
    end if
  end repeat
  return out
end tell'''
    raw = subprocess.run(["osascript", "-e", script], capture_output=True,
                         text=True, timeout=180).stdout
    table = {}
    for line in raw.splitlines():
        parts = line.split("\t")
        if len(parts) == 4:
            table[(parts[0], parts[1])] = (parts[2], int(parts[3]))
    return table


def check():
    """[failure, …] — empty means the running menu bar matches the declaration."""
    declared = read_declaration()
    bar = live_menu_bar()
    accelerators = live_accelerators()
    failures = []

    if not bar:
        raise Refused("the menu bar is empty in the accessibility tree — the head is "
                      "not up, or the tree was read before it settled. Call "
                      "sweep.wait_settled() first.")

    language = strings.app_language()
    print(f"menu_check: language={language}  menus in bar: {', '.join(bar)}")

    for menu_key, items in declared:
        wanted = caption(menu_key)
        # Either the app drew its own top-level menu, or macOS owned the title and the
        # items were merged into the stock menu of the same name. Both are correct;
        # what is NOT correct is the items being nowhere.
        hosts = [title for title in bar if title in wanted]
        hosts += [STANDARD_TITLES[title] for title in wanted
                  if title in STANDARD_TITLES and STANDARD_TITLES[title] in bar]
        hosts = list(dict.fromkeys(hosts))

        if not hosts:
            failures.append(
                f"MENU MISSING: {menu_key} — none of {wanted} is in the menu bar, and it "
                f"did not merge into a stock menu. Present: {sorted(bar)}")
            continue

        for item_key, accelerator, shift in items:
            names = caption(item_key)
            where = next(((host, name) for host in hosts for name in names
                          if name in bar[host]), None)
            if where is None:
                failures.append(
                    f"ITEM MISSING: {menu_key}▸{item_key} — none of {names} is under "
                    f"{hosts}. That menu currently shows: "
                    f"{[i for host in hosts for i in bar[host]]}")
                continue

            observed = accelerators.get(where)
            if accelerator is None:
                if observed is not None:
                    failures.append(
                        f"ACCELERATOR ADDED: {menu_key}▸{item_key} declares no shortcut "
                        f"but the menu bar shows {observed}.")
                continue

            if observed is None:
                failures.append(
                    f"ACCELERATOR MISSING: {menu_key}▸{item_key} declares "
                    f"{'Shift+' if shift else ''}Cmd+{accelerator} but the menu bar "
                    f"shows no key equivalent. Either the accelerator stopped being "
                    f"emitted (AppDelegate.ToCommand), or a stock UIKit command already "
                    f"owns that key — see AppDelegate.SupersededStandardMenus.")
                continue

            char, mask = observed
            want_mask = AX_SHIFT_CMD if shift else AX_CMD
            if char.lower() != accelerator.lower() or mask != want_mask:
                failures.append(
                    f"ACCELERATOR WRONG: {menu_key}▸{item_key} declares "
                    f"{'Shift+' if shift else ''}Cmd+{accelerator} but the menu bar "
                    f"shows char={char!r} modifiers={mask} (wanted char={accelerator!r} "
                    f"modifiers={want_mask}).")

    total = sum(len(i) for _k, i in declared)
    print(f"menu_check: {len(declared)} menus / {total} items declared, "
          f"{len(failures)} failure(s)")
    return failures


if __name__ == "__main__":
    try:
        alive, detail = drv.session_alive()
        if not alive:
            print(f"REFUSED: {detail}")
            sys.exit(2)
        sweep.wait_settled()
        problems = check()
    except Refused as refusal:
        print(f"REFUSED: {refusal}")
        sys.exit(2)
    except drv.SessionError as dead:
        print(f"REFUSED: {dead}")
        sys.exit(2)

    for problem in problems:
        print(f"  ✗ {problem}")
    if problems:
        sys.exit(1)
    print("  ✓ every declared menu, item and key equivalent is in the running menu bar")
