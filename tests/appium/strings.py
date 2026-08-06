#!/usr/bin/env python3
"""Locale-aware label lookup for the sweep (REQ-NFR-011).

The sidebar links and the breadcrumb both render `IStringLocalizer` values, so at
46.3% localization coverage (REQ-UI-050) a hardcoded English label table cannot
navigate the app: with `AppearanceLanguage='hi'` the sidebar reads
`चैट / दस्तावेज़ / कनेक्टर …` and every click misses.

The sidebar links carry NO locale-invariant handle — `identifier` is empty on every
one of them and the `href` is not exposed in the macOS accessibility tree (measured
2026-07-31, see README). So the harness resolves the label it must click from the
product's OWN resource files, keyed by resource key rather than by English text:

    SIDEBAR row  ->  resource key ("NavQdrantAdmin")
                 ->  AppStrings.<lang>.resx value, falling back to AppStrings.resx
                 ->  the string the app is actually rendering right now

Keying on the resource key means a re-translation cannot break the harness; only a
KEY RENAME can, and that fails loudly (`unknown resource key`) rather than silently.

Black-box: this READS product resource files and the app's own settings database.
It never modifies either.
"""
import os
import sqlite3
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RESX_DIR = os.environ.get(
    "TD_RESX_DIR", os.path.join(REPO, "apps", "TechieDesk.Core", "Resources"))
APP_DB = os.environ.get(
    "TD_DB", os.path.expanduser(
        "~/Library/Application Support/TechieDesk/techiedesk.db"))

NEUTRAL = "AppStrings.resx"          # the neutral resx IS English (SupportedLanguages[0])
LANG_KEY = "AppearanceLanguage"      # LanguageStore.LanguageKey
DEFAULT_LANG = "en"

_cache = {}


def _load_resx(path):
    """Return {name: value} for one .resx, or {} if it is absent."""
    if not os.path.exists(path):
        return {}
    root = ET.parse(path).getroot()
    return {d.get("name"): (d.findtext("value") or "") for d in root.findall("data")}


def app_language():
    """The language the RUNNING app is rendering, read from its own settings row.

    Same store of record the app reads (`LanguageStore`), so the harness follows the
    app rather than being told what language to expect. Opened read-only: the app
    holds this database open and the harness must never write to it.
    """
    env = os.environ.get("TD_LANG")
    if env:
        return env.strip()
    # Columns are SettingKey/SettingValue, NOT Key/Value — a `select Value …` typo here
    # fails silently back to English and the sweep then looks for English labels in a
    # Hindi app. The failure is re-raised as a warning rather than swallowed.
    uri = "file:" + APP_DB.replace("?", "%3f").replace("#", "%23") + "?mode=ro"
    try:
        con = sqlite3.connect(uri, uri=True, timeout=5)
        try:
            row = con.execute(
                "select SettingValue from InstanceSetting where SettingKey = ?",
                (LANG_KEY,)).fetchone()
        finally:
            con.close()
    except Exception as e:
        print(f"   ! could not read {LANG_KEY} from {APP_DB}: {e!r} — assuming "
              f"'{DEFAULT_LANG}'. Labels for other languages are still tried.")
        return DEFAULT_LANG
    val = (row[0] if row else "") or ""
    return val.strip() or DEFAULT_LANG


def table(lang=None):
    """Resolved {key: label} for `lang`, with per-key fallback to the neutral resx.

    Per-key (not per-file) fallback mirrors .NET's ResourceManager: a key the
    translation has not reached yet still renders in English, and the harness must
    look for the English text in that case — which is exactly what this produces.
    """
    lang = lang or app_language()
    if lang in _cache:
        return _cache[lang]
    neutral = _load_resx(os.path.join(RESX_DIR, NEUTRAL))
    merged = dict(neutral)
    if lang and lang != DEFAULT_LANG:
        for k, v in _load_resx(os.path.join(RESX_DIR, f"AppStrings.{lang}.resx")).items():
            if v:
                merged[k] = v
    _cache[lang] = merged
    return merged


def candidates(key, lang=None):
    """Every string the app might be rendering for `key`, best guess first.

    Both the localized value AND the neutral English value are returned, so the
    harness still finds the link when a Release bundle predates a translation, or
    when the language row and the running process disagree.
    """
    lang = lang or app_language()
    loc = table(lang).get(key)
    neu = table(DEFAULT_LANG).get(key)
    if loc is None and neu is None:
        raise KeyError(f"unknown resource key '{key}' in {RESX_DIR} — was it renamed?")
    out = []
    for v in (loc, neu):
        if v and v not in out:
            out.append(v)
    return out


def installed_languages():
    """Every language the resx set ships, neutral first."""
    out = [DEFAULT_LANG]
    for f in sorted(os.listdir(RESX_DIR)) if os.path.isdir(RESX_DIR) else []:
        if f.startswith("AppStrings.") and f.endswith(".resx") and f != NEUTRAL:
            code = f[len("AppStrings."):-len(".resx")]
            if code and code not in out:
                out.append(code)
    return out


def all_candidates(key):
    """Every language's rendering of `key`, the app's current language FIRST.

    Used for BOTH the sidebar click and the breadcrumb arrival gate. Widening past
    the database's language costs nothing and removes a whole failure class: a
    Release bundle whose satellite assemblies predate a resx change, or a running
    process started before the language row was edited, no longer strands the sweep.
    It cannot cause a wrong result — the key identifies the SCREEN, not the
    language, and `arrival_ok` still has to agree before anything is graded.
    """
    out = list(candidates(key))
    for lang in installed_languages():
        v = table(lang).get(key)
        if v and v not in out:
            out.append(v)
    return out


def missing_keys(keys, lang=None):
    """Keys absent from BOTH resx files — a rename the harness table has not tracked."""
    t, n = table(lang), table(DEFAULT_LANG)
    return [k for k in keys if k not in t and k not in n]


if __name__ == "__main__":
    import sys
    lang = sys.argv[1] if len(sys.argv) > 1 else app_language()
    print(f"language={lang} (db={APP_DB})")
    for k in sys.argv[2:] or ["NavChat", "NavQdrantAdmin", "NavRagConfiguration"]:
        print(f"  {k:24s} -> {candidates(k, lang)}")
