#!/usr/bin/env python3
"""Verify distant Previous/Next matches in the signed Mac viewer's Hex mode.

Usage: python3 mac-ax-viewer-hex-backward.py SOURCE REPORT.json
SOURCE is the isolated FileCatValidation panel with a.txt and b.txt. The
runner creates and removes only its own search fixture.
"""

import hashlib
import json
from pathlib import Path
import runpy
import sys


base = runpy.run_path(str(Path(__file__).with_name("mac-ax-viewer-backward.py")))
CONTENT = base["CONTENT"]
FIRST_OFFSET = base["FIRST_OFFSET"]
SECOND_OFFSET = base["SECOND_OFFSET"]
find_click = base["find_click"]
viewer_open = base["viewer_open"]
close_viewer = base["close_viewer"]
enter_query = base["enter_query"]
snapshot = base["snapshot"]
send_f3 = base["send_f3"]
FIXTURE_NAME = "native-hex-backward-search.txt"


def near(actual, expected, label):
    if actual is None or abs(actual - expected) > 65536:
        raise AssertionError(f"{label}: expected offset near {expected}, got {actual}")


def run(source):
    source = source.resolve(strict=True)
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated source fixture is not exact")
    if viewer_open():
        raise AssertionError("Close the existing viewer before this test")
    fixture = source / FIXTURE_NAME
    if fixture.exists():
        raise AssertionError("The dedicated Hex search fixture already exists")
    with fixture.open("xb") as output:
        output.write(CONTENT)
    digest = hashlib.sha256(CONTENT).digest()
    try:
        find_click("AXButton", "Reread")
        find_click("AXRow", FIXTURE_NAME)
        find_click("AXButton", "View")
        find_click("AXButton", "Hex", viewer=True)
        find_click("AXButton", "Show or hide viewer Find bar", viewer=True)
        enter_query("NEEDLE")

        find_click("AXButton", "Find next match", viewer=True)
        first = snapshot()
        near(first["offset"], FIRST_OFFSET, "first Hex match")
        find_click("AXButton", "Find next match", viewer=True)
        second = snapshot()
        near(second["offset"], SECOND_OFFSET, "second Hex match")
        if second["offset"] - first["offset"] <= 4 * 1024 * 1024:
            raise AssertionError("The distant Hex match was not reached")
        find_click("AXButton", "Find previous match", viewer=True)
        previous = snapshot()
        near(previous["offset"], FIRST_OFFSET, "previous Hex match")
        send_f3(backward=False)
        keyboard_next = snapshot()
        near(keyboard_next["offset"], SECOND_OFFSET, "Hex F3")
        send_f3(backward=True)
        keyboard_previous = snapshot()
        near(keyboard_previous["offset"], FIRST_OFFSET, "Hex Shift+F3")

        find_click("AXButton", "Find previous match", viewer=True)
        before_first = snapshot()
        if "was not found before this position" not in before_first["searchResult"]:
            raise AssertionError(f"Missing no-earlier-match feedback: {before_first}")
        if before_first["count"] != "2 matches":
            raise AssertionError(f"Missing exact Hex match count: {before_first}")
        enter_query("first")
        find_click("AXButton", "Find next match", viewer=True)
        changed = snapshot()
        near(changed["offset"], FIRST_OFFSET + len(b"NEEDLE "), "changed Hex query")
        if changed["count"] != "1 match":
            raise AssertionError(f"Hex changed query retained stale matches: {changed}")
        close_viewer()
        if hashlib.sha256(fixture.read_bytes()).digest() != digest:
            raise AssertionError("Hex search changed its source bytes")
        return {"passed": True, "platform": "macOS", "mode": "Hex",
                "firstVisibleOffset": first["offset"], "secondVisibleOffset": second["offset"],
                "previousVisibleOffset": previous["offset"],
                "matchStatus": before_first["count"], "changedQueryStatus": changed["count"],
                "keyboardF3AndShiftF3Found": True, "sourceBytesUnchanged": True}
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
