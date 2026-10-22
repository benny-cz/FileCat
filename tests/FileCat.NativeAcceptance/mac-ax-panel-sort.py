#!/usr/bin/env python3
"""Exercise visible per-panel sorting on an isolated, Git-built macOS app.

Usage: python3 mac-ax-panel-sort.py BUNDLE.app REPORT.json [--narrow]
Requires the logged-in desktop, Accessibility access for osascript and cliclick.
"""

import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from uuid import uuid4


FOREGROUND = '''tell application "System Events" to tell process "FileCat"
    set frontmost to true
end tell'''

KEY_FIRST = '''tell application "System Events"
    key code 125
    key code 36
end tell'''

NARROW = '''tell application "System Events" to tell process "FileCat"
    set position of window 1 to {80, 40}
    set size of window 1 to {760, 600}
    set s to size of window 1
    return ((item 1 of s) as text) & "|" & ((item 2 of s) as text)
end tell'''


def osa(script):
    done = subprocess.run(["/usr/bin/osascript", "-e", script], capture_output=True,
                          text=True, timeout=30)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "macOS accessibility action failed")
    return done.stdout.strip()


def wait_for(check, description, seconds=20):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        try:
            value = check()
            if value:
                return value
        except AssertionError:
            pass  # The owned window may still be starting or replacing its popup.
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def app_pids():
    result = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True,
                            text=True, timeout=5)
    return {int(value) for value in result.stdout.split()}


def snapshot_data(helper, executable, pid):
    done = subprocess.run([str(helper), str(executable), str(pid)],
                          capture_output=True, text=True, timeout=12)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "Direct Mac accessibility snapshot failed")
    data = json.loads(done.stdout)
    if data["visited"] >= 3000 or data["windows"] < 1:
        raise AssertionError("The Mac accessibility tree was unavailable or unbounded")
    return data


def snapshot(helper, executable, pid):
    return [(node["role"], node["name"], tuple(node["bounds"]))
            for node in snapshot_data(helper, executable, pid)["nodes"]]


def buttons(rows, name):
    return sorted((rect for role, label, rect in rows
                   if role == "AXButton" and label == name), key=lambda rect: rect[0])


def order(rows):
    return [name for _, name, _ in sorted(
        (rect[1], name, rect) for role, name, rect in rows
        if role == "AXRow" and name in ("a.txt", "b.txt", "c.txt", "d.txt"))]


def click(*actions):
    done = subprocess.run(["/opt/homebrew/bin/cliclick", *actions],
                          capture_output=True, text=True, timeout=12)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "Native pointer click failed")


def run(bundle, report_path, narrow):
    root = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    if bundle.parent != root or bundle.suffix != ".app" or not executable.is_file():
        raise AssertionError("Use a signed, isolated FileCat bundle under FileCatValidation")
    if report_path.parent.resolve() != root:
        raise AssertionError("Write the native report under FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes the native test ambiguous")
    fixture = root / "MacSortControl" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n",
                "c.txt": b"charlie\n", "d.txt": b"delta\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    ax_source = Path(__file__).with_name("mac-ax-panel-sort-snapshot.swift")
    if not ax_source.is_file():
        raise AssertionError("The bounded native AX helper source is missing")
    ax_helper = fixture / "ax-sort-snapshot"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    xcode_sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    if not xcode_sdk.is_dir():
        raise AssertionError("The matching Xcode macOS SDK is unavailable")
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(xcode_sdk),
                               str(ax_source), "-o", str(ax_helper)],
                              capture_output=True, text=True, timeout=30)
    if compiled.returncode:
        raise AssertionError("Direct AX helper build failed: " + compiled.stderr[-1200:])
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    process = None
    result = {"passed": False, "platform": "macOS", "narrow": narrow,
              "appSha256": hashlib.sha256((bundle / "Contents" / "MacOS" / "FileCat.dll").read_bytes()).hexdigest(),
              "sourceSha256": hashes}
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                        "--data", str(data), "--left", str(source), "--right", str(target)],
                       check=True, capture_output=True, timeout=15)
        process = wait_for(lambda: next(iter(app_pids()), None), "the isolated FileCat process")
        command = subprocess.run(["/bin/ps", "-p", str(process), "-o", "command="],
                                 check=True, capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active FileCat process is not this isolated test build")
        osa(FOREGROUND)
        wait_for(lambda: len(buttons(snapshot(ax_helper, executable, process), "Sort files: Name, ascending")) == 2,
                 "both populated panel Sort controls")
        if narrow:
            size = tuple(int(value) for value in osa(NARROW).split("|"))
            if not (700 <= size[0] <= 800 and 550 <= size[1] <= 650):
                raise AssertionError(f"The narrow window did not settle: {size}")
            result["windowSize"] = size
        def initial_snapshot():
            rows = snapshot(ax_helper, executable, process)
            return rows if len(buttons(rows, "Sort files: Name, ascending")) == 2 else None

        initial = wait_for(initial_snapshot, "accessible source and target Sort buttons")
        controls = buttons(initial, "Sort files: Name, ascending")
        if order(initial) != ["a.txt", "b.txt", "c.txt", "d.txt"]:
            raise AssertionError("The four-file source did not start in ascending Name order")
        if any(rect[2] < 25 or rect[3] < 12 for rect in controls):
            raise AssertionError("A panel Sort button has unusable bounds")
        source_button, target_button = controls
        if source_button[0] >= target_button[0]:
            raise AssertionError("The source and target controls are not in separate panels")

        # cliclick must retain one physical input session for both clicks.
        # A separate AX query between them can deactivate the native popup.
        x = round(source_button[0] + source_button[2] / 2)
        y = round(source_button[1] + source_button[3] / 2)
        choice_y = round(source_button[1] + source_button[3] + 17)
        click(f"c:{x},{y}", "w:1000", f"c:{x},{choice_y}")
        descending = wait_for(lambda: (rows if order(rows) == ["d.txt", "c.txt", "b.txt", "a.txt"]
                                and len(buttons(rows, "Sort files: Name, descending")) == 1 else None)
                              if (rows := snapshot(ax_helper, executable, process)) else None,
                              "the source Name order to reverse by pointer")
        if len(buttons(descending, "Sort files: Name, ascending")) != 1:
            raise AssertionError("The target Sort control changed with the source")

        rows = {name: rect for role, name, rect in descending if role == "AXRow"}
        for name in ("b.txt", "c.txt"):
            if name not in rows or rows[name][2] < 35 or rows[name][3] < 15:
                raise AssertionError(f"The {name} source row has unusable bounds")
        def center(rect):
            return round(rect[0] + min(40, rect[2] / 2)), round(rect[1] + rect[3] / 2)

        bx, by = center(rows["b.txt"])
        cx, cy = center(rows["c.txt"])
        click(f"c:{bx},{by}")
        click("kd:shift", f"c:{cx},{cy}", "ku:shift")

        def selected_range():
            nodes = snapshot_data(ax_helper, executable, process)["nodes"]
            selected = {node["name"] for node in nodes
                        if node["role"] == "AXRow" and node["selected"]}
            focused = [node["name"] for node in nodes
                       if node["role"] == "AXRow" and node["focused"]]
            summary = [node["name"] for node in nodes
                       if node["name"].startswith("Marked ")]
            if (selected == {"b.txt", "c.txt"} and focused == ["c.txt"] and
                    summary == ["Marked 2 of 4 files · 13 bytes"]):
                return selected, focused, summary
            return None

        selection = wait_for(selected_range, "the exact b.txt–c.txt range after sorting")

        click(f"c:{x},{y}")
        choices = wait_for(lambda: (rows if any(name ==
                           "Sort by Name, currently descending; activate to reverse"
                           for role, name, _ in rows if role == "AXMenuItem") else None)
                           if (rows := snapshot(ax_helper, executable, process)) else None,
                           "the accessible current Name menu choice")
        names = [name for role, name, _ in choices if role == "AXMenuItem"]
        if len(names) < 4 or "Sort by Size" not in names:
            raise AssertionError(f"The Sort popup omitted visible sortable columns: {names}")
        osa(KEY_FIRST)
        ascending = wait_for(lambda: (rows if order(rows) == ["a.txt", "b.txt", "c.txt", "d.txt"]
                               and len(buttons(rows, "Sort files: Name, ascending")) == 2 else None)
                             if (rows := snapshot(ax_helper, executable, process)) else None,
                             "keyboard reversal of the current Name sort")
        if len(buttons(ascending, "Sort files: Name, ascending")) != 2:
            raise AssertionError("A panel retained the wrong sort direction")
        if selected_range() is None:
            raise AssertionError("Reversing the sort lost the selected source identities")
        if {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                for name in expected} != hashes or list(target.iterdir()):
            raise AssertionError("Sorting changed source bytes or destination contents")
        result.update({"passed": True, "input": "native pointer and keyboard",
                       "initialButtons": [list(rect) for rect in controls],
                       "menuChoices": names, "pointerOrder": order(descending),
                       "keyboardOrder": order(ascending), "sourceBytesUnchanged": True,
                       "targetEmpty": True,
                       "rangeAfterSort": {"selected": sorted(selection[0]),
                                          "focused": selection[1], "status": selection[2][0]}})
        return result
    finally:
        if process is not None:
            command = subprocess.run(["/bin/ps", "-p", str(process), "-o", "command="],
                                     capture_output=True, text=True, timeout=5).stdout.strip()
            if command.startswith(str(executable) + " --new-instance --data " + str(data)):
                os.kill(process, signal.SIGTERM)


def main():
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != "--narrow"):
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2])
    try:
        report = run(Path(sys.argv[1]), report_path, len(sys.argv) == 4)
    except Exception as exc:
        report = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
