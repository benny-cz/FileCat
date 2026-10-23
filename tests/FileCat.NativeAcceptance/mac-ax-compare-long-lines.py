#!/usr/bin/env python3
"""Exercise long-line comparison, next difference, and source-faithful Copy on Mac.

Usage: python3 mac-ax-compare-long-lines.py BUNDLE.app REPORT.json [--narrow|--narrow-wrap]
Run in the unlocked isolated Mac desktop with the matching signed Git build.
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


def osa(script):
    result = subprocess.run(["/usr/bin/osascript", "-e", script],
                            capture_output=True, text=True, timeout=20)
    if result.returncode:
        raise AssertionError(result.stderr.strip() or "Mac accessibility action failed")
    return result.stdout.strip()


def until(check, label, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(.2)
    raise AssertionError(f"Timed out waiting for {label}")


def focused_name():
    return osa('''tell application "System Events" to tell process "FileCat"
        set frontmost to true
        return name of value of attribute "AXFocusedUIElement"
    end tell''')


def compare_open():
    return "Compare: compare.txt" in osa('tell application "System Events" to tell process "FileCat" to get name of every window')


def compare_click(relative_x, relative_y):
    frame = osa('''tell application "System Events" to tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "Compare: compare.txt" then
                set p to position of w
                set s to size of w
                return (item 1 of p as text) & "|" & (item 2 of p as text) & "|" & ¬
                    (item 1 of s as text) & "|" & (item 2 of s as text)
            end if
        end repeat
        error "The compare window has no native frame"
    end tell''')
    x, y, width, height = [int(value) for value in frame.split("|")]
    if width < 620 or height < 500 or relative_x >= width or relative_y >= height:
        raise AssertionError(f"Unexpected comparison frame: {frame!r}")
    subprocess.run(["/opt/homebrew/bin/cliclick", f"c:{x + relative_x},{y + relative_y}"],
                   capture_output=True, check=True, timeout=5)
    return [width, height]


def clipboard():
    return subprocess.run(["/usr/bin/pbpaste"], capture_output=True,
                          check=True, timeout=5).stdout


def set_clipboard(value):
    subprocess.run(["/usr/bin/pbcopy"], input=value, check=True, timeout=5)


def screenshot():
    root = Path.home() / "FileCatValidation"
    before = set(root.glob("native-screen-*.png"))
    subprocess.run(["/usr/bin/open", "-a", str(root / "FileCatCapture.app")],
                   capture_output=True, check=True, timeout=15)
    return str(until(lambda: next(iter(set(root.glob("native-screen-*.png")) - before), None),
                     "approved Mac screenshot", 10))


def run(bundle, report, narrow=False, wrap=False):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or
            not executable.is_file() or report.parent != validation or report.exists()):
        raise AssertionError("Use a signed validation app and fresh report under FileCatValidation")
    if subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True).returncode == 0:
        raise AssertionError("An existing FileCat process makes this test ambiguous")
    original_clipboard = clipboard()
    root = validation / ("MacCompareLong-" + uuid4().hex)
    left, right, data = (root / name for name in ("left", "right", "data"))
    for folder in (left, right, data):
        folder.mkdir(parents=True)
    prefix = "x" * 2000
    left_lines = ["first " + prefix + "LEFT", "equal middle", "second " + prefix + "L"]
    right_lines = ["first " + prefix + "RIGHT", "equal middle", "second " + prefix + "R"]
    sources = [(left / "compare.txt", ("\n".join(left_lines) + "\n").encode()),
               (right / "compare.txt", ("\n".join(right_lines) + "\n").encode())]
    for path, content in sources:
        path.write_bytes(content)
    digests = [hashlib.sha256(content).hexdigest() for _, content in sources]
    receipt = {"passed": False, "fixture": str(root),
               "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest()}
    pid = None
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                        "--data", str(data), "--left", str(left), "--right", str(right)],
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
        for _ in range(25):
            try:
                name = focused_name()
            except AssertionError:
                time.sleep(.2)
                continue
            if name == "compare.txt":
                break
            osa('tell application "System Events" to tell process "FileCat" to key code 125')
        else:
            raise AssertionError(f"Could not focus the test-owned file: {focused_name()!r}")
        shortcut = subprocess.run([str(validation / "mac-send-key-compare"), "compare"],
                                  capture_output=True, text=True, timeout=5)
        if shortcut.returncode:
            raise AssertionError(shortcut.stderr.strip() or "Native Ctrl+I input failed")
        until(compare_open, "comparison window")
        if narrow:
            frame = osa('''tell application "System Events" to tell process "FileCat"
                repeat with w in windows
                    if (name of w as text) contains "Compare: compare.txt" then
                        set size of w to {640, 600}
                        set s to size of w
                        return (item 1 of s as text) & "|" & (item 2 of s as text)
                    end if
                end repeat
            end tell''')
            width, height = [int(value) for value in frame.split("|")]
            if not 620 <= width <= 660 or not 580 <= height <= 620:
                raise AssertionError(f"Narrow comparison size was not applied: {frame!r}")
            receipt["requestedCompareSize"] = [width, height]
        if wrap:
            compare_click(190, 76)  # Wrap long lines in the narrow toolbar.
            receipt["wrapRequested"] = True
        time.sleep(.5)
        receipt["firstDifferenceScreenshot"] = screenshot()
        set_clipboard(b"FileCat compare first-line sentinel")
        copy_line_y = 135 if narrow else 110  # The toolbar wraps one extra row at 640.
        receipt["compareSize"] = compare_click(290, copy_line_y)
        first = until(lambda: (value if (value := clipboard()) != b"FileCat compare first-line sentinel" else b""),
                      "first complete source line")
        if first.decode() != left_lines[0]:
            raise AssertionError(f"First copied source line differed: {first[:80]!r}, {len(first)} bytes")
        set_clipboard(b"FileCat compare second-line sentinel")
        osa('tell application "System Events" to tell process "FileCat" to key code 100')  # F8, Next difference.
        time.sleep(.4)
        if clipboard() != b"FileCat compare second-line sentinel":
            raise AssertionError("Navigating to the next difference replaced the clipboard")
        compare_click(290, copy_line_y)
        second = until(lambda: (value if (value := clipboard()) != b"FileCat compare second-line sentinel" else b""),
                       "second complete source line")
        if second.decode() != left_lines[2]:
            raise AssertionError(f"Next difference copied the wrong line: {second[:80]!r}, {len(second)} bytes")
        receipt["nextDifferenceScreenshot"] = screenshot()
        if [hashlib.sha256(path.read_bytes()).hexdigest() for path, _ in sources] != digests:
            raise AssertionError("Comparison changed a source file")
        receipt["copiedLineLengths"] = [len(first.decode()), len(second.decode())]
        receipt["sourceBytesUnchanged"] = True
        receipt["passed"] = True
        return receipt
    finally:
        if pid is not None:
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        set_clipboard(original_clipboard)


def main():
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] not in ("--narrow", "--narrow-wrap")):
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    try:
        mode = sys.argv[3] if len(sys.argv) == 4 else ""
        result = run(Path(sys.argv[1]), report, narrow=bool(mode), wrap=mode == "--narrow-wrap")
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
