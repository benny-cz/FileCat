#!/usr/bin/env python3
"""Check native Mac close-X and backdrop pointer cancellation in an isolated app.

Usage: python3 mac-ax-dialog-pointer-dismiss.py BUNDLE.app REPORT.json
Requires the logged-in Mac desktop, native AXUIElement access, and cliclick.
"""

import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
from uuid import uuid4


def osa(script):
    result = subprocess.run(["/usr/bin/osascript", "-e", script], capture_output=True,
                            text=True, timeout=15)
    if result.returncode:
        raise AssertionError(result.stderr.strip() or "System Events failed")
    return result.stdout.strip()


def wait_for(check, description, seconds=15):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        value = check()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def app_pids():
    result = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True,
                            text=True, timeout=5)
    return {int(pid) for pid in result.stdout.split()}


def open_folder():
    osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
    osa('tell application "System Events" to key code 98')  # F7


def close_bounds(helper, executable, pid, report):
    run = subprocess.run([str(helper), str(executable), str(pid), str(report)],
                         capture_output=True, text=True, timeout=15)
    value = json.loads(report.read_text(encoding="utf-8"))
    if run.returncode == 0:
        match = value["matches"][0]
        bounds = match["bounds"]
        return (bounds["x"], bounds["y"], bounds["width"], bounds["height"]), match
    if run.returncode == 1 and value.get("visited", 0) > 0 and not value.get("matches"):
        return None
    raise AssertionError("Native AX close check failed: " + value.get("error", run.stderr.strip()))


def window_bounds():
    raw = osa('''
tell application "System Events"
    tell process "FileCat"
        set p to position of window 1
        set s to size of window 1
        return ((item 1 of p) as text) & "|" & ((item 2 of p) as text) & "|" & ((item 1 of s) as text) & "|" & ((item 2 of s) as text)
    end tell
end tell
''')
    return tuple(int(n) for n in raw.split("|"))


def click(x, y):
    result = subprocess.run(["/opt/homebrew/bin/cliclick", f"c:{x},{y}"],
                            capture_output=True, text=True, timeout=8)
    if result.returncode:
        raise AssertionError(f"Mac native pointer click failed: {result.stderr.strip()}")


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: mac-ax-dialog-pointer-dismiss.py BUNDLE.app REPORT.json")
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = Path(sys.argv[1]).resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    report = Path(sys.argv[2])
    if bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file():
        raise SystemExit("Use a signed FileCat test bundle under ~/FileCatValidation")
    if app_pids():
        raise SystemExit("An existing FileCat process makes the Mac test ambiguous")
    fixture = validation / "DialogPointerMac" / uuid4().hex
    source, target, data = (fixture / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    ax_source = Path(__file__).with_name("mac-ax-dialog-label.swift")
    if not ax_source.is_file():
        raise SystemExit("The native AX dialog-label source is missing beside this runner")
    ax_helper = fixture / "mac-ax-dialog-label"
    subprocess.run(["/usr/bin/swiftc", "-o", str(ax_helper), str(ax_source)],
                   check=True, capture_output=True, timeout=120)
    ax_report = fixture / "dialog-label.json"
    digest = hashlib.sha256(executable.read_bytes()).hexdigest()
    result = {"passed": False, "bundle": str(bundle), "appSha256": digest,
              "input": "cliclick-CGEvent-mouse"}
    pid = None
    try:
        launched = subprocess.run(["/usr/bin/open", "-na", str(bundle), "--args", "--new-instance",
                                   "--data", str(data), "--left", str(source), "--right", str(target)],
                                  capture_output=True, text=True, timeout=15)
        if launched.returncode:
            raise AssertionError(launched.stderr.strip() or "Mac test app failed to launch")
        pid = wait_for(lambda: next(iter(app_pids()), None), "isolated FileCat process")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 capture_output=True, text=True, check=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable)):
            raise AssertionError("The launched FileCat process is outside the owned bundle")
        wait_for(lambda: int(osa('''tell application "System Events" to get count of windows of process "FileCat"''')) > 0,
                 "the isolated FileCat window")
        open_folder()
        (x, y, width, height), first_match = wait_for(
            lambda: close_bounds(ax_helper, executable, pid, ax_report), "the dialog close button")
        result["closeBounds"] = [x, y, width, height]
        result["closeAccessibility"] = first_match
        if width < 25 or height < 25:
            raise AssertionError("The close control has an undersized Mac pointer target")
        click(round(x + width / 2), round(y + height / 2))
        wait_for(lambda: close_bounds(ax_helper, executable, pid, ax_report) is None,
                 "close-X cancellation")
        open_folder()
        wait_for(lambda: close_bounds(ax_helper, executable, pid, ax_report),
                 "the reopened dialog close button")
        wx, wy, ww, wh = window_bounds()
        outside = [wx + 20, wy + wh - 80]
        click(*outside)
        wait_for(lambda: close_bounds(ax_helper, executable, pid, ax_report) is None,
                 "backdrop cancellation")
        if list(source.iterdir()) or list(target.iterdir()):
            raise AssertionError("A canceled Mac dialog changed the fixture")
        result.update({"passed": True, "nativeCloseX": True, "nativeBackdropClick": True,
                       "outsidePoint": outside, "fixtureUnchanged": True})
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        helper = validation / "FileCatCapture.app"
        if helper.is_dir():
            try:
                before = set(validation.glob("native-screen-*.png"))
                subprocess.run(["/usr/bin/open", "-a", str(helper)], check=True,
                               capture_output=True, timeout=15)
                captured = wait_for(lambda: next(iter(set(validation.glob("native-screen-*.png")) - before), None),
                                    "Mac diagnostic screen capture", seconds=10)
                result["diagnosticCapture"] = str(captured)
            except Exception as capture_error:
                result["captureError"] = str(capture_error)
    finally:
        if pid is not None and pid in app_pids():
            process = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                     capture_output=True, text=True, timeout=5)
            if process.stdout.strip().startswith(str(executable)):
                os.kill(pid, signal.SIGTERM)
        shutil.rmtree(fixture)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
