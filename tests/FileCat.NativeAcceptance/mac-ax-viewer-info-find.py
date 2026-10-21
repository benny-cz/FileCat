#!/usr/bin/env python3
"""Verify native Mac Info-mode search on a test-owned Mach-O copy.

Usage: python3 mac-ax-viewer-info-find.py SOURCE REPORT.json
SOURCE is the isolated FileCatValidation source containing a.txt and b.txt.
"""

import hashlib
import json
from pathlib import Path
import runpy
import sys
import time


base = runpy.run_path(str(Path(__file__).with_name("mac-ax-viewer-backward.py")))
find_click = base["find_click"]
viewer_open = base["viewer_open"]
close_viewer = base["close_viewer"]
enter_query = base["enter_query"]
snapshot = base["snapshot"]
APP = Path.home() / "FileCatValidation" / "FileCatValidation.app" / "Contents" / "MacOS" / "FileCat"
FIXTURE_NAME = "native-info-search.bin"


def settled_count(expected):
    deadline = time.monotonic() + 12
    while time.monotonic() < deadline:
        state = snapshot()
        if state["count"] == expected:
            return state
        time.sleep(0.25)
    raise AssertionError(f"Info search did not show {expected}")


def run(source):
    source = source.resolve(strict=True)
    app = APP.resolve(strict=True)
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated two-file source fixture is not exact")
    if viewer_open():
        raise AssertionError("Close the existing viewer before this test")
    fixture = source / FIXTURE_NAME
    if fixture.exists():
        raise AssertionError("The test-owned Mach-O copy already exists")
    digest = hashlib.sha256(app.read_bytes()).digest()
    with fixture.open("xb") as output:
        output.write(app.read_bytes())
    try:
        find_click("AXButton", "Reread")
        find_click("AXRow", FIXTURE_NAME)
        find_click("AXButton", "View")
        find_click("AXButton", "Info", viewer=True)
        time.sleep(0.5)
        find_click("AXButton", "Show or hide viewer Find bar", viewer=True)
        enter_query("Mach-O")
        find_click("AXButton", "Find next match", viewer=True)
        first = settled_count("1 match")
        find_click("AXButton", "Find previous match", viewer=True)
        before_first = snapshot()
        if "was not found before this position" not in before_first["searchResult"]:
            raise AssertionError(f"Missing Info no-earlier-match feedback: {before_first}")
        enter_query("Load commands")
        find_click("AXButton", "Find next match", viewer=True)
        changed = settled_count("2 matches")
        close_viewer()
        if hashlib.sha256(fixture.read_bytes()).digest() != digest:
            raise AssertionError("Info search changed the Mach-O copy")
        return {"passed": True, "platform": "macOS", "mode": "Info",
                "firstMatchCount": first["count"], "changedQueryCount": changed["count"],
                "noEarlierMatchFeedback": True, "sourceBytesUnchanged": True}
    finally:
        close_viewer()
        if fixture.exists() and hashlib.sha256(fixture.read_bytes()).digest() == digest:
            fixture.unlink()
            find_click("AXButton", "Reread")


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
