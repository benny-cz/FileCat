#!/usr/bin/env python3
"""Check an owned FileCat Cut -> FileCat Paste move on an isolated Mac desktop.

Usage: python3 mac-ax-clipboard-owned-move.py SOURCE DESTINATION PASTEBOARD_HELPER KEY_HELPER REPORT.json
The validation app must be running with those panels. Both folders are exact
test fixtures; a passing run restores their initial contents and clipboard.
"""

import json
from pathlib import Path
import runpy
import sys
import time
from uuid import uuid4


clipboard = runpy.run_path(str(Path(__file__).with_name("mac-ax-clipboard-external-copy.py")))


def run(source, destination, pasteboard_helper, key_helper):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    pasteboard_helper = pasteboard_helper.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    source_manifest = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    target_manifest = {"a.txt": b"alpha\n"}
    if {p.name: p.read_bytes() for p in source.iterdir()} != source_manifest:
        raise AssertionError("Source must contain only exact a.txt and b.txt fixtures")
    if {p.name: p.read_bytes() for p in destination.iterdir()} != target_manifest:
        raise AssertionError("Destination must contain only the exact a.txt fixture")
    clipboard["require_no_marks"]()
    clipboard["osa"]('set the clipboard to ' + clipboard["apple_string"]("FileCat owned Cut " + str(uuid4())))
    primary_error = None
    moved = False
    try:
        before = clipboard["pasteboard_files"](pasteboard_helper)
        if before["filePaths"]:
            raise AssertionError("The test sentinel still exposes file URLs")
        clipboard["focus_source"](str(source))
        clipboard["require_no_marks"]()
        clipboard["send_key"](key_helper, "cut")
        generation = clipboard["await_file_publication"](pasteboard_helper, source / "b.txt", before["changeCount"])
        clipboard["paste_into_target"](str(destination), key_helper)
        target_b = destination / "b.txt"
        source_b = source / "b.txt"
        for _ in range(80):
            if target_b.exists() and target_b.read_bytes() == b"beta\n" and not source_b.exists():
                break
            time.sleep(0.25)
        if not target_b.exists() or target_b.read_bytes() != b"beta\n" or source_b.exists():
            raise AssertionError("Owned Cut did not move only b.txt with its exact bytes")
        time.sleep(2)
        if source_b.exists() or target_b.read_bytes() != b"beta\n":
            raise AssertionError("The moved source or destination changed after completion")
        if (source / "a.txt").read_bytes() != b"alpha\n" or (destination / "a.txt").read_bytes() != b"alpha\n":
            raise AssertionError("Unselected or pre-existing a.txt bytes changed")
        moved = True
        return {"passed": True, "platform": "macOS", "scenario": "FileCat Cut b.txt; FileCat Paste to target",
                "cutPublishedExactFile": True, "publicationGenerationAdvanced": generation > before["changeCount"],
                "ownedMoveCompleted": True, "movedBytesMatch": True,
                "unselectedSourceAndTargetBytesPreserved": True}
    except Exception as exc:
        primary_error = exc
        raise
    finally:
        clipboard_error = None
        try:
            clipboard["osa"]('set the clipboard to "FileCat native owned Cut test complete"')
        except Exception as exc:
            clipboard_error = exc
        if moved:
            (destination / "b.txt").unlink()
            (source / "b.txt").write_bytes(b"beta\n")
        if clipboard_error is not None and primary_error is None:
            raise clipboard_error


def main():
    if len(sys.argv) != 6:
        raise SystemExit("Usage: mac-ax-clipboard-owned-move.py SOURCE DESTINATION PASTEBOARD_HELPER KEY_HELPER REPORT.json")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[5]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
