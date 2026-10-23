#!/usr/bin/env python3
"""Measure bounded Mac accessibility work with a 2,000-file listing.

Usage: python3 mac-ax-large-listing.py SOURCE TARGET REPORT.json
Run with one signed validation FileCat process showing the isolated panels.
"""

import hashlib
import json
from pathlib import Path
import re
import runpy
import subprocess
import sys
import tempfile
import time


helpers = runpy.run_path(str(Path(__file__).with_name("mac-ax-viewer-mode-layout.py")))
panel = runpy.run_path(str(Path(__file__).with_name("mac-ax-panel-sort.py")))
osa = helpers["osa"]
focused_name = helpers["focused_name"]
viewer_open = helpers["viewer_open"]
snapshot_data = panel["snapshot_data"]
click = panel["click"]
PREFIX = "native-list-"
COUNT = 2000


def snapshot(helper, executable, pid):
    started = time.perf_counter()
    data = snapshot_data(helper, executable, pid)
    rows = [node for node in data["nodes"] if node["role"] == "AXRow"]
    statuses = [node["name"] for node in data["nodes"] if node["role"] == "AXStaticText"
                and "folders" in node["name"] and "files" in node["name"]]
    if not statuses:
        raise AssertionError(f"No listing status was exposed: {data['nodes'][:10]}")
    status = max(statuses, key=lambda value: file_count(value) or -1)
    return {"rowCount": len(rows), "focused": [node["name"] for node in rows if node["focused"]],
            "status": status, "fileCount": file_count(status), "visited": data["visited"],
            "querySeconds": round(time.perf_counter() - started, 3), "rows": rows}


def file_count(status):
    match = re.search(r"(\d[\d\s,.]*?)\s+files\b", status)
    return int(re.sub(r"\D", "", match.group(1))) if match else None


def await_count(count, helper, executable, pid):
    deadline = time.monotonic() + 30
    last = None
    while time.monotonic() < deadline:
        last = snapshot(helper, executable, pid)
        if last["fileCount"] == count:
            return last
        time.sleep(0.3)
    raise AssertionError(f"Listing did not show {count} files: "
                         f"{ {key: value for key, value in last.items() if key != 'rows'} if last else None}")


def focus_row(name, rows):
    row = next((node for node in rows if node["name"] == name), None)
    if row is None:
        raise AssertionError(f"The first generated row is not visible: {name}")
    x, y, width, height = row["bounds"]
    if width < 40 or height < 12:
        raise AssertionError(f"The generated row has invalid bounds: {row}")
    click(f"c:{round(x + min(40, width / 2))},{round(y + height / 2)}")
    if focused_name() != name:
        raise AssertionError(f"Could not focus {name}: actual={focused_name()}")
    return row["bounds"]


def run(source, target):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    source = source.resolve(strict=True)
    target = target.resolve(strict=True)
    if source != validation / "source" or target != validation / "LayoutCheck-target":
        raise AssertionError("Use only the isolated validation panels")
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    if {p.name: p.read_bytes() for p in source.iterdir()} != original or list(target.iterdir()):
        raise AssertionError("The isolated source or target fixture differs")
    pids = subprocess.check_output(["/usr/bin/pgrep", "-x", "FileCat"], text=True).split()
    if len(pids) != 1:
        raise AssertionError("Exactly one validation FileCat process must be running")
    command = subprocess.check_output(["/bin/ps", "-p", pids[0], "-o", "command="], text=True).strip()
    executable = Path(command.split(" --new-instance", 1)[0]).resolve(strict=True)
    if validation not in executable.parents or executable.name != "FileCat" or executable.parent.name != "MacOS":
        raise AssertionError("The running FileCat is not a validation app")
    if viewer_open():
        raise AssertionError("Close the viewer before the listing test")
    names = [f"{PREFIX}{index:04d}.txt" for index in range(COUNT)]
    if any((source / name).exists() for name in names):
        raise AssertionError("A generated fixture already exists")
    created = []
    helper_dir = tempfile.TemporaryDirectory(prefix="FileCatLargeList-", dir=validation)
    helper = Path(helper_dir.name) / "ax-snapshot"
    result = {"passed": False, "platform": "macOS", "generatedFiles": COUNT,
              "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest()}
    primary_error = None
    try:
        developer = subprocess.check_output(["/usr/bin/xcode-select", "-p"], text=True).strip()
        sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
        source_code = Path(__file__).with_name("mac-ax-panel-sort-snapshot.swift")
        if not sdk.is_dir() or not source_code.is_file():
            raise AssertionError("The native AX helper or matching macOS SDK is unavailable")
        build = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(source_code), "-o", str(helper)],
                               capture_output=True, text=True, timeout=30)
        if build.returncode:
            raise AssertionError("The bounded AX snapshot helper did not build: " + build.stderr[-1200:])
        for name in names:
            path = source / name
            path.write_bytes(b"")
            created.append(path)
        osa('''tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 15 using {control down}
end tell''')
        first = await_count(COUNT + len(original), helper, executable, int(pids[0]))
        if first["rowCount"] > 120 or first["visited"] > 800 or first["querySeconds"] > 5:
            raise AssertionError(f"Accessibility listing was not bounded: {first}")
        focused_bounds = focus_row(names[0], first["rows"])
        started = time.perf_counter()
        osa('''tell application "System Events" to tell process "FileCat"
    repeat 10 times
        key code 125
    end repeat
end tell''')
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline and focused_name() != names[10]:
            time.sleep(0.1)
        navigation_seconds = round(time.perf_counter() - started, 3)
        if focused_name() != names[10] or navigation_seconds > 10:
            raise AssertionError(f"Ten Down keys did not reach {names[10]} within 10 seconds")
        last = snapshot(helper, executable, int(pids[0]))
        if (last["fileCount"] != COUNT + len(original) or last["rowCount"] > 120
                or last["visited"] > 800 or last["querySeconds"] > 5):
            raise AssertionError(f"Listing state changed during navigation: {last}")
        if (any(not path.is_file() or path.stat().st_size != 0 for path in created)
                or {p.name: p.read_bytes() for p in source.iterdir() if p.name in original} != original
                or list(target.iterdir())):
            raise AssertionError("The original source or empty target changed")
        result.update({"passed": True,
                       "loaded": {key: value for key, value in first.items() if key != "rows"},
                       "afterNavigation": {key: value for key, value in last.items() if key != "rows"},
                       "initialFocusedRowBounds": focused_bounds,
                       "tenDownKeysSeconds": navigation_seconds,
                       "originalSourceBytesUnchanged": True, "targetEmpty": True})
        return result
    except BaseException as error:
        primary_error = error
        raise
    finally:
        cleanup_error = None
        for path in created:
            try:
                if path.is_file() or path.is_symlink():
                    path.unlink()
            except OSError as error:
                if cleanup_error is None:
                    cleanup_error = error
        try:
            osa('''tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 15 using {control down}
end tell''')
            await_count(len(original), helper, executable, int(pids[0]))
        except Exception as error:
            if cleanup_error is None:
                cleanup_error = error
        finally:
            helper_dir.cleanup()
        if cleanup_error is not None and primary_error is None:
            raise cleanup_error


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    try:
        report = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as exc:
        report = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[3]).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
