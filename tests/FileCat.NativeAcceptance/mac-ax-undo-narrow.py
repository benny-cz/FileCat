#!/usr/bin/env python3
"""Exercise real Move/Undo and narrow review/report on an isolated Mac.

Usage: python3 mac-ax-undo-narrow.py BUNDLE.app REPORT.json
Run in the unlocked desktop with approved FileCatCapture.app.
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
osa, wait_for, app_pids, click = (shared[name] for name in
                                   ("osa", "wait_for", "app_pids", "click"))
capture = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))["capture"]


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inside(frame, area):
    x, y, width, height = area
    fx, fy, fw, fh = frame
    return width > 0 and height > 0 and x >= fx and y >= fy and x + width <= fx + fw and y + height <= fy + fh


def resize(title, width, height):
    script = '''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains TITLE then
            set position of w to {80, 40}
            set size of w to {WIDTH, HEIGHT}
            set s to size of w
            return (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
    end repeat
end tell
error "Owned window absent during resize"
'''.replace("TITLE", json.dumps(title, ensure_ascii=False)).replace("WIDTH", str(width)).replace("HEIGHT", str(height))
    actual = [int(part) for part in osa(script).split("|")]
    if len(actual) != 2 or any(abs(got - expected) > 2 for got, expected in zip(actual, (width, height))):
        raise AssertionError(f"Mac window refused narrow {width}x{height}: {actual}")
    time.sleep(0.3)
    return actual


def run(bundle, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    capture_app = validation / "FileCatCapture.app"
    if (bundle.parent != validation or bundle.suffix != ".app" or
            not executable.is_file() or report_path.parent != validation or
            not capture_app.is_dir()):
        raise AssertionError("Use the isolated signed app, guest-local report and approved capture helper")
    if app_pids():
        raise AssertionError("An existing FileCat process makes Mac Undo ownership ambiguous")
    fixture = validation / "MacUndoNarrow" / uuid4().hex
    source = fixture / "source/active-client-work-with-a-long-project-location/daily-records"
    target = fixture / "target/archived-client-work-with-a-long-project-location/daily-records"
    data = fixture / "data"
    for path in (source, target, data):
        path.mkdir(parents=True)
    name = "approved-work-item-with-a-long-filename.txt"
    source_file, target_file = source / name, target / name
    source_file.write_bytes(b"owned inverse path wrapping\n")
    digest = sha256(source_file)
    receipt = {"passed": False, "bundle": str(bundle), "appDllSha256":
               sha256(executable.with_name("FileCat.dll")), "fixture": str(fixture),
               "source": str(source_file), "target": str(target_file)}
    sdk_root = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                              capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(sdk_root) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    helper = fixture / "ax-undo-snapshot"
    built = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk),
                            str(Path(__file__).with_name("mac-ax-rename-snapshot.swift")),
                            "-o", str(helper)], capture_output=True, text=True, timeout=40)
    if built.returncode:
        raise AssertionError("Direct AX helper build failed: " + built.stderr[-1000:])
    pid = None
    try:
        opened = subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args",
                                 "--new-instance", "--data", str(data),
                                 "--left", str(source), "--right", str(target)],
                                capture_output=True, text=True, timeout=15)
        if opened.returncode:
            raise AssertionError(opened.stderr.strip() or "Mac test app did not open")
        pid = wait_for(lambda: next(iter(app_pids()), None), "isolated Mac app")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 capture_output=True, text=True, check=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("Unexpected executable owns the Mac FileCat process")

        def nodes():
            done = subprocess.run([str(helper), str(executable), str(pid)],
                                  capture_output=True, text=True, timeout=15)
            if done.returncode:
                raise AssertionError(done.stderr.strip() or "Direct Mac AX snapshot failed")
            state = json.loads(done.stdout)
            if state["visited"] >= 5000 or state["windows"] < 1:
                raise AssertionError("Bounded Mac AX tree unavailable")
            return state["nodes"]

        def item(role, name, predicate=lambda _: True):
            matches = [node for node in nodes() if node["role"] == role and
                       node["name"] == name and predicate(node)]
            if len(matches) != 1:
                raise AssertionError(f"Expected one {role} {name!r}, found {len(matches)}")
            return matches[0]

        def click_item(node):
            x, y, width, height = node["bounds"]
            if width < 8 or height < 8:
                raise AssertionError(f"Unusable Mac AX click bounds: {node['bounds']}")
            click(f"c:{round(x + width / 2)},{round(y + height / 2)}")

        wait_for(lambda: next((n for n in nodes() if n["role"] == "AXRow" and n["name"] == name), None),
                 "exact source row")
        click_item(item("AXRow", name))
        click_item(item("AXButton", "Move / rename…"))
        click_item(wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Move" and 300 < n["bounds"][1] < 700), None), "modal Move approval"))
        wait_for(lambda: target_file.is_file() and not source_file.exists(), "completed exact Move", seconds=60)
        if sha256(target_file) != digest:
            raise AssertionError("Original Move changed the fixture bytes")
        receipt["reviewSize"] = resize("— FileCat —", 720, 540)
        click_item(wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Undo"), None), "Undo action"))
        approval = wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Run these inverse actions"), None), "Undo approval")
        current = nodes()
        frame = next((n["bounds"] for n in current if n["role"] == "AXWindow" and
                      "— FileCat —" in n["name"]), None)
        action = next((n for n in current if n["role"] == "AXRow" and
                       str(source_file) in n["name"] and str(target_file) in n["name"]), None)
        cancel = next((n for n in current if n["role"] == "AXButton" and n["name"] == "Cancel"), None)
        if frame is None or action is None or cancel is None:
            raise AssertionError("Undo review omitted the complete mapping or decisions")
        receipt["narrowReview"] = {"frame": frame, "action": action["bounds"],
                                   "approve": approval["bounds"], "cancel": cancel["bounds"]}
        if (not 700 <= frame[2] <= 740 or action["bounds"][3] <= 35 or
                any(not inside(frame, receipt["narrowReview"][key])
                    for key in ("action", "approve", "cancel"))):
            raise AssertionError("The narrow Mac Undo review clipped a complete path or decision")
        receipt["reviewCapture"] = capture(capture_app, validation)
        click_item(cancel)
        wait_for(lambda: target_file.is_file() and not source_file.exists() and
                 not any(n["role"] == "AXButton" and n["name"] == "Run these inverse actions"
                         for n in nodes()), "Cancel without inverse mutation")
        if sha256(target_file) != digest:
            raise AssertionError("Cancel changed the moved file bytes")
        receipt["cancelPreservedMove"] = True
        click_item(wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Undo"), None), "reopened Undo review"))
        approval = wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Run these inverse actions"), None), "reopened Undo approval")
        click_item(approval)
        wait_for(lambda: source_file.is_file() and not target_file.exists(),
                 "completed exact Undo", seconds=60)
        if sha256(source_file) != digest:
            raise AssertionError("Undo changed the original bytes")
        click_item(wait_for(lambda: next((n for n in nodes() if n["role"] == "AXButton" and
            n["name"] == "Undo report"), None), "retained Undo report"))
        receipt["reportSize"] = resize("Undo report", 600, 460)
        current = nodes()
        report_frame = next((n["bounds"] for n in current if n["role"] == "AXWindow" and
                             "Undo report" in n["name"]), None)
        copy_all = next((n for n in current if n["role"] == "AXButton" and n["name"] == "Copy all"), None)
        if report_frame is None or copy_all is None or not inside(report_frame, copy_all["bounds"]):
            raise AssertionError("Narrow Mac retained Undo report hid Copy all")
        receipt["reportBounds"] = {"frame": report_frame, "copyAll": copy_all["bounds"]}
        receipt["reportCapture"] = capture(capture_app, validation)
        click_item(copy_all)
        def copied_report():
            text = subprocess.run(["/usr/bin/pbpaste"], capture_output=True,
                                  text=True, timeout=5).stdout
            return text if "Undo:" in text and str(source_file) in text else None
        copied = wait_for(copied_report, "complete copied Undo report")
        receipt["reportCopy"] = {"bytes": len(copied.encode("utf-8")),
                                 "fullRestoredPath": str(source_file) in copied}
        receipt["passed"] = True
    except Exception as exc:
        receipt["error"] = f"{type(exc).__name__}: {exc}"
        if pid is not None and pid in app_pids():
            try:
                receipt["diagnosticControls"] = [n for n in nodes() if n["role"] in
                                                 ("AXWindow", "AXButton", "AXRow") and
                                                 (n["role"] == "AXWindow" or any(s in n["name"] for s in
                                                  ("Undo", "Cancel", "Copy all", "inverse")))]
            except Exception as diagnostic:
                receipt["diagnosticError"] = str(diagnostic)
    finally:
        if pid is not None and pid in app_pids():
            os.kill(pid, signal.SIGTERM)
        report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0 if receipt["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    raise SystemExit(run(Path(sys.argv[1]), Path(sys.argv[2])))
