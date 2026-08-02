#!/usr/bin/env python3
"""Minimal W3C WebDriver client for the Appium mac2 driver (stdlib only).

Harness for verify-phase 4a/4b against the TechieDesk Mac Catalyst head.
Black-box: reads the app only, never modifies application source.
"""
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request

HUB = os.environ.get("APPIUM_HUB", "http://127.0.0.1:4723")
APP_PATH = os.environ.get(
    "TD_APP",
    "/Users/MyCode/TechieRag/apps/TechieDesk/bin/Release/net10.0-maccatalyst/TechieDesk.app",
)
BUNDLE = "com.techierathore.techiedesk"
STATE = os.environ.get("TD_STATE", "/tmp/td_session.json")


class SessionError(RuntimeError):
    """The Appium session is gone, or the driver answered with an error payload.

    Raised instead of letting a WebDriver error object flow on into code that
    expects data. A dead session used to surface as
    `TypeError("a bytes-like object is required, not 'dict'")` on EVERY screen,
    because `screenshot()` base64-decoded the error dict — three sweeps were lost
    to that on 2026-07-31 before the real cause was found.
    """


def _unwrap(res, what, want=None):
    """Return `res['value']`, or raise SessionError if the driver reported a fault.

    A W3C error response is `{"value": {"error": ..., "message": ...}}` — a DICT
    where the caller expected data. Detection keys off the `error` member, not off
    dict-ness: plenty of endpoints (`/timeouts`, `/window/rect`) legitimately answer
    with a dict, and treating every dict as a failure is its own false verdict.

    `want` optionally asserts the payload type the caller can actually use, so a
    surprise shape is named here rather than as a `TypeError` 40 lines away.
    """
    v = res.get("value")
    if isinstance(v, dict) and ("error" in v or "stacktrace" in v):
        err = v.get("error") or "unknown error"
        msg = [ln for ln in (v.get("message") or "").strip().splitlines() if ln]
        raise SessionError(
            f"{what} failed — the Appium session is dead or refused the command "
            f"[{err}]: {msg[0] if msg else ''} "
            f"Recreate it: python3 tests/appium/drv.py new")
    if v is None:
        raise SessionError(f"{what} returned no value — session likely gone: {str(res)[:200]}")
    if want is not None and not isinstance(v, want):
        raise SessionError(
            f"{what} returned {type(v).__name__}, not "
            f"{getattr(want, '__name__', want)} — session state is not usable: {str(v)[:200]}")
    return v


def _req(method, path, body=None, timeout=180):
    url = HUB + path
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method=method,
                               headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return json.loads(raw)
        except Exception:
            return {"error": raw, "status": e.code}


def new_session():
    caps = {
        "capabilities": {
            "alwaysMatch": {
                "platformName": "mac",
                "appium:automationName": "mac2",
                "appium:bundleId": BUNDLE,
                "appium:appPath": APP_PATH,
                "appium:noReset": True,
                "appium:newCommandTimeout": 600,
            },
            "firstMatch": [{}],
        }
    }
    res = _req("POST", "/session", caps)
    v = res.get("value", {})
    sid = v.get("sessionId") or res.get("sessionId")
    if not sid:
        print(json.dumps(res)[:2000])
        sys.exit(1)
    json.dump({"sessionId": sid, "caps": v.get("capabilities", {})}, open(STATE, "w"))
    return sid


def sid():
    return json.load(open(STATE))["sessionId"]


def S(path, method="GET", body=None, timeout=180):
    return _req(method, f"/session/{sid()}{path}", body, timeout)


def source():
    return _unwrap(S("/source"), "GET /source", want=str)


def screenshot(dest):
    v = _unwrap(S("/screenshot"), "GET /screenshot", want=str)
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    open(dest, "wb").write(base64.b64decode(v))
    return dest


def session_alive():
    """(ok, detail) — is there a live session this harness can drive?

    Cheap: `GET /timeouts` touches the session without capturing a screen. Used by
    `run_sweep.preflight()` so a dead session is ONE clear refusal instead of 21
    identical confusing failures.
    """
    try:
        s = sid()
    except Exception as e:
        return False, f"no session state at {STATE} ({e!r}) — run: python3 tests/appium/drv.py new"
    try:
        _unwrap(_req("GET", f"/session/{s}/timeouts", timeout=20), "GET /timeouts")
    except SessionError as e:
        return False, f"session {s[:8]}… is dead: {e}"
    except Exception as e:
        return False, f"session {s[:8]}… unreachable: {e!r}"
    return True, f"session {s[:8]}… alive"


def find_all(xpath):
    r = S("/elements", "POST", {"using": "xpath", "value": xpath})
    v = r.get("value", [])
    if not isinstance(v, list):
        return []
    return [list(e.values())[0] for e in v]


def find(xpath):
    els = find_all(xpath)
    return els[0] if els else None


def rect(eid):
    return S(f"/element/{eid}/rect").get("value", {})


def attr(eid, name):
    return S(f"/element/{eid}/attribute/{name}").get("value")


def text(eid):
    return S(f"/element/{eid}/text").get("value")


def pointer_click(x, y):
    """W3C pointer actions — element/click no-ops on WKWebView content."""
    body = {"actions": [{
        "type": "pointer", "id": "mouse1",
        "parameters": {"pointerType": "mouse"},
        "actions": [
            {"type": "pointerMove", "duration": 100, "x": int(x), "y": int(y)},
            {"type": "pointerDown", "button": 0},
            {"type": "pause", "duration": 60},
            {"type": "pointerUp", "button": 0},
        ]}]}
    return S("/actions", "POST", body)


def click_el(eid):
    r = rect(eid)
    if not r or r.get("width", 0) <= 0:
        return False
    pointer_click(r["x"] + r["width"] / 2, r["y"] + r["height"] / 2)
    return True


def quit_session():
    try:
        s = sid()
    except Exception:
        return
    _req("DELETE", f"/session/{s}")
    try:
        os.remove(STATE)
    except Exception:
        pass


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "new":
        print(new_session())
    elif cmd == "quit":
        quit_session()
        print("quit")
    elif cmd == "src":
        out = source()
        dest = sys.argv[2] if len(sys.argv) > 2 else "/tmp/src.xml"
        open(dest, "w").write(out)
        print(f"{len(out)} bytes -> {dest}")
    elif cmd == "shot":
        print(screenshot(sys.argv[2]))
    elif cmd == "raw":
        print(json.dumps(S(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "GET",
                           json.loads(sys.argv[4]) if len(sys.argv) > 4 else None))[:4000])
