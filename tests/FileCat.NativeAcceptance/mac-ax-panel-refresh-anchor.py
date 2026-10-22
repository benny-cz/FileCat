#!/usr/bin/env python3
"""Verify a replaced file cannot keep its old Mac range-anchor row.

Usage: python3 mac-ax-panel-refresh-anchor.py BUNDLE.app REPORT.json [--narrow]
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


def nodes(helper, executable, pid):
    return snapshot_data(helper, executable, pid)["nodes"]


def rows(items):
    return [item for item in items if item["role"] == "AXRow" and
            item["name"] in {"0.txt", "a.txt", "b.txt", "c.txt", "d.txt"}]


def order(items):
    return [item["name"] for item in sorted(rows(items), key=lambda item: item["bounds"][1])]


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
    fixture = validation / "MacRefreshAnchor" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n",
                "c.txt": b"charlie\n", "d.txt": b"delta\n"}
    for name, content in original.items():
        (source / name).write_bytes(content)
    expected = {"0.txt": b"zero\n", "a.txt": original["a.txt"],
                "c.txt": original["c.txt"], "d.txt": original["d.txt"]}
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    ax_source = Path(__file__).with_name("mac-ax-panel-sort-snapshot.swift")
    if not ax_source.is_file():
        raise AssertionError("The bounded direct AX helper source is missing")
    helper = fixture / "ax-refresh-snapshot"
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

        def initial():
            found = nodes(helper, executable, pid)
            return found if order(found) == ["a.txt", "b.txt", "c.txt", "d.txt"] else None

        first = wait_for(initial, "the original four-file source")
        if narrow:
            size = tuple(int(value) for value in osa(narrow_window).split("|"))
            if not (700 <= size[0] <= 800 and 550 <= size[1] <= 650):
                raise AssertionError(f"The narrow window did not settle: {size}")
            first = wait_for(initial, "the narrow original source")
        else:
            size = None
        b = next(item for item in rows(first) if item["name"] == "b.txt")
        click("c:%d,%d" % center(b["bounds"]))
        wait_for(lambda: True if any(item["name"] == "b.txt" and item["focused"]
                 for item in rows(nodes(helper, executable, pid))) else None,
                 "b.txt as the physical range anchor")
        (source / "b.txt").unlink()
        (source / "0.txt").write_bytes(expected["0.txt"])

        osa('''tell application "System Events"
    key code 15 using {control down}
end tell''')

        def refreshed():
            found = nodes(helper, executable, pid)
            return found if order(found) == ["0.txt", "a.txt", "c.txt", "d.txt"] else None

        updated = wait_for(refreshed, "the refreshed replacement-file order")
        osa('''tell application "System Events"
    repeat 4 times
        key code 125
    end repeat
end tell''')
        wait_for(lambda: True if any(item["name"] == "d.txt" and item["focused"]
                 for item in rows(nodes(helper, executable, pid))) else None,
                 "d.txt as the deliberate current focus after refresh")
        target_row = next(item for item in rows(updated) if item["name"] == "0.txt")
        click("kd:shift", "c:%d,%d" % center(target_row["bounds"]), "ku:shift")

        def selected():
            found = nodes(helper, executable, pid)
            marked = {item["name"] for item in rows(found) if item["selected"]}
            focused = [item["name"] for item in rows(found) if item["focused"]]
            summary = [item["name"] for item in found if item["name"].startswith("Marked ")]
            if (marked == set(expected) and focused == ["0.txt"] and
                    summary == ["Marked 4 of 4 files · 25 bytes"]):
                return marked, focused, summary
            return None

        selection = wait_for(selected, "the exact current-focus range after replacing b.txt")
        if (source / "b.txt").exists() or {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                                             for name in expected} != hashes or list(target.iterdir()):
            raise AssertionError("The deliberate source replacement or empty target changed")
        return {"passed": True, "platform": "macOS", "narrow": narrow,
                "windowSize": size, "appSha256": hashlib.sha256((bundle / "Contents/MacOS/FileCat.dll").read_bytes()).hexdigest(),
                "originalOrder": order(first), "refreshedOrder": order(updated),
                "removedAnchor": "b.txt", "addedFile": "0.txt", "sourceSha256": hashes,
                "selected": sorted(selection[0]), "focused": selection[1],
                "status": selection[2][0], "sourceBytesExact": True, "targetEmpty": True}
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
