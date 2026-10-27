#!/usr/bin/env python3
"""Follow the keyboard from a Markdown page into its Find field and back to the page on macOS.

Usage: python3 mac-ax-page-focus-return.py BUNDLE.app REPORT.json
Run in the unlocked desktop session against a signed, isolated FileCat bundle under ~/FileCatValidation, with
mac-ax-focus-observer.swift beside this runner and no other FileCat running. VoiceOver speaks the element named by
each AXFocusedUIElementChanged notification and by the application's AXFocusedUIElement; this records both through a
read-only AXObserver. Keys come from System Events; requires Accessibility access for osascript and the observer.
Where the keys go is judged from what Command+A and Command+C copy (pbpaste): the page's text while the page has the
keyboard, the Find field's text while Find has it. The general pasteboard is overwritten.

1. F3 on wide.md opens the viewer in Page mode with the page's host ("Web page") as the focused element; the viewer
   narrows to 520 by 520 points.
2. F7 focuses Find; "marker" and Return report the page's match status; Command+A and Command+C copy "marker".
3. Escape returns the keyboard to the page with the viewer still open: the host is the focused element again, and
   Command+A and Command+C copy the page's text.
4. F7 and Escape once more return to the page, and F4 shows the same file as text.
"""

import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
from uuid import uuid4


NAME = "wide.md"
COLUMNS = ["Item", "Owner", "Location", "Size", "Modified", "Kind", "Checksum", "State", "Notes", "Rightmost marker cell"]
CONTENT = ("# Wide table check\n\nThe first marker paragraph says enough words to need several lines in a narrow viewer, "
           "so its lines must wrap inside the window instead of pushing the page sideways.\n\n"
           + "| " + " | ".join(COLUMNS) + " |\n|" + "---|" * len(COLUMNS) + "\n"
           + "| alpha | benny | /srv/data/alpha | 1 MiB | 2026-10-27 | file | 3a7bd3e2360a3d29 | ok | first | edge one |\n"
           + "\nThe closing paragraph has the last marker.\n").encode()
FOREGROUND = '''tell application "System Events" to tell process "FileCat"
    set frontmost to true
end tell'''
KEYS = {"down": "key code 125", "f3": "key code 99", "shift+f3": "key code 99 using {shift down}", "f4": "key code 118",
        "f7": "key code 98", "escape": "key code 53", "return": "key code 36", "marker": 'keystroke "marker"',
        "select-all": "key code 0 using {command down}", "copy": "key code 8 using {command down}"}


def osa(script):
    done = subprocess.run(["/usr/bin/osascript", "-e", script], capture_output=True, text=True, timeout=30)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "macOS accessibility action failed")
    return done.stdout.strip()


def app_pids():
    result = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True, text=True, timeout=5)
    return {int(value) for value in result.stdout.split()}


def wait_for(check, description, seconds=20):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        value = check()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def spoken(record):
    return " ".join(record.get(key, "") for key in ("title", "description", "value", "help"))


def is_host(record):
    """The page's host, which VoiceOver names while the page has the keyboard."""
    return record.get("title") == "Web page" and "Tab moves into the page" in record.get("help", "")


def clipboard():
    return subprocess.run(["/usr/bin/pbpaste"], capture_output=True, text=True, timeout=5).stdout


def run(bundle, report_path):
    root = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    if bundle.parent != root or bundle.suffix != ".app" or not executable.is_file():
        raise AssertionError("Use a signed, isolated FileCat bundle under FileCatValidation")
    if report_path.parent.resolve() != root:
        raise AssertionError("Write the native report under FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes the native test ambiguous")
    fixture = root / "MacPageFocus" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / NAME).write_bytes(CONTENT)
    observer_source = Path(__file__).with_name("mac-ax-focus-observer.swift")
    observer = fixture / "ax-focus-observer"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True, capture_output=True, text=True,
                               timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(observer_source), "-o", str(observer)],
                              capture_output=True, text=True, timeout=180)
    if compiled.returncode:
        raise AssertionError("AX observer build failed: " + compiled.stderr[-1200:])
    log, stop = fixture / "focus.jsonl", fixture / "observer.stop"
    result = {"passed": False, "platform": "macOS", "fixture": str(fixture),
              "sourceSha256": hashlib.sha256(CONTENT).hexdigest(),
              "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest(), "steps": []}
    process = watcher = None
    offset = 0

    def records():
        nonlocal offset
        if not log.is_file():
            return []
        with log.open("rb") as stream:
            stream.seek(offset)
            chunk = stream.read()
        complete, _, _ = chunk.rpartition(b"\n")
        offset += len(complete) + (1 if complete else 0)
        return [json.loads(line) for line in complete.decode("utf-8").splitlines() if line.strip()]

    def focused():
        done = subprocess.run([str(observer), "focused", str(process)], capture_output=True, text=True, timeout=10)
        return json.loads(done.stdout or "{}")

    def step(label, keys, expect, description):
        """After the keys, the focused element (and a focus notification) must satisfy expect."""
        records()
        osa(FOREGROUND)
        for key in keys:
            osa(f'tell application "System Events" to {KEYS[key]}')
            time.sleep(0.25)
        seen, deadline, current = [], time.monotonic() + 8, {}
        while time.monotonic() < deadline:
            seen += [entry for entry in records() if entry.get("notification") == "AXFocusedUIElementChanged"]
            current = focused()
            if expect(current) and any(expect(entry) for entry in seen):
                break
            time.sleep(0.15)
        notified = any(expect(entry) for entry in seen)
        passed = expect(current) and notified
        result["steps"].append({"step": label, "keys": keys, "expect": description, "notifications": seen,
                                "focused": current, "notified": notified, "passed": passed})
        if not passed:
            raise AssertionError(f"{label}: focus {current!r} after {seen!r}")

    def press(keys):
        osa(FOREGROUND)
        for key in keys:
            osa(f'tell application "System Events" to {KEYS[key]}')
            time.sleep(0.3)

    def copied(label, expect, description):
        """Command+A and Command+C must copy text satisfying expect (where the keyboard is)."""
        subprocess.run(["/usr/bin/pbcopy"], input="", text=True, timeout=5)
        press(["select-all", "copy"])
        time.sleep(0.8)
        text = clipboard()
        passed = expect(text)
        result["steps"].append({"step": label, "expect": description, "copied": text[:300], "passed": passed})
        if not passed:
            raise AssertionError(f"{label}: copied {text[:200]!r}")

    def viewer_open():
        return osa('''tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "FileCat Viewer" then return "yes"
    end repeat
end tell
return "no"''') == "yes"

    def viewer_text(text):
        """The spoken attributes of the first element in the app's AX tree whose title, description or value has text."""
        done = subprocess.run([str(observer), "find", str(process), text], capture_output=True, text=True, timeout=60)
        for line in done.stdout.splitlines():
            record = json.loads(line)
            if "visited" not in record:
                return spoken(record).strip() or text
        return ""

    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance", "--data", str(data),
                        "--left", str(source), "--right", str(target)], check=True, capture_output=True, timeout=15)
        process = wait_for(lambda: next(iter(app_pids()), None), "the isolated FileCat process")
        command = subprocess.run(["/bin/ps", "-p", str(process), "-o", "command="], check=True,
                                 capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active FileCat process is not this isolated test build")
        watcher = subprocess.Popen([str(observer), "observe", str(process), str(log), str(stop)])
        wait_for(lambda: any(entry.get("notification") == "observing" for entry in records()), "the AX observer")
        osa(FOREGROUND)
        wait_for(lambda: "Go to parent location" in spoken(focused()), "the focused parent row", seconds=30)
        step("down to wide.md", ["down"], lambda entry: NAME in spoken(entry), "the wide.md row")
        records()
        press(["f3"])
        wait_for(viewer_open, "the viewer")
        wait_for(lambda: viewer_text("Mode: Page"), "Page mode", seconds=15)
        time.sleep(2.0)
        result["afterOpen"] = {"focused": focused(), "notifications": [entry for entry in records()
                                                                    if entry.get("notification") == "AXFocusedUIElementChanged"]}
        if not is_host(result["afterOpen"]["focused"]):
            raise AssertionError(f"With the page shown the focused element was {result['afterOpen']['focused']!r}")
        osa('''tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "FileCat Viewer" then set size of w to {520, 520}
    end repeat
end tell''')
        time.sleep(1.5)
        result["viewerSize"] = osa('''tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "FileCat Viewer" then return size of w
    end repeat
end tell''')
        step("F7 focuses Find", ["f7"], lambda entry: "Find in file" in spoken(entry), "the Find field")
        press(["marker", "return"])
        time.sleep(1.0)
        still = focused()
        result["steps"].append({"step": "marker and Return search the page from Find", "focused": still,
                                "passed": "Find in file" in spoken(still)})
        if "Find in file" not in spoken(still):
            raise AssertionError(f"Typing in Find moved the focus to {still!r}")
        result["matchStatus"] = wait_for(lambda: viewer_text("At least"), "the page's match status", seconds=10)
        copied("Command+A and Command+C in Find copy its text", lambda text: text == "marker", "exactly marker")
        records()
        press(["escape"])
        time.sleep(1.5)
        result["afterEscape"] = {"viewerOpen": viewer_open(), "focused": focused(), "notifications": [
            entry for entry in records() if entry.get("notification") == "AXFocusedUIElementChanged"]}
        if not result["afterEscape"]["viewerOpen"]:
            raise AssertionError("Escape in Find closed the viewer")
        if not is_host(result["afterEscape"]["focused"]):
            raise AssertionError(f"After Escape the focused element was {result['afterEscape']['focused']!r}")
        copied("Escape gives the keyboard back to the page", lambda text: "Wide table check" in text and
               "Rightmost marker cell" in text, "the page's text")
        step("F7 focuses Find again", ["f7"], lambda entry: "Find in file" in spoken(entry), "the Find field")
        press(["escape"])
        time.sleep(1.0)
        copied("Escape returns to the page again", lambda text: "Wide table check" in text, "the page's text")
        step("F4 from the page shows the text", ["f4"], lambda entry: "Text viewer" in spoken(entry), "the text viewer")
        if (source / NAME).read_bytes() != CONTENT or any(target.iterdir()):
            raise AssertionError("The journey changed the owned source or destination")
        result["passed"] = True
    except Exception as error:
        result["error"] = str(error)
    finally:
        stop.touch()
        if watcher is not None:
            try:
                watcher.wait(timeout=5)
            except subprocess.TimeoutExpired:
                watcher.kill()
        if process is not None:
            subprocess.run(["/bin/kill", str(process)], capture_output=True, timeout=5)
        report_path.write_text(json.dumps(result, indent=2), encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    raise SystemExit(run(Path(sys.argv[1]), Path(sys.argv[2]).resolve()))
