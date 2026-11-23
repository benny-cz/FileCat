#!/usr/bin/env python3
"""U1 G-UX: actual muted VoiceOver speech for File -> Inspect -> Permissions and ownership.

Usage: python3 mac-voiceover-permissions.py BUNDLE.app REPORT.json
Run in the unlocked Mac desktop with VoiceOver running under the owner-approved AppleScript control and Mac output
muted, against a signed, isolated FileCat bundle under ~/FileCatValidation, with mac-ax-focus-observer.swift beside
this runner and cliclick installed. Keyboard only: the command search opens the inspector, one Tab cycle must speak the
item, its owner and group and the access entries with their count, and Escape must return to the item. macOS
permission changes are not enabled in this build, so no editor is reachable. The witness keeps its mode and ACL.
"""

import hashlib
import json
from pathlib import Path
import os
import subprocess
import sys
import time
from uuid import uuid4

KEYS = {"down": "key code 125", "tab": "key code 48", "return": "key code 36", "escape": "key code 53",
        "palette": "key code 35 using {control down, shift down}"}


def osa(script):
    done = subprocess.run(["/usr/bin/osascript", "-e", script], capture_output=True, text=True, timeout=12)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "macOS accessibility action failed")
    return done.stdout.strip()


def last_phrase():
    return osa('tell application "VoiceOver" to get content of last phrase')


def app_pids():
    done = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True, text=True, timeout=5)
    return {int(value) for value in done.stdout.split()}


def wait_for(check, description, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def acl_listing(path):
    return subprocess.run(["/bin/ls", "-le", str(path)], check=True, capture_output=True, text=True, timeout=10).stdout


def run(bundle, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file() or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use an isolated signed FileCat app and a fresh validation receipt")
    if "output muted:true" not in osa("get volume settings"):
        raise AssertionError("Mute Mac output before VoiceOver acceptance")
    last_phrase()
    if app_pids():
        raise AssertionError("An existing FileCat process makes speech ownership ambiguous")

    fixture = validation / "MacVoiceOverPermissions" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    witness = source / "acl-witness.txt"
    witness.write_bytes(b"Permissions editor reader fixture\n")
    os.chmod(witness, 0o640)
    # Two extended entries make the access section a list.
    for entry in ("everyone allow read", "everyone deny write"):
        subprocess.run(["/bin/chmod", "+a", entry, str(witness)], check=True, timeout=10)
    before = (os.stat(witness).st_mode & 0o7777, acl_listing(witness))
    observer_source = Path(__file__).with_name("mac-ax-focus-observer.swift")
    observer = fixture / "ax-focus-observer"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True, capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(observer_source), "-o", str(observer)],
                              capture_output=True, text=True, timeout=120)
    if compiled.returncode:
        raise AssertionError("AX focus probe build failed: " + compiled.stderr[-1200:])
    receipt = {"passed": False, "fixture": str(fixture), "witness": str(witness),
               "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest(),
               "steps": [], "error": None}
    process = watcher = None
    log, stop = fixture / "ax-events.jsonl", fixture / "observer.stop"
    offset = 0

    def records():
        """AX notifications the observer logged since the previous call."""
        nonlocal offset
        if not log.is_file():
            return []
        with log.open("rb") as stream:
            stream.seek(offset)
            chunk = stream.read()
        complete, _, _ = chunk.rpartition(b"\n")
        offset += len(complete) + (1 if complete else 0)
        return [json.loads(line) for line in complete.decode("utf-8").splitlines() if line.strip()]

    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance", "--data", str(data),
                        "--left", str(source), "--right", str(target)], check=True, capture_output=True, timeout=15)
        process = wait_for(lambda: next(iter(app_pids()), None), "the isolated FileCat process")
        command = subprocess.run(["/bin/ps", "-p", str(process), "-o", "command="], check=True,
                                 capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active FileCat process is not the isolated signed build")
        # FileCat repeats a dialog's title as an announcement request; VoiceOver can speak it between two reads of its
        # last phrase, so the observer records the requests beside VoiceOver's own speech.
        watcher = subprocess.Popen([str(observer), "observe", str(process), str(log), str(stop)])
        wait_for(lambda: any(entry.get("notification") == "observing" for entry in records()), "the AX observer")

        def focused():
            done = subprocess.run([str(observer), "focused", str(process)], capture_output=True, text=True, timeout=10)
            if done.returncode:
                raise AssertionError("AX focused-element probe failed: " + done.stderr[-800:])
            return json.loads(done.stdout or "{}")

        def front():
            osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')

        def press(name):
            front()
            osa(f'tell application "System Events" to {KEYS[name]}')

        def listen(seconds, until=None):
            heard, deadline = [], time.monotonic() + seconds
            while time.monotonic() < deadline:
                phrase = last_phrase()
                if phrase and (not heard or heard[-1] != phrase):
                    heard.append(phrase)
                if until and until(heard):
                    time.sleep(0.4)
                    phrase = last_phrase()
                    if phrase and heard[-1] != phrase:
                        heard.append(phrase)
                    break
                time.sleep(0.25)
            return heard

        def step(label, keys, *expected, seconds=6):
            for name in keys:
                press(name)
            heard = listen(seconds, lambda phrases: all(any(part in phrase for phrase in phrases) for part in expected))
            current = focused()
            passed = all(any(part in phrase for phrase in heard) for part in expected)
            receipt["steps"].append({"step": label, "expected": list(expected), "heard": heard, "focused": current,
                                     "passed": passed})
            if not passed:
                raise AssertionError(f"{label}: VoiceOver said {heard!r}; focus {current!r}")
            return current

        front()
        # The new window announces itself after its first row has focus; keys sent earlier are lost.
        wait_for(lambda: focused().get("title") == "Go to parent location", "the focused parent row", seconds=30)
        listen(3, lambda phrases: any("has new window" in phrase or "keyboard focus" in phrase for phrase in phrases))
        time.sleep(1.0)
        step("down to the witness", ["down"], "acl-witness.txt", seconds=12)
        # A listing refresh can close the command search; Return then opens the file instead, so it is pressed only
        # while the search field holds the query.
        time.sleep(2.5)
        for attempt in range(3):
            press("palette")
            try:
                wait_for(lambda: focused().get("title") == "Search commands", "command search", seconds=4)
            except AssertionError:
                continue
            time.sleep(0.5)  # Let macOS release the synthesized shortcut modifiers.
            typed = subprocess.run(["/opt/homebrew/bin/cliclick", "t:permissions and ownership"],
                                   capture_output=True, text=True, timeout=15)
            if typed.returncode:
                raise AssertionError(typed.stderr.strip() or "Could not type the command search query")
            time.sleep(0.8)
            search = focused()
            if search.get("title") == "Search commands" and search.get("value") == "permissions and ownership":
                receipt["paletteAttempts"] = attempt + 1
                break
            press("escape")
            time.sleep(0.8)
        else:
            raise AssertionError("The command search did not keep the permissions query")
        records()
        press("return")
        heard = listen(6, lambda phrases: any("Permissions and ownership" in phrase for phrase in phrases))
        announced = [entry.get("announcement", "") for entry in records()
                     if entry.get("notification") == "AXAnnouncementRequested"]
        current = focused()
        passed = any("Permissions and ownership" in text for text in heard + announced)
        receipt["steps"].append({"step": "Return opens the inspector", "heard": heard, "announced": announced,
                                 "focused": current, "titleSpoken": any("Permissions and ownership" in p for p in heard),
                                 "passed": passed})
        if not passed:
            raise AssertionError(f"Return opens the inspector: VoiceOver said {heard!r}; announced {announced!r}")

        # One Tab cycle through the inspector's focusable controls must speak the item, its owner and group and the
        # access entries; the values themselves are plain text the keyboard never reaches. VoiceOver finishes a long
        # hint before it speaks the next control, so each Tab waits for the newly focused control's own speech and
        # then for its help, which VoiceOver reads after it.
        def name_of(record):
            return record.get("title") or record.get("description") or ""

        cycle = []
        for _ in range(16):
            press("tab")
            current = focused()
            name = name_of(current)
            heard = listen(7, lambda phrases: any(name and name in phrase for phrase in phrases))
            help_text = current.get("help", "")
            if help_text and not any(help_text in phrase for phrase in heard):
                heard += [phrase for phrase in listen(4, lambda phrases: any(help_text in p for p in phrases))
                          if phrase not in heard]
            try:
                cursor = osa('tell application "VoiceOver" to get text under cursor of vo cursor')
            except AssertionError as error:
                cursor = f"unavailable: {error}"
            cycle.append({"heard": heard, "focused": current, "voiceOverCursor": cursor})
            if len(cycle) > 3 and name == "Close":
                break
        receipt["tabCycle"] = cycle

        def entry_for(name):
            return next((entry for entry in cycle if name_of(entry["focused"]) == name), None)

        missing = []
        for name, value in (("Copy exact path", str(witness)), ("Copy exact owner ID", str(os.getuid())),
                            ("Copy exact primary group ID", str(os.getgid()))):
            entry = entry_for(name)
            if entry is None or entry["focused"].get("help") != value:
                missing.append(f"{name}: help {value}")
            elif not any(value in phrase.replace(name, "") for phrase in entry["heard"]):
                missing.append(f"{name}: spoken {value}")
        row = next((entry for entry in cycle if entry["focused"].get("role") == "AXRow"), None)
        if row is None or not any(" of 2" in phrase or " 2 items" in phrase for phrase in row["heard"]):
            missing.append("Access now row with its count of two entries")
        # macOS permission changes are not enabled in this build: the inspector offers no change action.
        offered = [name_of(entry["focused"]) for entry in cycle
                   if name_of(entry["focused"]).startswith(("Edit ", "Change "))]
        receipt["offeredChanges"] = offered
        receipt["tabCycleMissing"] = missing
        if missing:
            raise AssertionError(f"The Tab cycle never spoke {missing}")
        step("Escape closes the inspector on the witness row", ["escape"], "acl-witness.txt", seconds=8)
        after = (os.stat(witness).st_mode & 0o7777, acl_listing(witness))
        receipt["witnessBefore"], receipt["witnessAfter"] = [oct(before[0]), before[1]], [oct(after[0]), after[1]]
        if after != before:
            raise AssertionError("The witness mode or ACL changed")
        receipt["passed"] = True
    except Exception as exc:
        receipt["error"] = f"{type(exc).__name__}: {exc}"
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
            end = time.monotonic() + 10
            while process in app_pids() and time.monotonic() < end:
                time.sleep(0.2)
            if process in app_pids():
                subprocess.run(["/bin/kill", "-KILL", str(process)], timeout=5)
        report_path.write_text(json.dumps(receipt, indent=2), encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    run(Path(sys.argv[1]), Path(sys.argv[2]))
