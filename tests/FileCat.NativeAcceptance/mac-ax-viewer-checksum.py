#!/usr/bin/env python3
"""Check real Mac viewer checksum scope, results, and explicit clipboard copying.

Usage: python3 mac-ax-viewer-checksum.py BUNDLE.app REPORT.json
Run in the unlocked, isolated Mac desktop with the matching signed Git build.
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
import zlib


CONTENT = b"\x00ABC\n"
FIXTURE = "native-checksum.bin"
SENTINEL = b"FileCat checksum clipboard sentinel"


def osa(script):
    result = subprocess.run(["/usr/bin/osascript", "-e", script],
                            capture_output=True, text=True, timeout=20)
    if result.returncode:
        raise AssertionError(result.stderr.strip() or "Mac accessibility action failed")
    return result.stdout.strip()


def find_click(role, name, viewer=False):
    target = "true" if viewer else "false"
    return osa(f'''
tell application "System Events" to tell process "FileCat"
    set frontmost to true
    repeat with w in windows
        set isViewer to (name of w as text) contains "FileCat Viewer"
        if isViewer is {target} then
            repeat with e in (entire contents of w)
                try
                    if (role of e as text) is {json.dumps(role)} and (name of e as text) is {json.dumps(name)} then
                        click e
                        return "clicked"
                    end if
                end try
            end repeat
        end if
    end repeat
    error "Missing {role}: {name}"
end tell
''')


def viewer_open():
    return osa('''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "FileCat Viewer" then return "yes"
    end repeat
end tell
return "no"
''') == "yes"


def control_value(name):
    return osa(f'''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "FileCat Viewer" then
            repeat with e in (entire contents of w)
                try
                    if (name of e as text) is {json.dumps(name)} then return value of e as text
                end try
            end repeat
        end if
    end repeat
    error "Missing viewer value: {name}"
end tell
''')


def focused_value():
    return osa('''
tell application "System Events" to tell process "FileCat"
    set frontmost to true
    set focusedElement to value of attribute "AXFocusedUIElement"
    return value of focusedElement as text
end tell
''')


def focused_name():
    return osa('''
tell application "System Events" to tell process "FileCat"
    set frontmost to true
    set focusedElement to value of attribute "AXFocusedUIElement"
    return name of focusedElement as text
end tell
''')


def key(code, shift=False):
    suffix = " using {shift down}" if shift else ""
    osa(f'tell application "System Events" to tell process "FileCat" to key code {code}{suffix}')


def clipboard():
    return subprocess.run(["/usr/bin/pbpaste"], capture_output=True,
                          check=True, timeout=5).stdout


def set_clipboard(value):
    subprocess.run(["/usr/bin/pbcopy"], input=value, check=True, timeout=5)


def until(check, label, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {label}")


def capture_screen():
    root = Path.home() / "FileCatValidation"
    before = set(root.glob("native-screen-*.png"))
    subprocess.run(["/usr/bin/open", "-a", str(root / "FileCatCapture.app")],
                   capture_output=True, check=True, timeout=15)
    return until(lambda: next(iter(set(root.glob("native-screen-*.png")) - before), None),
                 "approved Mac screenshot", 10)


def pointer_click_viewer(relative_x, above_bottom):
    raw = osa('''tell application "System Events" to tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set p to position of w
                set s to size of w
                return (item 1 of p as text) & "|" & (item 2 of p as text) & "|" & ¬
                    (item 1 of s as text) & "|" & (item 2 of s as text)
            end if
        end repeat
        error "Checksum viewer has no native frame"
    end tell''')
    x, y, width, height = [int(value) for value in raw.split("|")]
    if width < 700 or height < 500 or relative_x >= width or above_bottom >= height:
        raise AssertionError(f"Unexpected checksum viewer frame: {raw!r}")
    location = f"c:{x + relative_x},{y + height - above_bottom}"
    subprocess.run(["/opt/homebrew/bin/cliclick", location], check=True,
                   capture_output=True, timeout=5)
    return location


def checksum(scope, content):
    osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
    helper = Path.home() / "FileCatValidation" / "mac-send-key-checksum"
    sent = subprocess.run([str(helper), "checksum"], capture_output=True, text=True, timeout=5)
    if sent.returncode:
        raise AssertionError(sent.stderr.strip() or "Native Ctrl+K input failed")
    set_clipboard(SENTINEL)
    try:
        find_click("AXButton", "Compute", viewer=True)
    except AssertionError:
        pointer_click_viewer(290, 132)
    expected_sha = hashlib.sha256(content).hexdigest()
    expected_crc = f"{zlib.crc32(content):08x}"
    time.sleep(0.5)  # The five-byte local checksum finishes before Copy becomes enabled.
    if clipboard() != SENTINEL:
        raise AssertionError("Computing a checksum replaced the clipboard without Copy results")
    visual = str(capture_screen())
    osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
    pointer_click_viewer(380, 132)
    copied = until(lambda: (value if (value := clipboard()) != SENTINEL else b""),
                   "explicit checksum clipboard copy")
    result = copied.decode("utf-8")
    if (f"SHA-256: {expected_sha}" not in result or f"CRC-32: {expected_crc}" not in result or
            f"{len(content)} of {len(content)} bytes read" not in result or scope not in result):
        raise AssertionError(f"Checksum result differed: {result!r}; desktop: {visual}")
    pointer_click_viewer(500, 132)
    return {"scope": scope, "result": result, "explicitCopyMatched": True, "screenshot": visual}


def run(bundle, report):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or
            not executable.is_file() or report.parent != validation or report.exists()):
        raise AssertionError("Use a signed validation app and fresh report under FileCatValidation")
    if subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True).returncode == 0:
        raise AssertionError("An existing FileCat process makes this test ambiguous")
    original_clipboard = clipboard()
    root = validation / ("MacChecksumScope-" + uuid4().hex)
    source, target, data = (root / part for part in ("source", "destination", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n", FIXTURE: CONTENT}
    for name, value in expected.items():
        (source / name).write_bytes(value)
    digest = {name: hashlib.sha256(value).hexdigest() for name, value in expected.items()}
    receipt = {"passed": False, "fixture": str(root),
               "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest()}
    pid = None
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                        "--data", str(data), "--left", str(source), "--right", str(target)],
                       capture_output=True, check=True, timeout=15)
        def owned_pid():
            found = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True, text=True).stdout.split()
            if len(found) != 1:
                return None
            candidate = int(found[0])
            command = subprocess.run(["/bin/ps", "-p", str(candidate), "-o", "command="],
                                     capture_output=True, text=True, check=True).stdout.strip()
            return candidate if command.startswith(str(executable) + " --new-instance --data " + str(data)) else None
        pid = until(owned_pid, "test-owned FileCat process")
        until(lambda: "FileCat" in osa('tell application "System Events" to tell process "FileCat" to get name of every window'),
              "main window")
        # Native Down/F3 remains usable even when a newly launched Mac window
        # briefly exposes no descendants to System Events' flattened AX query.
        for _ in range(16):
            if focused_name() == FIXTURE:
                break
            key(125)
        else:
            raise AssertionError(f"Could not focus the test-owned source row: {focused_name()!r}")
        key(99)  # F3 opens the focused file in the viewer.
        until(viewer_open, "checksum viewer")
        if "Offset 0 of 5" not in focused_value():
            raise AssertionError("The test-owned binary did not open focused in Hex mode")
        key(123, shift=True)  # Shift+Left at byte zero selects exactly that one byte.
        selection = focused_value()
        if "Selected bytes 0 through 0, 1 bytes" not in selection:
            raise AssertionError(f"One-byte Hex selection was not active: {selection!r}")
        receipt["selectedByte"] = checksum("selected range 0x0–0x0", CONTENT[:1])
        key(124)  # Right clears the selection before reviewing the whole-file scope.
        if "No byte selection" not in focused_value():
            raise AssertionError("Clearing the byte selection did not restore whole-file scope")
        receipt["wholeFile"] = checksum("whole file", CONTENT)
        if {name: hashlib.sha256((source / name).read_bytes()).hexdigest() for name in expected} != digest or list(target.iterdir()):
            raise AssertionError("The checksum journey changed source or destination bytes")
        receipt["sourceBytesUnchanged"] = True
        receipt["passed"] = True
        return receipt
    finally:
        if pid is not None:
            try:
                if viewer_open():
                    osa('''tell application "System Events" to tell process "FileCat"
                        repeat with w in windows
                            if (name of w as text) contains "FileCat Viewer" then
                                repeat with e in UI elements of w
                                    try
                                        if (description of e as text) is "close button" then click e
                                    end try
                                end repeat
                            end if
                        end repeat
                    end tell''')
            except (AssertionError, subprocess.TimeoutExpired):
                pass
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        set_clipboard(original_clipboard)


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    try:
        result = run(Path(sys.argv[1]), report)
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
