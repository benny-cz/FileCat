#!/usr/bin/env python3
"""Capture the live macOS reduced-motion route on an isolated signed Git app.

Usage: mac-ax-theme-motion.py BUNDLE.app REPORT.json
The approved FileCatCapture helper, an unlocked desktop and the native System
Settings > Accessibility > Motion page are required. The test toggles that
actual checkbox and restores its prior state after a normal failure.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
from uuid import uuid4


HEAD = "799e8918"
DLL_SHA256 = "4134da26bd8c950d4976c010192a1fa93009ac955e18a3dda5015f71394bf128"


def run_command(args, timeout=25, check=True):
    result = subprocess.run(args, text=True, capture_output=True, timeout=timeout)
    if check and result.returncode:
        raise AssertionError(f"{args[0]} failed: {result.stderr[-500:]}")
    return result


def wait_for(callback, purpose, seconds=25):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = callback()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {purpose}")


def app_pids():
    done = run_command(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                       check=False)
    return {int(item) for item in done.stdout.split()}


def native_preference(helper):
    value = run_command([str(helper)]).stdout.strip()
    if value not in ("true", "false"):
        raise AssertionError(f"AppKit returned an invalid motion preference: {value!r}")
    return value == "true"


def ui_preference():
    dismiss_system_later()
    script = Path(__file__).with_name("mac-motion-toggle.applescript")
    value = run_command(["/usr/bin/osascript", str(script), "read"]).stdout.strip()
    if value not in ("0", "1"):
        raise AssertionError(f"System Settings returned an invalid motion switch: {value!r}")
    return value == "1"


def set_preference(desired):
    current = ui_preference()
    if current != desired:
        script = Path(__file__).with_name("mac-motion-toggle.applescript")
        value = run_command(["/usr/bin/osascript", str(script), "1" if current else "0"]).stdout.strip()
        if value != ("1" if desired else "0"):
            raise AssertionError("The native Reduce motion checkbox refused the requested value")
    if ui_preference() != desired:
        raise AssertionError("The native Reduce motion checkbox did not retain its value")


def foreground_and_position():
    script = '''tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        delay 0.3
        set w to first window whose name contains "FileCat"
        set position of w to {100, 75}
        set size of w to {1000, 660}
        delay 0.3
        set AppleScript's text item delimiters to ","
        return (position of w as text) & "|" & (size of w as text)
    end tell
end tell'''
    geometry = run_command(["/usr/bin/osascript", "-e", script]).stdout.strip()
    coordinates = [int(value) for value in re.findall(r"\d+", geometry)]
    if (len(coordinates) != 4 or not (0 <= coordinates[0] <= 200) or
            not (0 <= coordinates[1] <= 150) or
            any(abs(actual - expected) > 2 for actual, expected in
                zip(coordinates[2:], (1000, 660)))):
        raise AssertionError(f"FileCat refused the bounded test geometry: {geometry!r}")
    front = run_command(["/usr/bin/osascript", "-e",
                         'tell application "System Events" to get name of first application process whose frontmost is true']).stdout.strip()
    if front != "FileCat":
        raise AssertionError(f"The owned window was not frontmost: {front!r}")
    return coordinates


def dismiss_system_later():
    # The lab account occasionally shows a FaceTime/iCloud reauthentication
    # alert over the app. The owner approved its explicit Later action.
    script = '''tell application "System Events"
    if not (exists process "UserNotificationCenter") then return "absent"
    tell process "UserNotificationCenter"
        repeat with win in windows
            if exists button "Later" of win then
                click button "Later" of win
                return "dismissed"
            end if
        end repeat
    end tell
end tell
return "absent"'''
    result = run_command(["/usr/bin/osascript", "-e", script]).stdout.strip()
    if result not in ("absent", "dismissed"):
        raise AssertionError(f"Unexpected system-alert result: {result!r}")
    return result


def capture(helper, validation, report, phase, index):
    dismiss_system_later()
    before = set(validation.glob("native-screen-*.png"))
    run_command(["/usr/bin/open", "-a", str(helper)], timeout=30)
    generated = wait_for(lambda: next((path for path in
        sorted(set(validation.glob("native-screen-*.png")) - before,
               key=lambda path: path.stat().st_mtime_ns, reverse=True)
        if path.stat().st_size > 100_000), None), "approved Mac capture")
    target = report.with_name(f"{report.stem}.{phase}-{index}.png")
    if target.exists():
        raise AssertionError(f"Capture already exists: {target}")
    generated.rename(target)
    if dismiss_system_later() == "dismissed":
        raise AssertionError("A system alert appeared during the FileCat capture")
    return str(target)


def run(bundle, report):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    report = report.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    capture_helper = validation / "FileCatCapture.app"
    if (bundle != validation / "FileCatValidation-799e8918.app" or
            report != validation / "ac-s2-013-mac-motion-799e8918-r1.json" or
            report.exists() or not executable.is_file() or
            not (capture_helper / "Contents/MacOS/FileCatCapture").is_file() or
            app_pids()):
        raise AssertionError("Use the exact signed Git bundle, fresh guest receipt and no other FileCat process")
    digest = hashlib.sha256(executable.with_name("FileCat.dll").read_bytes()).hexdigest()
    if digest != DLL_SHA256:
        raise AssertionError("The signed Mac app DLL does not match the verified Git build")
    fixture = validation / "ThemeMotion" / uuid4().hex
    source, target, data = (fixture / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    source_file = source / "visible.txt"
    content = b"VISIBLE CONTENT STAYS FIXED\n"
    source_file.write_bytes(content)
    (source / ".hidden.txt").write_text("hidden but actionable\n", encoding="utf-8")
    (data / "settings.json").write_text(json.dumps({"SchemaVersion": 1,
        "Theme": "Psychedelic", "ThemeAnimations": True}), encoding="utf-8")
    helper = fixture / "mac-motion-preference"
    run_command(["/usr/bin/swiftc", "-framework", "AppKit", "-o", str(helper),
                 str(Path(__file__).with_name("mac-motion-preference.swift"))], timeout=60)
    original_native = native_preference(helper)
    original_ui = ui_preference()
    if original_native != original_ui:
        raise AssertionError("AppKit and the native Motion checkbox disagree before launch")
    result = {"passed": False, "head": HEAD, "appDllSha256": digest,
              "originalNativeReducedMotion": original_native,
              "originalUiReducedMotion": original_ui,
              "phases": [], "fixture": str(fixture)}
    pid = None
    try:
        set_preference(False)
        if native_preference(helper):
            raise AssertionError("AppKit did not accept the initial motion-enabled state")
        before = app_pids()
        run_command(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                     "--data", str(data), "--left", str(source), "--right", str(target)])
        pid = wait_for(lambda: next(iter(app_pids() - before), None), "owned Mac FileCat process")
        command = run_command(["/bin/ps", "-p", str(pid), "-o", "command="]).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The exact signed bundle did not own the FileCat window")
        time.sleep(3)
        result["systemAlertsDismissed"] = []
        for phase, reduced in (("moving", False), ("reduced", True), ("resumed", False)):
            set_preference(reduced)
            time.sleep(6)  # FileCat's live poll is three seconds.
            actual = native_preference(helper)
            if actual != reduced:
                raise AssertionError(f"AppKit did not retain {phase} motion preference")
            if pid not in app_pids():
                raise AssertionError(f"FileCat exited before {phase} captures")
            geometry = foreground_and_position()
            result["systemAlertsDismissed"].append(dismiss_system_later())
            foreground_and_position()
            first = capture(capture_helper, validation, report, phase, 1)
            time.sleep(4)
            foreground_and_position()
            result["systemAlertsDismissed"].append(dismiss_system_later())
            foreground_and_position()
            second = capture(capture_helper, validation, report, phase, 2)
            result["phases"].append({"name": phase, "nativeReducedMotion": actual,
                                     "appPid": pid, "geometry": geometry,
                                     "captures": [first, second]})
        result["sourceUnchanged"] = source_file.read_bytes() == content
        result["targetEmpty"] = not any(target.iterdir())
        if not result["sourceUnchanged"] or not result["targetEmpty"]:
            raise AssertionError("The native motion check modified its fixture")
        result["passed"] = True
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        if pid in app_pids():
            os.kill(pid, signal.SIGTERM)
            wait_for(lambda: pid not in app_pids(), "owned FileCat exit", seconds=12)
        try:
            set_preference(original_ui)
            time.sleep(2)
            result["restoredNativeReducedMotion"] = native_preference(helper)
            result["restoredUiReducedMotion"] = ui_preference()
            if (result["restoredNativeReducedMotion"] != original_native or
                    result["restoredUiReducedMotion"] != original_ui):
                raise AssertionError("The original macOS motion setting was not restored")
        except Exception as error:
            result["passed"] = False
            result["error"] = f"Restore failed: {type(error).__name__}: {error}"
        # Source and target are test-owned, but retain captures and receipt.
        import shutil
        shutil.rmtree(fixture)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    outcome = run(Path(sys.argv[1]), Path(sys.argv[2]))
    print(json.dumps(outcome), flush=True)
    raise SystemExit(0 if outcome["passed"] else 1)
