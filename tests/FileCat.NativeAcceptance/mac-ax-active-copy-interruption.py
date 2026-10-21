#!/usr/bin/env python3
"""Exercise a real, process-interrupted Copy in an isolated Mac FileCat app.

Usage: mac-ax-active-copy-interruption.py PHASE mac-active-copy-<8hex>.json REPORT.json
Phases: seed, inspect-window, open-form, inspect-form, prepare, inspect-interruption,
inspect-recovery, review, cancel-review, continue-review, inspect-completion,
stop-completed. Restart the exact app/profile between prepare and inspect-recovery.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shlex
import signal
import subprocess
import sys
import time
from uuid import uuid4


_move = runpy.run_path(str(Path(__file__).with_name("mac-ax-clipboard-interrupted-move.py")))
ax_find = _move["ax_find"]
osa = _move["clipboard"]["osa"]
wait_for = _move["wait_for"]
ROOT = (Path.home() / "FileCatValidation").resolve(strict=True)
bundle = os.environ.get("FILECAT_NATIVE_MAC_BUNDLE", "FileCatValidation.app")
if not re.fullmatch(r"FileCatValidation(?:-[0-9a-f]{8})?\.app", bundle):
    raise SystemExit("Use a signed FileCatValidation bundle under FileCatValidation")
APP = ROOT / bundle / "Contents" / "MacOS" / "FileCat"
PAYLOAD = (b"FileCat active Mac Copy acceptance payload\n" * 120_000)[: 4 << 20]
PAYLOAD_HASH = hashlib.sha256(PAYLOAD).hexdigest()


def checked_manifest(path):
    if path.parent.resolve(strict=True) != ROOT or not re.fullmatch(
            r"mac-active-copy-[0-9a-f]{8}\.json", path.name):
        raise AssertionError("Use a build-specific Mac active-Copy manifest in FileCatValidation")
    return path


def checked_record(path):
    record = json.loads(checked_manifest(path).read_text(encoding="utf-8"))
    source = Path(record["source"]).resolve(strict=True)
    destination = Path(record["destination"]).resolve(strict=True)
    data = Path(record["data"]).resolve(strict=True)
    fixture = source.parent
    if (fixture.parent != ROOT / "MacActiveCopy" or
            not re.fullmatch(r"[0-9a-f]{32}", fixture.name) or
            source != fixture / "source" or destination != fixture / "destination" or
            data != fixture / "data"):
        raise AssertionError("The active-Copy paths left their isolated fixture")
    if Path(record["app"]).resolve(strict=True) != APP.resolve(strict=True):
        raise AssertionError("The validation executable changed")
    if record["size"] != len(PAYLOAD) or record["sha256"] != PAYLOAD_HASH:
        raise AssertionError("The fixture payload identity changed")
    if hashlib.sha256((source / "payload.bin").read_bytes()).hexdigest() != PAYLOAD_HASH:
        raise AssertionError("The Copy source bytes changed")
    return record


def seed(path):
    checked_manifest(path)
    if path.exists() or not APP.is_file():
        raise AssertionError("The manifest already exists or the signed app is absent")
    fixture = ROOT / "MacActiveCopy" / uuid4().hex
    for name in ("source", "destination", "data"):
        (fixture / name).mkdir(parents=True, exist_ok=False)
    (fixture / "source" / "payload.bin").write_bytes(PAYLOAD)
    record = {"app": str(APP), "source": str(fixture / "source"),
              "destination": str(fixture / "destination"), "data": str(fixture / "data"),
              "size": len(PAYLOAD), "sha256": PAYLOAD_HASH}
    path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "phase": "seed", **record}


def exact_pid(record):
    expected = [record["app"], "--new-instance", "--data", record["data"],
                "--left", record["source"], "--right", record["destination"]]
    matches = []
    result = subprocess.run(["ps", "-axo", "pid=,command="], capture_output=True,
                            text=True, check=True, timeout=15)
    for line in result.stdout.splitlines():
        try:
            pid, command = line.strip().split(None, 1)
            if shlex.split(command) == expected:
                matches.append(int(pid))
        except (ValueError, IndexError):
            continue
    if len(matches) != 1:
        raise AssertionError(f"Expected one exact validation Copy process; found {len(matches)}")
    return matches[0]


def ax_controls():
    return osa('''
on describe(itemRef, depth)
    if depth > 28 then return ""
    set found to ""
    tell application "System Events"
        try
            set r to role of itemRef as text
            set n to name of itemRef as text
            if r is "AXButton" or r is "AXTextField" or r is "AXCheckBox" or r is "AXRow" then
                set found to r & ":" & n & linefeed
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                set found to found & my describe(childRef, depth + 1)
            end repeat
        end try
    end tell
    return found
end describe
tell application "System Events"
    tell process "FileCat"
        return my describe(window 1, 0)
    end tell
end tell
''')


def inspect_window(record):
    pid = exact_pid(record)
    state = osa('''
tell application "System Events"
    tell process "FileCat"
        set windowCount to count of windows
        if windowCount is 0 then return "no windows"
        return "windows=" & windowCount & " title=" & (name of window 1 as text) & " children=" & (count of UI elements of window 1) & " contents=" & (count of entire contents of window 1)
    end tell
end tell
''')
    return {"passed": True, "phase": "inspect-window", "pid": pid,
            "window": state, "sourceVisible": ax_find("payload.bin"),
            "copyVisible": ax_find("Copy…"), "controls": ax_controls()}


def inspect_form(record):
    exact_pid(record)
    controls = ax_controls()
    if (controls.splitlines().count("AXButton:Copy") != 2 or
            "AXTextField:Destination" not in controls or "AXButton:Cancel" not in controls):
        raise AssertionError("The Copy approval form or its decisive controls are not open")
    return {"passed": True, "phase": "inspect-form", "controls": controls,
            "sourceSha256": PAYLOAD_HASH, "destinationEmpty":
            not any(Path(record["destination"]).iterdir())}


def open_form(record):
    exact_pid(record)
    if any(Path(record["destination"]).iterdir()):
        raise AssertionError("The isolated destination is not empty")
    osa('''
on focusNamed(itemRef, depth)
    if depth > 28 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXRow" and (name of itemRef as text) is "payload.bin" then
                set focused of itemRef to true
                return (focused of itemRef)
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my focusNamed(childRef, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end focusNamed
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        if not my focusNamed(window 1, 0) then error "The source row did not receive focus"
    end tell
end tell
''')
    if not ax_find("Copy…", exact=True, click=True):
        raise AssertionError("The accessible Copy action is absent")
    wait_for(lambda: ax_find("Copy", exact=True), "the Copy approval form")
    result = inspect_form(record)
    result["phase"] = "open-form"
    return result


def partial_state(record):
    destination = Path(record["destination"])
    if (destination / "payload.bin").exists():
        raise AssertionError("The target was published before interrupted approval")
    staged = list(destination.glob(".fc-*.tmp"))
    if len(staged) != 1:
        raise AssertionError("Expected one test-owned staging file")
    bytes_on_disk = staged[0].read_bytes()
    if not bytes_on_disk or len(bytes_on_disk) > len(PAYLOAD):
        raise AssertionError("The staging file is empty or larger than the source")
    written = bytes_on_disk.find(b"\0")
    if written < 0:
        written = len(bytes_on_disk)
    prefix_matches = bytes_on_disk[:written] == PAYLOAD[:written]
    nonzero_tail = sum(value != 0 for value in bytes_on_disk[written:])
    if not 0 < written <= len(PAYLOAD) or not prefix_matches or nonzero_tail:
        raise AssertionError(f"The staged prefix or unwritten tail differs from the source: "
                             f"written={written}, size={len(bytes_on_disk)}, "
                             f"prefixMatches={prefix_matches}, nonzeroTail={nonzero_tail}")
    journal_dir = Path(record["data"]) / "local" / "journal"
    journals = sorted(p.name for p in journal_dir.glob("*.fcj"))
    if len(journals) != 1:
        raise AssertionError("The active Copy has no single durable journal")
    begin = next((json.loads(line.split(" ", 1)[1])
                  for line in (journal_dir / journals[0]).read_text(encoding="utf-8").splitlines()
                  if " " in line and '"t":"begin"' in line), None)
    intent = begin.get("transferIntent") if begin else None
    if (not intent or intent.get("kind") != "Copy" or
            intent.get("rateLimit") != 65_536 or
            intent.get("roots", [{}])[0].get("path") != str(Path(record["source"]) / "payload.bin") or
            record["destination"] not in intent.get("destination", "")):
        raise AssertionError("The durable journal did not retain the exact rate-limited Copy intent")
    return {"stagedPath": str(staged[0]), "stagedSize": len(bytes_on_disk),
            "writtenPrefixBytes": written, "targetUnpublished": True,
            "stagingComplete": written == len(PAYLOAD), "sourceSha256": PAYLOAD_HASH,
            "rateLimitBytesPerSecond": 65_536, "journalEntries": journals}


def set_speed_limit():
    return osa('''
on setSpeed(itemRef, depth)
    if depth > 28 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXTextField" and (name of itemRef as text) contains "Speed limit" then
                set value of itemRef to "0.0625"
                return ((value of itemRef as text) is "0.0625")
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my setSpeed(childRef, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end setSpeed
tell application "System Events"
    tell process "FileCat"
        return my setSpeed(window 1, 0)
    end tell
end tell
''') == "true"


def click_approval_copy():
    return osa('''
on clickSecond(itemRef, depth, seen)
    if depth > 28 then return {seen, false}
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXButton" and (name of itemRef as text) is "Copy" then
                set seen to seen + 1
                if seen is 2 then
                    click itemRef
                    return {seen, true}
                end if
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                set resultPair to my clickSecond(childRef, depth + 1, seen)
                set seen to item 1 of resultPair
                if item 2 of resultPair then return resultPair
            end repeat
        end try
    end tell
    return {seen, false}
end clickSecond
tell application "System Events"
    tell process "FileCat"
        set resultPair to my clickSecond(window 1, 0, 0)
        if item 2 of resultPair then return "approved"
        return "missing"
    end tell
end tell
''') == "approved"


def prepare(record):
    pid = exact_pid(record)
    form = inspect_form(record)
    if form["controls"].splitlines().count("AXButton:Copy") != 2:
        raise AssertionError("The modal Copy approval cannot be distinguished from the key bar")
    if not ax_find("Speed limit (MB/s):") and not ax_find("Options", exact=True, click=True):
        raise AssertionError("The Copy options control is unavailable")
    if not set_speed_limit():
        raise AssertionError("The reviewed Copy speed did not retain 0.0625 MB/s")
    if not click_approval_copy():
        raise AssertionError("The exact Copy approval button is unavailable")
    wait_for(lambda: partial_if_ready(record), "written Copy staging and journal", seconds=35)
    if exact_pid(record) != pid:
        raise AssertionError("The validation process changed during Copy")
    state = partial_state(record)
    if state["stagingComplete"]:
        raise AssertionError("The Copy reached a full staging file before this partial-copy interruption")
    os.kill(pid, signal.SIGKILL)
    wait_for(lambda: not _move["is_process_alive"](pid), "the exact Copy process to exit", seconds=15)
    return {"passed": True, "phase": "prepare", "killedPid": pid,
            "rateLimitMBps": 0.0625, **state}


def partial_if_ready(record):
    try:
        return partial_state(record)
    except (AssertionError, OSError):
        return None


def recovery(record):
    exact_pid(record)
    wait_for(lambda: ax_find("interrupted operation needs attention"), "interrupted Copy notice")
    if not ax_find("Continue copy") or not ax_find(record["destination"]):
        raise AssertionError("Recovery omitted its Copy action or exact destination")
    return partial_state(record)


def review(record):
    recovery(record)
    if not ax_find("Continue copy", click=True):
        raise AssertionError("Could not open the retained Copy review")
    wait_for(lambda: ax_find("Destination: " + record["destination"]), "reviewed Copy destination")
    if not ax_find("Continue copy", exact=True) or not ax_find("Cancel", exact=True):
        raise AssertionError("The Copy review has no explicit Continue and Cancel")
    return {"passed": True, "phase": "review", "destination": record["destination"],
            "explicitActions": True, **partial_state(record)}


def cancel_review(record):
    if not ax_find("Cancel", exact=True, click=True):
        raise AssertionError("Could not cancel the reviewed Copy")
    wait_for(lambda: not ax_find("Destination: " + record["destination"]), "Copy review to close")
    return {"passed": True, "phase": "cancel-review", **partial_state(record)}


def continue_review(record):
    review(record)
    if not ax_find("Continue copy", exact=True, click=True):
        raise AssertionError("Could not approve the exact reviewed Copy")
    return {"passed": True, "phase": "continue-review", "reviewedDestination": record["destination"]}


def inspect_completion(record):
    exact_pid(record)
    target = Path(record["destination"]) / "payload.bin"
    wait_for(lambda: target.is_file() and target.stat().st_size == len(PAYLOAD),
             "the continued Copy target", seconds=180)
    if hashlib.sha256(target.read_bytes()).hexdigest() != PAYLOAD_HASH:
        raise AssertionError("The continued Copy target differs from its source")
    if list(Path(record["destination"]).glob(".fc-*.tmp")):
        raise AssertionError("The successful continuation left staging behind")
    wait_for(lambda: ax_find("recovery tracking closed"), "closed recovery tracking", seconds=20)
    if not ax_find("Completed"):
        raise AssertionError("The Operations view did not show Completed")
    return {"passed": True, "phase": "inspect-completion", "sourceSha256": PAYLOAD_HASH,
            "destinationSha256": PAYLOAD_HASH, "stagingRemoved": True,
            "recoveryTrackingClosed": True}


def stop_completed(record):
    inspect_completion(record)
    pid = exact_pid(record)
    os.kill(pid, signal.SIGTERM)
    wait_for(lambda: not _move["is_process_alive"](pid), "completed validation app to close", seconds=15)
    return {"passed": True, "phase": "stop-completed", "pid": pid,
            "exactSourceAndDestinationRetained": True}


def main():
    phases = {"seed", "inspect-window", "open-form", "inspect-form", "prepare", "inspect-interruption",
              "inspect-recovery", "review", "cancel-review", "continue-review",
              "inspect-completion", "stop-completed"}
    if len(sys.argv) != 4 or sys.argv[1] not in phases:
        raise SystemExit(__doc__)
    phase, manifest, report = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    try:
        if report.parent.resolve(strict=True) != ROOT:
            raise AssertionError("Keep native receipts in FileCatValidation")
        record = None if phase == "seed" else checked_record(manifest)
        result = (seed(manifest) if phase == "seed" else
                  inspect_window(record) if phase == "inspect-window" else
                  open_form(record) if phase == "open-form" else
                  inspect_form(record) if phase == "inspect-form" else
                  prepare(record) if phase == "prepare" else
                  {"passed": True, "phase": phase, **partial_state(record)} if phase == "inspect-interruption" else
                  {"passed": True, "phase": phase, **recovery(record)} if phase == "inspect-recovery" else
                  review(record) if phase == "review" else
                  cancel_review(record) if phase == "cancel-review" else
                  continue_review(record) if phase == "continue-review" else
                  inspect_completion(record) if phase == "inspect-completion" else
                  stop_completed(record))
    except Exception as exc:
        result = {"passed": False, "phase": phase, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
