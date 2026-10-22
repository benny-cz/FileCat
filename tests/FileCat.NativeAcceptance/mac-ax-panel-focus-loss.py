#!/usr/bin/env python3
"""Verify a held Mac Shift gesture retires when FileCat loses window focus.

Usage: python3 mac-ax-panel-focus-loss.py BUNDLE.app REPORT.json [--narrow]
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

FRONT_PROCESS = '''tell application "System Events"
    return name of first process whose frontmost is true
end tell'''
FINDER = '''tell application "Finder" to activate'''


def nodes(helper, executable, pid):
    return snapshot_data(helper, executable, pid)["nodes"]


def selected_state(items):
    rows = [item for item in items if item["role"] == "AXRow" and
            item["name"] in {"a.txt", "b.txt", "c.txt", "d.txt"}]
    return ({item["name"] for item in rows if item["selected"]},
            [item["name"] for item in rows if item["focused"]],
            [item["name"] for item in items if item["name"].startswith("Marked ")])


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
    fixture = validation / "MacFocusLoss" / uuid4().hex
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
    helper = fixture / "ax-focus-snapshot"
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
    shift_held = False
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
            rows = [item for item in found if item["role"] == "AXRow" and
                    item["name"] in expected]
            return found if {item["name"] for item in rows} == {*expected} else None

        initial = wait_for(initial_snapshot, "the complete four-file source")
        if narrow:
            size = tuple(int(value) for value in osa(narrow_window).split("|"))
            if not (700 <= size[0] <= 800 and 550 <= size[1] <= 650):
                raise AssertionError(f"The narrow window did not settle: {size}")
            initial = wait_for(initial_snapshot, "the narrow four-file source")
        else:
            size = None
        b = next(item for item in initial if item["role"] == "AXRow" and item["name"] == "b.txt")
        bounds = b["bounds"]
        if bounds[2] < 35 or bounds[3] < 15:
            raise AssertionError("The b.txt source row has unusable bounds")
        bx = round(bounds[0] + min(40, bounds[2] / 2))
        by = round(bounds[1] + bounds[3] / 2)
        click(f"c:{bx},{by}")
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 (set(), ["b.txt"]) else None, "the b.txt pointer anchor")
        click("kd:shift", "kp:arrow-down", "ku:shift")
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 ({"b.txt"}, ["c.txt"]) else None, "b.txt marked by Shift+Down")
        osa('''tell application "System Events"
    key code 126
end tell''')
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 ({"b.txt"}, ["b.txt"]) else None, "ordinary Up preserving the b.txt mark")

        # Keep the physical Shift key down while a different application owns
        # focus; release it there, so FileCat cannot observe the key-up itself.
        click("kd:shift", "kp:arrow-down")
        shift_held = True
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 (set(), ["c.txt"]) else None, "held Shift+Down unmarking b.txt")
        osa(FINDER)
        wait_for(lambda: osa(FRONT_PROCESS) == "Finder", "Finder owning the Mac desktop")
        click("ku:shift")
        shift_held = False
        osa(foreground)
        wait_for(lambda: osa(FRONT_PROCESS) == "FileCat", "FileCat restored to foreground")
        click("kd:shift", "kp:arrow-down", "ku:shift")

        def fresh_range():
            selected, focused, summary = selected_state(nodes(helper, executable, pid))
            if (selected == {"c.txt"} and focused == ["d.txt"] and
                    summary == ["Marked 1 of 4 files · 8 bytes"]):
                return selected, focused, summary
            return None

        final = wait_for(fresh_range, "a fresh c.txt mark after window deactivation")

        osa('''tell application "System Events"
    key code 126
end tell''')
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 ({"c.txt"}, ["c.txt"]) else None, "ordinary Up preserving c.txt")
        click("kd:shift", "kp:arrow-down")
        shift_held = True
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 (set(), ["d.txt"]) else None, "held Shift+Down unmarking c.txt")
        fields = [item for item in nodes(helper, executable, pid)
                  if item["role"] == "AXTextField" and item["name"] == "Command line"]
        if len(fields) != 1 or fields[0]["bounds"][2] < 40:
            raise AssertionError("The command line has no usable native AX bounds")
        cb = fields[0]["bounds"]
        click(f"c:{round(cb[0] + min(80, cb[2] / 2))},{round(cb[1] + cb[3] / 2)}")
        wait_for(lambda: True if any(item["name"] == "Command line" and item["focused"]
                 for item in nodes(helper, executable, pid)) else None,
                 "command-line focus while Shift is held")
        click("ku:shift")
        shift_held = False
        osa('''tell application "System Events"
    key code 53
end tell''')
        wait_for(lambda: True if selected_state(nodes(helper, executable, pid))[:2] ==
                 (set(), ["d.txt"]) else None, "list focus restored by Escape")
        click("kd:shift", "kp:arrow-up", "ku:shift")

        def fresh_command_line_range():
            selected, focused, summary = selected_state(nodes(helper, executable, pid))
            if (selected == {"d.txt"} and focused == ["c.txt"] and
                    summary == ["Marked 1 of 4 files · 6 bytes"]):
                return selected, focused, summary
            return None

        command_line_final = wait_for(fresh_command_line_range,
                                      "a fresh d.txt mark after command-line focus")
        if {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                for name in expected} != hashes or list(target.iterdir()):
            raise AssertionError("The focus gesture changed fixture bytes")
        return {"passed": True, "platform": "macOS", "narrow": narrow,
                "windowSize": size, "appSha256": hashlib.sha256((bundle / "Contents/MacOS/FileCat.dll").read_bytes()).hexdigest(),
                "sourceSha256": hashes, "deactivatedTo": "Finder",
                "shiftReleasedOutsideFileCat": True,
                "selected": sorted(final[0]), "focused": final[1], "status": final[2][0],
                "afterCommandLine": {"selected": sorted(command_line_final[0]),
                                     "focused": command_line_final[1],
                                     "status": command_line_final[2][0],
                                     "shiftReleasedInCommandLine": True},
                "sourceBytesUnchanged": True, "targetEmpty": True}
    finally:
        if shift_held:
            click("ku:shift")
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
