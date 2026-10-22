#!/usr/bin/env python3
"""Verify Mac range anchors do not leak into a new tab with native input.

Usage: python3 mac-ax-panel-tab-anchor.py BUNDLE.app REPORT.json [--narrow]
Keep mac-ax-panel-sort.py and mac-ax-panel-sort-snapshot.swift beside this runner.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
import subprocess
import sys
from uuid import uuid4


shared = runpy.run_path(str(Path(__file__).with_name("mac-ax-panel-sort.py")))
osa = shared["osa"]
wait_for = shared["wait_for"]
app_pids = shared["app_pids"]
snapshot_data = shared["snapshot_data"]
click = shared["click"]
foreground = shared["FOREGROUND"]
narrow_window = shared["NARROW"]

NEW_TAB = '''tell application "System Events"
    key code 17 using {control down}
end tell'''
ENTER = '''tell application "System Events"
    key code 36
end tell'''


def nodes(helper, executable, pid):
    return snapshot_data(helper, executable, pid)["nodes"]


def rows(items, names):
    return [item for item in items if item["role"] == "AXRow" and item["name"] in names]


def center(bounds):
    return round(bounds[0] + min(40, bounds[2] / 2)), round(bounds[1] + bounds[3] / 2)


def tabs(items):
    return [item["name"] for item in items if item["role"] == "AXButton" and
            ", Unlocked tab, " in item["name"]]


def run(bundle, report_path, narrow):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    if bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file():
        raise AssertionError("Use an isolated signed FileCat bundle under FileCatValidation")
    if report_path.parent.resolve() != validation:
        raise AssertionError("Write the native report under FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes the native test ambiguous")
    fixture = validation / "MacTabAnchor" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    second = source / "second"
    for folder in (source, target, data, second):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n",
                "c.txt": b"charlie\n", "d.txt": b"delta\n"}
    second_expected = {"e.txt": b"echo\n", "f.txt": b"foxtrot\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    for name, content in second_expected.items():
        (second / name).write_bytes(content)
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    second_hashes = {name: hashlib.sha256(content).hexdigest()
                     for name, content in second_expected.items()}
    ax_source = Path(__file__).with_name("mac-ax-panel-sort-snapshot.swift")
    if not ax_source.is_file():
        raise AssertionError("The bounded direct AX helper source is missing")
    helper = fixture / "ax-tab-snapshot"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    if not sdk.is_dir():
        raise AssertionError("The matching Xcode macOS SDK is unavailable")
    build = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(ax_source), "-o", str(helper)],
                           capture_output=True, text=True, timeout=30)
    if build.returncode:
        raise AssertionError("Direct AX helper build failed: " + build.stderr[-1200:])

    pid = None
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                        "--data", str(data), "--left", str(source), "--right", str(target)],
                       check=True, capture_output=True, timeout=15)
        pid = wait_for(lambda: next(iter(app_pids()), None), "the isolated Mac app")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="], check=True,
                                 capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active process is not the owned Git-built app")
        osa(foreground)

        def initial_snapshot():
            found = nodes(helper, executable, pid)
            names = {item["name"] for item in rows(found, {*expected, "second"})}
            return found if names == {*expected, "second"} and len(tabs(found)) == 2 else None

        initial = wait_for(initial_snapshot, "the source folder and two original tabs")
        if narrow:
            size = tuple(int(value) for value in osa(narrow_window).split("|"))
            if not (700 <= size[0] <= 800 and 550 <= size[1] <= 650):
                raise AssertionError(f"The narrow window did not settle: {size}")
            initial = wait_for(initial_snapshot, "the narrow source folder and two tabs")
        else:
            size = None
        d = next(item for item in rows(initial, {"d.txt"}))
        click("c:%d,%d" % center(d["bounds"]))
        wait_for(lambda: True if any(item["name"] == "d.txt" and item["focused"]
                 for item in rows(nodes(helper, executable, pid), {"d.txt"})) else None,
                 "d.txt focus in the first tab")

        osa(NEW_TAB)
        def new_tab_snapshot():
            found = nodes(helper, executable, pid)
            return found if len(tabs(found)) == 3 else None

        new_tab = wait_for(new_tab_snapshot, "a second source tab")
        folder = next((item for item in rows(new_tab, {"second"})), None)
        if folder is None:
            raise AssertionError("The second folder is absent in the new tab")
        click("c:%d,%d" % center(folder["bounds"]))
        osa(ENTER)

        def second_listing():
            found = nodes(helper, executable, pid)
            names = {item["name"] for item in rows(found, {*second_expected})}
            if names == {*second_expected} and any(str(second) in tab for tab in tabs(found)):
                return found
            return None

        current = wait_for(second_listing, "e.txt and f.txt in the new tab")
        e = next(item for item in rows(current, {"e.txt"}))
        f = next(item for item in rows(current, {"f.txt"}))
        click("c:%d,%d" % center(e["bounds"]))
        fx, fy = center(f["bounds"])
        click("kd:shift", f"c:{fx},{fy}", "ku:shift")

        def selection():
            found = nodes(helper, executable, pid)
            selected = {item["name"] for item in rows(found, {*second_expected}) if item["selected"]}
            focused = [item["name"] for item in rows(found, {*second_expected}) if item["focused"]]
            summary = [item["name"] for item in found if item["name"].startswith("Marked ")]
            if (selected == {"e.txt", "f.txt"} and focused == ["f.txt"] and
                    summary == ["Marked 2 of 2 files · 13 bytes"]):
                return selected, focused, summary, tabs(found)
            return None

        selected = wait_for(selection, "a tab-local e.txt–f.txt range")
        if ({name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                for name in expected} != hashes or
                {name: hashlib.sha256((second / name).read_bytes()).hexdigest()
                 for name in second_expected} != second_hashes or list(target.iterdir())):
            raise AssertionError("Tab navigation or selection changed fixture bytes")
        return {"passed": True, "platform": "macOS", "narrow": narrow,
                "windowSize": size, "appSha256": hashlib.sha256((bundle / "Contents/MacOS/FileCat.dll").read_bytes()).hexdigest(),
                "sourceSha256": hashes, "secondSha256": second_hashes,
                "initialTabs": tabs(initial), "newTabs": tabs(new_tab),
                "finalTabs": selected[3], "selected": sorted(selected[0]),
                "focused": selected[1], "status": selected[2][0],
                "sourceBytesUnchanged": True, "targetEmpty": True}
    finally:
        if pid is not None:
            command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                     capture_output=True, text=True, timeout=5).stdout.strip()
            if command.startswith(str(executable) + " --new-instance --data " + str(data)):
                os.kill(pid, signal.SIGTERM)


def main():
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != "--narrow"):
        raise SystemExit(__doc__)
    path = Path(sys.argv[2])
    try:
        report = run(Path(sys.argv[1]), path, len(sys.argv) == 4)
    except Exception as exc:
        report = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
