#!/usr/bin/env python3
"""Assert actual muted VoiceOver speech for an isolated FileCat file-list journey.

Usage: python3 mac-voiceover-list.py BUNDLE.app REPORT.json [--actions|--review]
Run in the unlocked Mac desktop after the owner-approved VoiceOver controls.
This runner does not attach a long-lived AX observer while querying VoiceOver.
"""

import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
from uuid import uuid4


def osa(script):
    done = subprocess.run(["/usr/bin/osascript", "-e", script],
                          capture_output=True, text=True, timeout=12)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "macOS accessibility action failed")
    return done.stdout.strip()


def last_phrase():
    return osa('tell application "VoiceOver" to get content of last phrase')


def app_pids():
    done = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"],
                          capture_output=True, text=True, timeout=5)
    return {int(value) for value in done.stdout.split()}


def wait_for(check, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError("The isolated FileCat process did not start")


def screenshot():
    root = Path.home() / "FileCatValidation"
    before = set(root.glob("native-screen-*.png"))
    subprocess.run(["/usr/bin/open", "-a", str(root / "FileCatCapture.app")],
                   capture_output=True, check=True, timeout=15)
    return str(wait_for(lambda: next(iter(set(root.glob("native-screen-*.png")) - before), None)))


def run(bundle, report_path, actions=False, review=False):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or
            not executable.is_file() or report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use an isolated signed FileCat app and a fresh validation receipt")
    if "output muted:true" not in osa("get volume settings"):
        raise AssertionError("Mute Mac output before VoiceOver acceptance")
    last_phrase()
    if app_pids():
        raise AssertionError("An existing FileCat process makes speech ownership ambiguous")

    fixture = validation / "MacVoiceOverList" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n", "c.txt": b"charlie\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    original = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    observer_source = Path(__file__).with_name("mac-ax-focus-observer.swift")
    observer = fixture / "ax-focus-observer"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(observer_source),
                               "-o", str(observer)], capture_output=True, text=True, timeout=120)
    if compiled.returncode:
        raise AssertionError("AX focus probe build failed: " + compiled.stderr[-1200:])
    receipt = {"passed": False, "fixture": str(fixture),
               "appDllSha256": hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest(),
               "phrases": [], "error": None}
    process = None
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance", "--data", str(data),
                        "--left", str(source), "--right", str(target)], check=True,
                       capture_output=True, timeout=15)
        process = wait_for(lambda: next(iter(app_pids()), None))
        command = subprocess.run(["/bin/ps", "-p", str(process), "-o", "command="], check=True,
                                 capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active FileCat process is not the isolated signed build")
        def focused():
            done = subprocess.run([str(observer), "focused", str(process)],
                                  capture_output=True, text=True, timeout=10)
            if done.returncode:
                raise AssertionError("AX focused-element probe failed: " + done.stderr[-800:])
            return json.loads(done.stdout or "{}")

        osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
        time.sleep(1.5)
        for label, key, expected_name in (
                ("first file", 125, "a.txt"),
                ("second file", 125, "b.txt"),
                ("third file", 125, "c.txt"),
                ("previous file", 126, "b.txt")):
            osa(f'tell application "System Events" to key code {key}')
            heard = []
            deadline = time.monotonic() + 3
            while time.monotonic() < deadline:
                phrase = last_phrase()
                if phrase not in heard:
                    heard.append(phrase)
                if expected_name in phrase:
                    break
                time.sleep(0.25)
            matched = any(expected_name in phrase for phrase in heard)
            current = focused()
            receipt["phrases"].append({"step": label, "expected": expected_name,
                                       "heard": heard, "matched": matched, "focused": current})
            if not matched or current.get("title") != expected_name or \
                    "not marked" not in current.get("help", ""):
                raise AssertionError(f"VoiceOver focus or mark state was wrong for {expected_name}")
        # Space is an intentional FileCat mark command; returning to the row must expose
        # its mark state without VoiceOver's ordinary Up/Down movement changing it.
        osa('tell application "System Events" to key code 49')
        time.sleep(0.4)
        osa('tell application "System Events" to key code 126')
        time.sleep(0.4)
        marked = focused()
        receipt["explicitMark"] = {"marked": marked}
        if marked.get("title") != "b.txt" or ", marked," not in marked.get("help", ""):
            raise AssertionError("Explicit Space did not mark b.txt")
        osa('tell application "System Events" to key code 49')
        time.sleep(0.4)
        osa('tell application "System Events" to key code 126')
        time.sleep(0.4)
        unmarked = focused()
        receipt["explicitMark"]["unmarked"] = unmarked
        if unmarked.get("title") != "b.txt" or ", not marked," not in unmarked.get("help", ""):
            raise AssertionError("Explicit Space did not clear the b.txt mark")
        if actions:
            osa('tell application "System Events" to key code 99')  # F3, open the focused item.
            def windows():
                return osa('tell application "System Events" to tell process "FileCat" to get name of every window')
            wait_for(lambda: "b.txt — FileCat Viewer" in windows())
            heard_viewer = []
            for _ in range(12):
                phrase = last_phrase()
                if phrase not in heard_viewer:
                    heard_viewer.append(phrase)
                time.sleep(.25)
            osa('tell application "System Events" to key code 53')  # Escape closes the viewer.
            wait_for(lambda: "FileCat Viewer" not in windows())
            heard_return = []
            for _ in range(12):
                phrase = last_phrase()
                if phrase not in heard_return:
                    heard_return.append(phrase)
                if "b.txt" in phrase:
                    break
                time.sleep(.25)
            returned = focused()
            receipt["fileAction"] = {"viewerSpeech": heard_viewer,
                                     "returnSpeech": heard_return, "returnedFocus": returned}
            if not any("b.txt" in phrase and "FileCat Viewer" in phrase for phrase in heard_viewer):
                raise AssertionError("VoiceOver did not announce the opened b.txt viewer")
            if returned.get("title") != "b.txt" or ", not marked," not in returned.get("help", ""):
                raise AssertionError("Closing the viewer did not restore the unmarked source-row focus")
            if not any("b.txt" in phrase for phrase in heard_return):
                raise AssertionError("VoiceOver did not speak the restored source-row focus")
        if review:
            osa('tell application "System Events" to key code 49')
            time.sleep(.4)
            osa('tell application "System Events" to key code 126')
            if focused().get("title") != "b.txt":
                raise AssertionError("Review setup did not retain the marked source row")
            search_focus = {}
            for attempt in range(3):
                osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
                osa('tell application "System Events" to key code 35 using {control down, shift down}')
                try:
                    wait_for(lambda: focused().get("title") == "Search commands", seconds=3)
                except AssertionError:
                    continue
                time.sleep(.5)  # Let macOS release the synthesized shortcut modifiers.
                setter = subprocess.run(["/opt/homebrew/bin/cliclick", "t:Review marked items"],
                                        capture_output=True, text=True, timeout=10)
                if setter.returncode:
                    raise AssertionError(setter.stderr.strip() or "Could not type command search query")
                search_focus = focused()
                if search_focus.get("value") == "Review marked items":
                    receipt["reviewSearchAttempts"] = attempt + 1
                    break
            if search_focus.get("value") != "Review marked items":
                raise AssertionError(f"Command search did not keep the requested review query: {search_focus!r}")
            time.sleep(.5)
            osa('tell application "System Events" to key code 36')
            time.sleep(.8)
            speech = [last_phrase()]
            current = focused()
            osa('tell application "System Events" to key code 125')
            time.sleep(.4)
            after_down = focused()
            speech.append(last_phrase())
            receipt["reviewAfterEnterScreenshot"] = screenshot()
            receipt["markedReview"] = {"speech": speech, "focused": current,
                                       "afterDown": after_down}
            if current.get("role") != "AXRow" or current.get("title") != "b.txt, file":
                raise AssertionError(f"Marked-item review did not focus the marked row: {current!r}")
            if after_down.get("title") == "Search commands":
                raise AssertionError("Marked-item review returned keyboard focus to command search")
            osa('tell application "System Events" to key code 53')
            time.sleep(.4)
            returned = focused()
            receipt["markedReview"]["returnedFocus"] = returned
            if returned.get("role") != "AXRow" or returned.get("title") not in expected:
                raise AssertionError(f"Closing marked-item review did not restore the source list: {returned!r}")
            if returned.get("title") != "b.txt":
                key = 125 if returned.get("title") == "a.txt" else 126
                osa(f'tell application "System Events" to key code {key}')
                time.sleep(.4)
            still_marked = focused()
            receipt["markedReview"]["markedAfterClose"] = still_marked
            if still_marked.get("title") != "b.txt" or ", marked," not in still_marked.get("help", ""):
                raise AssertionError(f"The reviewed mark changed while the dialog was open: {still_marked!r}")
        if sorted(path.name for path in source.iterdir()) != sorted(expected) or any(target.iterdir()):
            raise AssertionError("The speech journey changed the fixture inventory")
        if any(hashlib.sha256((source / name).read_bytes()).hexdigest() != digest
               for name, digest in original.items()):
            raise AssertionError("The speech journey changed source bytes")
        receipt["passed"] = True
    except Exception as exc:
        receipt["error"] = f"{type(exc).__name__}: {exc}"
        raise
    finally:
        if process is not None and process in app_pids():
            subprocess.run(["/bin/kill", "-TERM", str(process)], timeout=5)
            deadline = time.monotonic() + 10
            while process in app_pids() and time.monotonic() < deadline:
                time.sleep(0.2)
            if process in app_pids():
                subprocess.run(["/bin/kill", "-KILL", str(process)], timeout=5)
        report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] not in ("--actions", "--review")):
        raise SystemExit(__doc__)
    mode = sys.argv[3] if len(sys.argv) == 4 else ""
    run(Path(sys.argv[1]), Path(sys.argv[2]), actions=mode == "--actions", review=mode == "--review")
