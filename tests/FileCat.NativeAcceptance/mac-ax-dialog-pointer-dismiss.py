#!/usr/bin/env python3
"""Check native Mac overlay cancellation and termination in an isolated app.

Usage: python3 mac-ax-dialog-pointer-dismiss.py BUNDLE.app REPORT.json [--sigterm|--copy|--move|--delete]
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


def open_review(kind):
    key_code = {"Copy": 96, "Move": 97, "Delete": 100}[kind]
    def attempt():
        return osa(f'''
tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 126
    key code 125
    delay 0.1
    set sourceRow to value of attribute "AXFocusedUIElement"
    if (role of sourceRow as text) is not "AXRow" or (name of sourceRow as text) is not "b.txt" then return "waiting"
    key code {key_code}
    return "opened"
end tell
''')
    wait_for(lambda: attempt() == "opened", f"the loaded {kind} fixture controls", seconds=20)


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
    modes = {"--copy": "Copy", "--move": "Move", "--delete": "Delete"}
    if len(sys.argv) not in (3, 4) or len(sys.argv) == 4 and sys.argv[3] not in ("--sigterm", *modes):
        raise SystemExit("Usage: mac-ax-dialog-pointer-dismiss.py BUNDLE.app REPORT.json [--sigterm|--copy|--move|--delete]")
    mode = sys.argv[3] if len(sys.argv) == 4 else ""
    sigterm = mode == "--sigterm"
    review = modes.get(mode)
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
    if review:
        (source / "b.txt").write_bytes(b"beta\n")
    ax_source = Path(__file__).with_name("mac-ax-dialog-label.swift")
    if not ax_source.is_file():
        raise SystemExit("The native AX dialog-label source is missing beside this runner")
    ax_helper = fixture / "mac-ax-dialog-label"
    subprocess.run(["/usr/bin/swiftc", "-o", str(ax_helper), str(ax_source)],
                   check=True, capture_output=True, timeout=120)
    ax_report = fixture / "dialog-label.json"
    digest = hashlib.sha256(executable.read_bytes()).hexdigest()
    result = {"passed": False, "bundle": str(bundle), "appSha256": digest,
              "input": "System Events and SIGTERM" if sigterm else "cliclick-CGEvent-mouse",
              "dialog": review or "Create folder"}
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
        opener = (lambda: open_review(review)) if review else open_folder
        opener()
        (x, y, width, height), first_match = wait_for(
            lambda: close_bounds(ax_helper, executable, pid, ax_report), "the dialog close button")
        result["closeBounds"] = [x, y, width, height]
        result["closeAccessibility"] = first_match
        if review:
            review_state = json.loads(ax_report.read_text(encoding="utf-8"))
            approval = review_state.get(review.lower() + "Approval")
            if not approval or (review != "Delete" and not review_state.get("destinationField")):
                raise AssertionError(f"The open overlay is not an actionable {review} review")
            result["reviewControls"] = True
        if width < 25 or height < 25:
            raise AssertionError("The close control has an undersized Mac pointer target")
        if sigterm:
            osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
            osa('tell application "System Events" to keystroke "unsubmitted"')
            draft = osa('''
tell application "System Events" to tell process "FileCat"
    set focusedElement to value of attribute "AXFocusedUIElement"
    if (role of focusedElement as text) is not "AXTextField" then
        error "Create folder draft field is not focused"
    end if
    return value of focusedElement as text
end tell
''')
            if draft != "unsubmitted":
                raise AssertionError(f"The Create folder draft was not entered: {draft!r}")
            if not close_bounds(ax_helper, executable, pid, ax_report):
                raise AssertionError("The draft dialog closed before SIGTERM")
            os.kill(pid, signal.SIGTERM)
            wait_for(lambda: pid not in app_pids(), "open-dialog SIGTERM exit", seconds=10)
            result.update({"openDialogSigtermExited": True, "draftName": draft})
        else:
            click(round(x + width / 2), round(y + height / 2))
            wait_for(lambda: close_bounds(ax_helper, executable, pid, ax_report) is None,
                     "close-X cancellation")
            opener()
            wait_for(lambda: close_bounds(ax_helper, executable, pid, ax_report),
                     "the reopened dialog close button")
            wx, wy, ww, wh = window_bounds()
            # VoiceOver's floating caption can cover the lower-left corner of
            # the window. Press the visible left pane above it, outside the
            # centered modal, so this remains a real FileCat backdrop click.
            outside = [wx + min(80, ww // 8), wy + min(250, wh // 3)]
            click(*outside)
            wait_for(lambda: close_bounds(ax_helper, executable, pid, ax_report) is None,
                     "backdrop cancellation")
            result.update({"nativeCloseX": True, "nativeBackdropClick": True,
                           "outsidePoint": outside})
        source_entries = list(source.iterdir())
        source_files = {item.name: item.read_bytes() for item in source_entries if item.is_file()}
        if len(source_entries) != len(source_files) or source_files != ({"b.txt": b"beta\n"} if review else {}) or list(target.iterdir()):
            raise AssertionError("A canceled Mac dialog changed the fixture")
        result.update({"passed": True, "fixtureUnchanged": True})
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
                try:
                    os.kill(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                deadline = time.monotonic() + 5
                while pid in app_pids() and time.monotonic() < deadline:
                    time.sleep(0.1)
                if pid in app_pids():
                    try:
                        os.kill(pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
        result["ownedProcessStopped"] = pid is None or pid not in app_pids()
        if not result["ownedProcessStopped"]:
            result["passed"] = False
        shutil.rmtree(fixture)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
