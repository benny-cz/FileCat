#!/usr/bin/env python3
"""Assert actual muted VoiceOver speech for an isolated FileCat file-list journey.

Usage: python3 mac-voiceover-list.py BUNDLE.app REPORT.json [--actions|--copy-action|--review|--review-pages]
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


def run(bundle, report_path, actions=False, copy_action=False, review=False, review_pages=False):
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
    if review_pages:
        expected.update({f"item-{index:03}.txt": f"entry {index}\n".encode()
                         for index in range(70)})
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
    dialog_probe = fixture / "ax-dialog-label"
    dialog_report = fixture / "dialog-label.json"
    if copy_action:
        dialog_source = Path(__file__).with_name("mac-ax-dialog-label.swift")
        built = subprocess.run(["/usr/bin/swiftc", "-o", str(dialog_probe), str(dialog_source)],
                               capture_output=True, text=True, timeout=120)
        if built.returncode:
            raise AssertionError("AX dialog probe build failed: " + built.stderr[-1200:])
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

        def copy_review():
            checked = subprocess.run([str(dialog_probe), str(executable), str(process),
                                      str(dialog_report)], capture_output=True, text=True, timeout=15)
            state = json.loads(dialog_report.read_text(encoding="utf-8"))
            if checked.returncode not in (0, 1):
                raise AssertionError("AX Copy review probe failed: " + state.get("error", checked.stderr[-800:]))
            return state

        def prepare_review_search():
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
                    return attempt + 1
            raise AssertionError(f"Command search did not keep the requested review query: {search_focus!r}")

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
        if copy_action:
            osa('tell application "System Events" to key code 96')  # F5, Copy focused b.txt.
            def open_copy_review():
                found = copy_review()
                if (found.get("copyApproval") and found.get("destinationField") and
                        len(found.get("matches", [])) == 1):
                    return found
                return None
            state = wait_for(open_copy_review, seconds=10)
            heard_open = []
            for _ in range(16):
                phrase = last_phrase()
                if phrase not in heard_open:
                    heard_open.append(phrase)
                if "Copy" in phrase or "Destination" in phrase:
                    break
                time.sleep(.25)
            opened_focus = focused()
            voiceover_cursor = osa('tell application "VoiceOver" to get text under cursor of vo cursor')
            osa('tell application "System Events" to key code 48')  # Tab through the review.
            heard_tab = []
            for _ in range(12):
                phrase = last_phrase()
                if phrase not in heard_tab:
                    heard_tab.append(phrase)
                if phrase != heard_open[-1]:
                    break
                time.sleep(.25)
            after_tab = focused()
            osa('tell application "System Events" to key code 53')  # Escape cancels Copy.
            wait_for(lambda: not copy_review().get("matches"), seconds=10)
            heard_return = []
            for _ in range(16):
                phrase = last_phrase()
                if phrase not in heard_return:
                    heard_return.append(phrase)
                if "b.txt" in phrase:
                    break
                time.sleep(.25)
            returned = focused()
            receipt["copyAction"] = {"review": {"copyApproval": state["copyApproval"],
                                                "destinationField": state["destinationField"],
                                                "close": state["matches"][0]},
                                     "openSpeech": heard_open, "openFocus": opened_focus,
                                     "voiceOverCursorAtOpen": voiceover_cursor,
                                     "tabSpeech": heard_tab, "tabFocus": after_tab,
                                     "returnSpeech": heard_return, "returnedFocus": returned}
            if not any("Copy" in phrase or "Destination" in phrase for phrase in heard_open):
                raise AssertionError("VoiceOver did not speak the opened Copy review")
            if returned.get("title") != "b.txt" or ", not marked," not in returned.get("help", ""):
                raise AssertionError("Cancel did not restore the unmarked b.txt source-row focus")
            receipt["copyAction"]["returnedRowSpoken"] = any("b.txt" in phrase for phrase in heard_return)
        if review:
            osa('tell application "System Events" to key code 49')
            time.sleep(.4)
            osa('tell application "System Events" to key code 126')
            if focused().get("title") != "b.txt":
                raise AssertionError("Review setup did not retain the marked source row")
            receipt["reviewSearchAttempts"] = prepare_review_search()
            focus_log, focus_stop = fixture / "review-focus.jsonl", fixture / "review-focus.stop"
            watcher = subprocess.Popen([str(observer), "observe", str(process),
                                        str(focus_log), str(focus_stop)],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
            try:
                wait_for(lambda: focus_log.exists() and "observing" in focus_log.read_text(), seconds=5)
                time.sleep(.2)
                osa('tell application "System Events" to key code 36')
                time.sleep(.8)
            finally:
                focus_stop.touch()
                watcher.wait(timeout=5)
            events = [json.loads(line) for line in focus_log.read_text().splitlines()]
            receipt["reviewFocusEvents"] = [event for event in events
                                            if event.get("notification") == "AXFocusedUIElementChanged"]
            receipt["reviewAnnouncements"] = [event for event in events
                                              if event.get("notification") == "AXAnnouncementRequested"]
            speech = []
            deadline = time.monotonic() + 3
            while time.monotonic() < deadline:
                phrase = last_phrase()
                if phrase not in speech:
                    speech.append(phrase)
                if "b.txt" in phrase:
                    break
                time.sleep(.25)
            current = focused()
            vo_cursor = osa('tell application "VoiceOver" to get text under cursor of vo cursor')
            osa('tell application "System Events" to key code 125')
            time.sleep(.4)
            after_down = focused()
            speech.append(last_phrase())
            receipt["reviewAfterEnterScreenshot"] = screenshot()
            receipt["markedReview"] = {"speech": speech, "focused": current,
                                       "voiceOverCursor": vo_cursor,
                                       "afterDown": after_down}
            if current.get("role") != "AXRow" or current.get("title") != "b.txt, file":
                raise AssertionError(f"Marked-item review did not focus the marked row: {current!r}")
            if not any("b.txt" in phrase for phrase in speech) or "Marked items on this page" not in vo_cursor:
                raise AssertionError("VoiceOver did not announce and enter the marked-review list")
            if not any("Marked items 1" in event.get("announcement", "")
                       for event in receipt["reviewAnnouncements"]):
                raise AssertionError("The marked-review scope was not announced")
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
        if review_pages:
            osa('tell application "System Events" to key code 0 using {control down, shift down}')
            wait_for(lambda: ", marked," in focused().get("help", ""), seconds=10)
            receipt["reviewSearchAttempts"] = prepare_review_search()
            osa('tell application "System Events" to key code 36')
            time.sleep(.8)
            first = focused()
            receipt["pagesOpenFocus"] = first
            receipt["pagesFirstSpeech"] = last_phrase()
            receipt["pagesScreenshot"] = screenshot()
            if first.get("role") != "AXRow" or not first.get("title", "").endswith(", file"):
                raise AssertionError(f"The paged review did not focus a marked item: {first!r}")
            if first["title"].split(",", 1)[0] not in receipt["pagesFirstSpeech"]:
                raise AssertionError("VoiceOver did not speak the first page's focused item")
            found = subprocess.run([str(observer), "find", str(process), "Marked items 1"],
                                   capture_output=True, text=True, check=True, timeout=15)
            if "of 73" not in found.stdout:
                raise AssertionError("The first native review page does not expose all 73 marks")
            visited = []
            for _ in range(8):
                current = focused()
                visited.append(current)
                if current.get("title") == "Next page of marked items":
                    break
                osa('tell application "System Events" to key code 48')
                time.sleep(.25)
            else:
                raise AssertionError(f"Tab did not reach the marked-review Next page button: {visited!r}")
            osa('tell application "System Events" to key code 36')
            time.sleep(.5)
            second = subprocess.run([str(observer), "find", str(process), "Marked items 65"],
                                    capture_output=True, text=True, check=True, timeout=15)
            if "of 73" not in second.stdout:
                raise AssertionError("The second native review page is missing its remaining nine marks")
            second_speech = last_phrase()
            receipt["markedReviewPages"] = {"firstFocus": first, "tabRoute": visited,
                                            "firstScope": found.stdout, "secondScope": second.stdout,
                                            "secondSpeech": second_speech}
            if "Marked items 65" not in second_speech or "of 73" not in second_speech:
                raise AssertionError("VoiceOver did not speak the second page's remaining scope")
            osa('tell application "System Events" to key code 53')
            time.sleep(.4)
            if focused().get("role") != "AXRow":
                raise AssertionError("Closing the paged review did not restore the source list")
            osa('tell application "System Events" to key code 1 using {control down}')
            time.sleep(.5)
            receipt["filterPromptFocus"] = focused()
            if receipt["filterPromptFocus"].get("role") != "AXTextField":
                raise AssertionError(f"Quick filter did not focus its prompt: {receipt['filterPromptFocus']!r}")
            typed = subprocess.run(["/opt/homebrew/bin/cliclick", "t:item-00*"],
                                   capture_output=True, text=True, timeout=10)
            if typed.returncode:
                raise AssertionError(typed.stderr.strip() or "Could not type the Quick filter mask")
            time.sleep(.3)
            receipt["filterDraftFocus"] = focused()
            if receipt["filterDraftFocus"].get("value") != "item-00*":
                raise AssertionError(f"Quick filter draft differed: {receipt['filterDraftFocus']!r}")
            osa('tell application "System Events" to key code 36')
            wait_for(lambda: focused().get("role") == "AXRow", seconds=5)
            receipt["reviewSearchAttemptsAfterFilter"] = prepare_review_search()
            osa('tell application "System Events" to key code 36')
            time.sleep(.8)
            hidden_scope = subprocess.run([str(observer), "find", str(process), "marks are hidden"],
                                          capture_output=True, text=True, check=True, timeout=15)
            hidden_row = subprocess.run([str(observer), "find", str(process), "hidden in file list"],
                                        capture_output=True, text=True, check=True, timeout=15)
            receipt["hiddenReview"] = {"scope": hidden_scope.stdout, "row": hidden_row.stdout,
                                       "speech": last_phrase(), "focus": focused()}
            if "63 marks are hidden" not in hidden_scope.stdout:
                raise AssertionError("The filtered review did not disclose all 63 hidden marks")
            hidden_records = [json.loads(line) for line in hidden_row.stdout.splitlines()]
            if not any(record.get("role") == "AXRow" and "hidden in file list" in record.get("title", "")
                       for record in hidden_records):
                raise AssertionError("The filtered review did not expose a hidden marked row")
            if not any(record.get("title") == "Full details of the reviewed item" and
                       str(source) in record.get("value", "") for record in hidden_records):
                raise AssertionError("The filtered review did not expose the complete original location")
            if "hidden in file list" not in receipt["hiddenReview"]["speech"]:
                raise AssertionError("VoiceOver did not speak the hidden mark state")
            osa('tell application "System Events" to key code 53')
            time.sleep(.4)
            if focused().get("role") != "AXRow":
                raise AssertionError("Closing the filtered review did not restore the source list")
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
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] not in ("--actions", "--copy-action", "--review", "--review-pages")):
        raise SystemExit(__doc__)
    mode = sys.argv[3] if len(sys.argv) == 4 else ""
    run(Path(sys.argv[1]), Path(sys.argv[2]), actions=mode == "--actions",
        copy_action=mode == "--copy-action",
        review=mode == "--review", review_pages=mode == "--review-pages")
