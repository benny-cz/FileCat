#!/usr/bin/env python3
"""Check Mac Command-click marks and the following range anchor.

Usage: python3 mac-ax-panel-command-click.py BUNDLE.app REPORT.json [--narrow]
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


def rows(nodes):
    return [item for item in nodes if item["role"] == "AXRow" and
            item["name"] in {"a.txt", "b.txt", "c.txt", "d.txt"}]


def center(bounds):
    return round(bounds[0] + min(40, bounds[2] / 2)), round(bounds[1] + bounds[3] / 2)


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
    fixture = validation / "MacCommandClick" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n",
                "c.txt": b"charlie\n", "d.txt": b"delta\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    ax_source = Path(__file__).with_name("mac-ax-panel-sort-snapshot.swift")
    if not ax_source.is_file():
        raise AssertionError("The bounded direct AX helper source is missing")
    helper = fixture / "ax-command-click-snapshot"
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

        def nodes():
            return snapshot_data(helper, executable, pid)["nodes"]

        def initial():
            found = rows(nodes())
            return found if {item["name"] for item in found} == set(expected) else None

        first = wait_for(initial, "the complete four-file source")
        if narrow:
            size = tuple(int(value) for value in osa(narrow_window).split("|"))
            if not (700 <= size[0] <= 800 and 550 <= size[1] <= 650):
                raise AssertionError(f"The narrow window did not settle: {size}")
            first = wait_for(initial, "the narrow source")
        else:
            size = None
        bounds = {item["name"]: item["bounds"] for item in first}
        if any(rect[2] < 35 or rect[3] < 15 for rect in bounds.values()):
            raise AssertionError("A source row has unusable native bounds")
        click("c:%d,%d" % center(bounds["b.txt"]))
        wait_for(lambda: True if [item["name"] for item in rows(nodes()) if item["focused"]] ==
                 ["b.txt"] else None, "the physical b.txt anchor")

        click("kd:cmd", "c:%d,%d" % center(bounds["d.txt"]), "ku:cmd")

        def state(marked, focused, summary):
            found = nodes()
            actual_marks = {item["name"] for item in rows(found) if item["selected"]}
            actual_focus = [item["name"] for item in rows(found) if item["focused"]]
            actual_summary = [item["name"] for item in found if item["name"].startswith("Marked ")]
            return (actual_marks, actual_focus, actual_summary) if (
                actual_marks == marked and actual_focus == [focused] and
                actual_summary == [summary]) else None

        try:
            additive = wait_for(lambda: state({"d.txt"}, "d.txt", "Marked 1 of 4 files · 6 bytes"),
                                "Command-click to mark only d.txt", seconds=4)
        except AssertionError as exc:
            observed = nodes()
            marks = sorted(item["name"] for item in rows(observed) if item["selected"])
            focus = [item["name"] for item in rows(observed) if item["focused"]]
            summary = [item["name"] for item in observed if item["name"].startswith("Marked ")]
            raise AssertionError(f"{exc}; observed marks={marks}, focus={focus}, status={summary}") from exc
        click("kd:shift", "c:%d,%d" % center(bounds["c.txt"]), "ku:shift")
        range_result = wait_for(lambda: state({"b.txt", "c.txt", "d.txt"}, "c.txt",
                                               "Marked 3 of 4 files · 19 bytes"),
                                "Shift-click to extend from b.txt while retaining d.txt")
        click("kd:cmd", "c:%d,%d" % center(bounds["d.txt"]), "ku:cmd")
        toggle_off = wait_for(lambda: state({"b.txt", "c.txt"}, "d.txt",
                                             "Marked 2 of 4 files · 13 bytes"),
                              "Command-click to unmark d.txt")
        if {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                for name in expected} != hashes or list(target.iterdir()):
            raise AssertionError("The modifier gesture changed fixture bytes")
        return {"passed": True, "platform": "macOS", "narrow": narrow,
                "windowSize": size, "appSha256": hashlib.sha256((bundle / "Contents/MacOS/FileCat.dll").read_bytes()).hexdigest(),
                "sourceSha256": hashes, "afterCommandClick": sorted(additive[0]),
                "afterRange": sorted(range_result[0]), "afterToggleOff": sorted(toggle_off[0]),
                "finalFocus": toggle_off[1], "finalStatus": toggle_off[2][0],
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
