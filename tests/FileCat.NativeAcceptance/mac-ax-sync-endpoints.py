#!/usr/bin/env python3
"""Check full sync identities, direction and narrow review on the isolated Mac.

Usage: python3 mac-ax-sync-endpoints.py BUNDLE.app REPORT.json
Requires the logged-in desktop and approved FileCatCapture.app.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
import subprocess
import sys
import time
from uuid import uuid4


shared = runpy.run_path(str(Path(__file__).with_name("mac-ax-panel-sort.py")))
osa = shared["osa"]
wait_for = shared["wait_for"]
app_pids = shared["app_pids"]
click = shared["click"]
capture = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))["capture"]


def window_exists(title):
    return osa('''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains TITLE then return "yes"
    end repeat
end tell
return "no"
'''.replace("TITLE", json.dumps(title))) == "yes"


def resize(width, height):
    raw = osa('''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) is "Synchronize folders" then
            set size of w to {WIDTH, HEIGHT}
            set position of w to {80, 60}
            set s to size of w
            return (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
    end repeat
end tell
error "Sync review absent during resize"
'''.replace("WIDTH", str(width)).replace("HEIGHT", str(height)))
    actual = [int(part) for part in raw.split("|")]
    if len(actual) != 2 or any(abs(got - want) > 2 for got, want in zip(actual, (width, height))):
        raise AssertionError(f"Mac sync review refused {width}x{height}: {actual}")
    return actual


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def run(bundle, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    helper = validation / "FileCatCapture.app"
    if (bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file() or
            report_path.parent != validation or not helper.is_dir()):
        raise AssertionError("Use the isolated signed app, report and approved capture helper in FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process would make the Mac review ambiguous")
    fixture = validation / "MacSyncEndpoints" / uuid4().hex
    left = fixture / "source/active-client-work-with-a-long-location/Project"
    right = fixture / "target/archived-client-work-with-a-long-location/Project"
    data = fixture / "data"
    for path in (left, right, data):
        path.mkdir(parents=True)
    (left / "current.txt").write_bytes(b"new source content\n")
    (right / "old.txt").write_bytes(b"old target content\n")
    expected = {str(path): sha256(path) for path in (left / "current.txt", right / "old.txt")}
    receipt = {"passed": False, "bundle": str(bundle), "appDllSha256": sha256(executable.with_name("FileCat.dll")),
               "fixture": str(fixture), "left": str(left), "right": str(right), "steps": []}
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    ax_helper = fixture / "ax-sync-snapshot"
    built = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk),
                            str(Path(__file__).with_name("mac-ax-rename-snapshot.swift")),
                            "-o", str(ax_helper)], capture_output=True, text=True, timeout=40)
    if built.returncode:
        raise AssertionError("Direct AX helper build failed: " + built.stderr[-1000:])
    pid = None
    try:
        opened = subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                                 "--data", str(data), "--left", str(left), "--right", str(right)],
                                capture_output=True, text=True, timeout=15)
        if opened.returncode:
            raise AssertionError(opened.stderr.strip() or "Mac test app did not open")
        pid = wait_for(lambda: next(iter(app_pids()), None), "isolated Mac app")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 capture_output=True, text=True, check=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The process is not the exact isolated app and data profile")
        def nodes():
            completed = subprocess.run([str(ax_helper), str(executable), str(pid)],
                                       capture_output=True, text=True, timeout=15)
            if completed.returncode:
                raise AssertionError(completed.stderr.strip() or "Mac AX snapshot failed")
            state = json.loads(completed.stdout)
            if state["visited"] >= 5000 or state["windows"] < 1:
                raise AssertionError("The bounded Mac AX tree is unavailable")
            return state["nodes"]

        def match(role, name, source=None, starts=False):
            found = [n for n in (source if source is not None else nodes()) if
                     n["role"] == role and (n["name"].startswith(name) if starts else n["name"] == name)]
            if len(found) != 1:
                raise AssertionError(f"Expected one {role} {name!r}, found {len(found)}")
            return found[0]

        def click_node(node):
            x, y, width, height = node["bounds"]
            if width < 8 or height < 8:
                raise AssertionError(f"Unusable Mac AX bounds for {node['name']!r}: {node['bounds']}")
            click(f"c:{round(x + width / 2)},{round(y + height / 2)}")

        def endpoint_values():
            current = nodes()
            source = match("AXTextField", "Source folder: ", current, starts=True)
            target = match("AXTextField", "Target folder: ", current, starts=True)
            return [source["name"], source["value"], target["name"], target["value"]]

        wait_for(lambda: window_exists("FileCat"), "main FileCat window")
        time.sleep(0.5)
        osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 35 using {control down, shift down}
        delay 0.25
        keystroke "a" using {command down}
        key code 51
        keystroke "compare directories"
        key code 36
    end tell
end tell
''')
        checkbox = wait_for(lambda: match("AXCheckBox",
            "Include subfolders: a preview of every difference below, marking nothing"),
            "recursive Compare directories choice")
        click_node(checkbox)
        wait_for(lambda: match("AXCheckBox",
            "Include subfolders: a preview of every difference below, marking nothing")["value"] == "1",
            "checked recursive comparison")
        click_node(match("AXButton", "Compare"))
        wait_for(lambda: window_exists("Compare folders:"), "comparison preview", seconds=30)
        sync_button = wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Synchronize…" and n["enabled"]), None),
            "enabled Synchronize action", seconds=40)
        click_node(sync_button)
        wait_for(lambda: window_exists("Synchronize folders"), "sync review")
        receipt["steps"].append("Compared distinct, same-basename endpoint folders")
        values = endpoint_values()
        if values != ["Source folder: " + str(left), str(left), "Target folder: " + str(right), str(right)]:
            raise AssertionError(f"Mac AX lost complete first-direction paths: {values!r}")
        receipt["steps"].append("Full selectable Source and Target path values and names verified")
        direction = next((n for n in nodes() if n["role"] in ("AXComboBox", "AXPopUpButton") and
                          n["name"].startswith("Synchronization direction: from ")), None)
        if direction is None:
            raise AssertionError("Mac AX direction selector is unavailable")
        click_node(direction)
        osa('tell application "System Events" to key code 125')
        osa('tell application "System Events" to key code 36')
        wait_for(lambda: endpoint_values()[1::2] == [str(right), str(left)], "reversed full endpoint roles")
        receipt["steps"].append("Physical Down reversed the complete endpoint roles")
        mode = next((n for n in nodes() if n["role"] in ("AXComboBox", "AXPopUpButton") and
                     n["name"] == "Mode"), None)
        if mode is None:
            raise AssertionError("Mac AX synchronization mode is unavailable")
        click_node(mode)
        osa('tell application "System Events" to key code 125')
        osa('tell application "System Events" to key code 36')
        warning = wait_for(lambda: next((n["name"] for n in nodes() if n["role"] == "AXStaticText" and
            f'Mirror changes the target "{left}"' in n["name"]), None), "Mirror warning with full target")
        receipt["mirrorWarning"] = warning
        receipt["steps"].append("Mirror warning named the reversed full target")
        receipt["narrowSize"] = resize(640, 600)
        if endpoint_values()[1::2] != [str(right), str(left)]:
            raise AssertionError("Narrow Mac review lost the complete endpoint values")
        time.sleep(0.3)
        receipt["narrowCapture"] = capture(helper, validation)
        receipt["steps"].append("Captured native 640x600 sync review")
        click_node(match("AXButton", "Cancel"))
        if {str(path): sha256(Path(path)) for path in expected} != expected:
            raise AssertionError("Cancel changed exact Mac fixture bytes")
        if sorted(p.name for p in left.iterdir()) != ["current.txt"] or sorted(p.name for p in right.iterdir()) != ["old.txt"]:
            raise AssertionError("Cancel changed exact Mac fixture trees")
        receipt["steps"].append("Cancel preserved both exact trees and file hashes")
        receipt["passed"] = True
    except Exception as exc:
        receipt["error"] = f"{type(exc).__name__}: {exc}"
        if pid is not None and pid in app_pids():
            try:
                receipt["windows"] = osa('''tell application "System Events" to tell process "FileCat"
                    return name of windows
                end tell''')
            except Exception as diagnostic:
                receipt["windowsError"] = str(diagnostic)
            try:
                receipt["decisionElements"] = osa('''
tell application "System Events" to tell process "FileCat"
    set found to {}
    repeat with w in windows
        set end of found to "WINDOW:" & (name of w as text)
        repeat with e in entire contents of w
            try
                set r to role of e as text
                if r is "AXCheckBox" or r is "AXButton" then set end of found to r & ":" & (name of e as text)
            end try
        end repeat
    end repeat
    return found
end tell
''')
            except Exception as diagnostic:
                receipt["decisionElementsError"] = str(diagnostic)
            try:
                developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                                           capture_output=True, text=True, timeout=5).stdout.strip()
                sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
                direct = fixture / "ax-sync-snapshot"
                source = Path(__file__).with_name("mac-ax-rename-snapshot.swift")
                subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(source), "-o", str(direct)],
                               check=True, capture_output=True, text=True, timeout=40)
                data = subprocess.run([str(direct), str(executable), str(pid)], check=True,
                                      capture_output=True, text=True, timeout=15)
                snapshot = json.loads(data.stdout)
                receipt["directAX"] = {"visited": snapshot["visited"], "windows": snapshot["windows"],
                                       "controls": [n for n in snapshot["nodes"] if
                                                    n["role"] == "AXCheckBox" or
                                                    any(word in n["name"] for word in
                                                        ("Compare", "subfolders", "Cancel"))]}
            except Exception as diagnostic:
                receipt["directAXError"] = str(diagnostic)
            try:
                receipt["failureCapture"] = capture(helper, validation)
            except Exception as diagnostic:
                receipt["captureError"] = str(diagnostic)
    finally:
        if pid is not None and pid in app_pids():
            os.kill(pid, signal.SIGTERM)
        report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0 if receipt["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    raise SystemExit(run(Path(sys.argv[1]), Path(sys.argv[2])))
