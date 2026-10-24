#!/usr/bin/env python3
"""Follow FileCat's macOS accessibility focus while keys move between files and into a dialog.

Usage: python3 mac-ax-focus-speech.py BUNDLE.app REPORT.json [--decision-returns]
Run in the unlocked desktop session against a signed, isolated FileCat bundle under ~/FileCatValidation, with
mac-ax-focus-observer.swift beside this runner. VoiceOver speaks the element named by each
AXFocusedUIElementChanged notification and by the application's AXFocusedUIElement after an activation; this
records both through a read-only AXObserver and checks the spoken attributes of every focused file row and field.
Requires Accessibility access for osascript and the compiled observer.
"""

import hashlib
import json
import re
from pathlib import Path
import subprocess
import sys
import time
from uuid import uuid4


FOREGROUND = '''tell application "System Events" to tell process "FileCat"
    set frontmost to true
end tell'''
KEY_CODES = {"down": "key code 125", "up": "key code 126", "home": "key code 115", "end": "key code 119",
             "tab": "key code 48", "f2": "key code 120", "f7": "key code 98", "f8": "key code 100",
             "escape": "key code 53", "copy": "key code 8 using {command down}",
             "filter": "key code 1 using {command down}", "slash": "key code 44", "a": "key code 0", "return": "key code 36",
             "f3": "key code 99", "c": "key code 8", "h": "key code 4", "r": "key code 15",
             "f5": "key code 96", "select-all": "key code 0 using {command down}", "backspace": "key code 51",
             "palette": "key code 35 using {command down, shift down}", "s": "key code 1", "p": "key code 35",
             "l": "key code 37", "i": "key code 34", "t": "key code 17", "e": "key code 14", "right": "key code 124"}


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


def run(bundle, report_path, decision_returns=False):
    root = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    if bundle.parent != root or bundle.suffix != ".app" or not executable.is_file():
        raise AssertionError("Use a signed, isolated FileCat bundle under FileCatValidation")
    if report_path.parent.resolve() != root:
        raise AssertionError("Write the native report under FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes the native test ambiguous")
    fixture = root / "MacFocusSpeech" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n", "c.txt": b"charlie\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    observer_source = Path(__file__).with_name("mac-ax-focus-observer.swift")
    observer = fixture / "ax-focus-observer"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True, capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(observer_source), "-o", str(observer)],
                              capture_output=True, text=True, timeout=120)
    if compiled.returncode:
        raise AssertionError("AX observer build failed: " + compiled.stderr[-1200:])
    log, stop = fixture / "focus.jsonl", fixture / "observer.stop"
    result = {"passed": False, "platform": "macOS", "fixture": str(fixture), "sourceSha256": hashes,
              "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest(), "steps": []}
    process = watcher = None
    offset = 0

    def records():
        nonlocal offset
        if not log.is_file():
            return []
        with log.open("rb") as stream:
            stream.seek(offset)
            data = stream.read()
        complete, _, _ = data.rpartition(b"\n")
        offset += len(complete) + (1 if complete else 0)
        return [json.loads(line) for line in complete.decode("utf-8").splitlines() if line.strip()]

    def focused():
        done = subprocess.run([str(observer), "focused", str(process)], capture_output=True, text=True, timeout=10)
        return json.loads(done.stdout or "{}")

    def step(label, key, expect, activation=False, keys=None):
        records()
        osa(FOREGROUND)
        for name in ([key] if key else []) + (keys or []):
            osa(f'tell application "System Events" to {KEY_CODES[name]}')
        seen, deadline, current = [], time.monotonic() + 6, {}
        while time.monotonic() < deadline:
            seen += [entry for entry in records() if entry.get("notification") == "AXFocusedUIElementChanged"]
            current = focused()
            if (activation or any(expect.casefold() in spoken(entry).casefold() for entry in seen)) and \
                    expect.casefold() in spoken(current).casefold():
                break
            time.sleep(0.15)
        notified = any(expect.casefold() in spoken(entry).casefold() for entry in seen)
        passed = expect.casefold() in spoken(current).casefold() and (activation or notified)
        result["steps"].append({"step": label, "expect": expect, "notifications": seen, "focused": current,
                                "notified": notified, "passed": passed})
        if not passed:
            raise AssertionError(f"{label}: focus {current!r} after {seen!r}")

    def window_step(label, key, expect):
        """The key must give focus to a window whose title contains the expected text."""
        records()
        osa(FOREGROUND)
        osa(f'tell application "System Events" to {KEY_CODES[key]}')
        seen, deadline = [], time.monotonic() + 8
        while time.monotonic() < deadline and not any(expect in entry.get("title", "") for entry in seen):
            seen += [entry for entry in records() if entry.get("notification") == "AXFocusedWindowChanged"]
            time.sleep(0.15)
        passed = any(expect in entry.get("title", "") for entry in seen)
        result["steps"].append({"step": label, "expect": expect, "windows": seen, "passed": passed})
        if not passed:
            raise AssertionError(f"{label}: focused windows {seen!r}")

    def value_step(label, key, pattern):
        """The key must change the focused element's AXValue, which VoiceOver reads, to text matching the pattern."""
        records()
        osa(FOREGROUND)
        osa(f'tell application "System Events" to {KEY_CODES[key]}')
        seen, deadline = [], time.monotonic() + 6
        while time.monotonic() < deadline and not any(re.fullmatch(pattern, entry.get("value", "")) for entry in seen):
            seen += [entry for entry in records() if entry.get("notification") == "AXValueChanged"]
            time.sleep(0.15)
        current = focused()
        passed = any(re.fullmatch(pattern, entry.get("value", "")) for entry in seen) and re.fullmatch(pattern, current.get("value", "")) is not None
        result["steps"].append({"step": label, "pattern": pattern, "values": seen, "focused": current, "passed": passed})
        if not passed:
            raise AssertionError(f"{label}: value changes {seen!r}, focus {current!r}")
    def announce(label, keys, expect, exact=False, pattern=None):
        """The keys must make FileCat request exactly one announcement containing (or, if exact, equal to) the text."""
        records()
        osa(FOREGROUND)
        for key in keys:
            osa(f'tell application "System Events" to {KEY_CODES[key]}')
        heard, deadline = [], time.monotonic() + 6
        while time.monotonic() < deadline and not any(re.search(pattern, entry.get("announcement", "")) if pattern else expect in entry.get("announcement", "") for entry in heard):
            heard += [entry for entry in records() if entry.get("notification") == "AXAnnouncementRequested"]
            time.sleep(0.15)
        time.sleep(0.6)
        heard += [entry for entry in records() if entry.get("notification") == "AXAnnouncementRequested"]
        def matches(text):
            return re.search(pattern, text) is not None if pattern else text == expect if exact else expect in text
        matching = [entry for entry in heard if matches(entry.get("announcement", ""))]
        result["steps"].append({"step": label, "expect": expect, "announcements": heard, "matching": len(matching),
                                "passed": len(matching) == 1})
        if len(matching) != 1:
            raise AssertionError(f"{label}: announcements {heard!r}")

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
        step("activate the new window", None, "Go to parent location", activation=True)
        if not decision_returns:
            step("down to a.txt", "down", "a.txt")
            step("down to b.txt", "down", "b.txt")
            step("down to c.txt", "down", "c.txt")
            step("up to b.txt", "up", "b.txt")
            step("home to parent", "home", "Go to parent location")
        step("end to c.txt", "end", "c.txt")
        if not decision_returns:
            step("tab to the target panel", "tab", "Go to parent location")
            step("tab back to the source panel", "tab", "c.txt")
        step("F7 opens Create folder", "f7", "folder")
        step("Escape returns to the list", "escape", "c.txt")
        step("F8 opens Delete", "f8", "Delete")
        step("Escape cancels Delete and returns to the same row", "escape", "c.txt")
        step("F2 opens Rename", "f2", "name")
        step("Escape cancels Rename and returns to the same row", "escape", "c.txt")
        # An earlier focus event can precede a queued row update. Inspect the settled
        # AX focus too, so a transient c.txt notification does not count as a return.
        time.sleep(0.5)
        settled = focused()
        result["steps"].append({"step": "Rename cancellation settled focus", "focused": settled,
                                "passed": settled.get("title") == "c.txt"})
        if settled.get("title") != "c.txt":
            raise AssertionError(f"Rename cancellation settled on {settled!r}")
        if decision_returns:
            names = sorted(path.name for path in source.iterdir())
            if names != sorted(expected) or any(target.iterdir()) or any(
                    hashlib.sha256((source / name).read_bytes()).hexdigest() != digest
                    for name, digest in hashes.items()):
                raise AssertionError("Cancel changed the owned fixture")
            result["passed"] = True
            return
        # The first notice after startup, then the same notice again, must each be announced once.
        announce("Command+C announces the copy notice", ["copy"], "Copied 1 item")
        announce("a repeated copy is announced again", ["copy"], "Copied 1 item")
        # A rejected value in a prompt is spoken as its message appears, and again when Enter is refused.
        step("Command+S opens Quick filter", "filter", "Show only the files that match")
        announce("typing an unclosed mask is announced", ["slash"], "Unclosed regular expression in mask.")
        announce("Return on the rejected mask explains it again", ["a", "return"], "Unclosed regular expression in mask.")
        step("Escape closes Quick filter", "escape", "c.txt")
        # A viewer search reports its result count through a live region.
        window_step("F3 opens the viewer", "f3", "FileCat Viewer")
        step("F7 opens the viewer search", "f7", "Find in file")
        # The outcome only, not the searching and counting states before it.
        announce("a viewer search speaks its match count", ["c", "h", "a", "r", "return"], "1 match", exact=True)
        step("Escape returns from the search to the text", "escape", "No selection")
        step("Escape closes the viewer", "escape", "c.txt")
        # A rejected transfer destination is spoken while the form stays open.
        step("F5 opens Copy", "f5", "Destination")
        announce("an empty destination is announced", ["select-all", "backspace"], "Destination: Enter a destination folder.")
        step("Escape cancels Copy", "escape", "c.txt")
        # Keyboard panel resizing speaks the boundary and each new share.
        step("Command+Shift+P opens command search", "palette", "Search commands")
        step("resizing a boundary focuses it", None, "first side's share of panels 1 and 2", keys=["s", "p", "l", "i", "t", "t", "e", "r", "return"])
        value_step("Right changes the share VoiceOver reads", "right", r"5[1-9]")
        step("Escape restores the boundary", "escape", "c.txt")
        osa('tell application "Finder" to activate')
        time.sleep(1.5)
        step("switch away and back", None, "c.txt", activation=True)
        names = sorted(path.name for path in source.iterdir())
        if names != sorted(expected) or any(target.iterdir()):
            raise AssertionError("The focus journey changed the owned fixture folders")
        if any(hashlib.sha256((source / name).read_bytes()).hexdigest() != digest for name, digest in hashes.items()):
            raise AssertionError("A source file changed during the focus journey")
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        raise
    finally:
        stop.write_text("stop")
        if watcher is not None:
            try:
                watcher.wait(10)
            except subprocess.TimeoutExpired:
                watcher.kill()
        if process is not None and process in app_pids():
            subprocess.run(["/bin/kill", "-TERM", str(process)], timeout=5)
            # An open dialog can keep FileCat from shutting down; the next run must not find this process.
            end = time.monotonic() + 10
            while process in app_pids() and time.monotonic() < end:
                time.sleep(0.2)
            if process in app_pids():
                subprocess.run(["/bin/kill", "-KILL", str(process)], timeout=5)
        report_path.write_text(json.dumps(result, indent=2), encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4) or len(sys.argv) == 4 and sys.argv[3] != "--decision-returns":
        raise SystemExit(__doc__)
    run(Path(sys.argv[1]), Path(sys.argv[2]).resolve(), len(sys.argv) == 4)
