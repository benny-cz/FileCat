#!/usr/bin/env python3
"""Exercise a process-interrupted, FileCat-owned Move on the isolated Mac.

Usage: python3 mac-ax-clipboard-interrupted-move.py seed|stop-previous|prepare|inspect|review|cancel-review|continue-review|inspect-completion|restore|reseed-after-crash FIXTURE.json REPORT.json
Launch the exact app/profile from the seed receipt before prepare and restart
it after prepare. Every filesystem mutation stays under FileCatValidation.
"""

import json
import os
from pathlib import Path
import runpy
import shlex
import signal
import subprocess
import sys
import time
from uuid import uuid4


clipboard = runpy.run_path(str(Path(__file__).with_name("mac-ax-clipboard-external-copy.py")))
ROOT = (Path.home() / "FileCatValidation").resolve(strict=True)
FIXTURE = ROOT / "mac-interrupted-move-fixture.json"
APP = ROOT / "FileCatValidation.app" / "Contents" / "MacOS" / "FileCat"
SOURCE = ROOT / "source"
DESTINATION = ROOT / "destination"
PASTEBOARD_HELPER = ROOT / "mac-pasteboard-files"
KEY_HELPER = ROOT / "mac-send-key"
ORIGINAL_SOURCE = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
ORIGINAL_TARGET = {"a.txt": b"alpha\n"}
FINISHED_SOURCE = {"a.txt": b"alpha\n"}
FINISHED_TARGET = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}


AX_SEARCH = '''
on seek(itemRef, needle, exactName, clickMatch, depth)
    if depth > 28 then return false
    tell application "System Events"
        try
            set itemName to name of itemRef as text
            if (exactName and itemName is needle) or ((not exactName) and itemName contains needle) then
                if clickMatch then
                    if (role of itemRef as text) is "AXButton" then
                        click itemRef
                        return true
                    end if
                else
                    return true
                end if
            end if
        end try
        if not clickMatch then
            try
                set itemValue to value of itemRef as text
                if itemValue contains needle then return true
            end try
        end if
        try
            repeat with childRef in (UI elements of itemRef)
                if my seek(childRef, needle, exactName, clickMatch, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end seek
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        return (my seek(window 1, NEEDLE, EXACT, CLICK, 0) as text)
    end tell
end tell
'''


def ax_find(needle, *, exact=False, click=False):
    script = (AX_SEARCH.replace("NEEDLE", clipboard["apple_string"](needle))
             .replace("EXACT", "true" if exact else "false")
             .replace("CLICK", "true" if click else "false"))
    return clipboard["osa"](script) == "true"


def wait_for(check, description, seconds=30):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if check():
            return
        time.sleep(0.25)
    raise AssertionError(f"Timed out waiting for {description}")


def manifest(folder):
    return {path.name: path.read_bytes() for path in folder.iterdir()}


def fixture(path):
    if path != FIXTURE:
        raise AssertionError("Use only the dedicated FileCatValidation fixture manifest")
    record = json.loads(path.read_text(encoding="utf-8"))
    data = Path(record["data"]).resolve(strict=True)
    if data.name != "data" or data.parent.parent != ROOT / "MacInterruptedMove" or len(data.parent.name) != 32:
        raise AssertionError("The profile is outside the isolated Mac fixture")
    if Path(record["app"]).resolve(strict=True) != APP.resolve(strict=True):
        raise AssertionError("The executable is not the signed validation app")
    if Path(record["source"]).resolve(strict=True) != SOURCE.resolve(strict=True):
        raise AssertionError("The source is outside the validation fixture")
    if Path(record["destination"]).resolve(strict=True) != DESTINATION.resolve(strict=True):
        raise AssertionError("The destination is outside the validation fixture")
    return record


def seed(path):
    if path != FIXTURE or path.exists():
        raise AssertionError("A dedicated fixture already exists; finish or inspect it before reseeding")
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("The isolated source or destination bytes are not at their initial state")
    for helper in (APP, PASTEBOARD_HELPER, KEY_HELPER):
        if not helper.is_file():
            raise AssertionError(f"Missing Mac validation executable: {helper}")
    data = ROOT / "MacInterruptedMove" / uuid4().hex / "data"
    data.mkdir(parents=True, exist_ok=False)
    record = {"app": str(APP), "source": str(SOURCE), "destination": str(DESTINATION),
              "data": str(data), "pasteboardHelper": str(PASTEBOARD_HELPER),
              "keyHelper": str(KEY_HELPER)}
    path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "phase": "seed", **record}


def pids_for_args(expected):
    process = subprocess.run(["ps", "-axo", "pid=,command="], capture_output=True,
                             text=True, timeout=15, check=True)
    matches = []
    for line in process.stdout.splitlines():
        try:
            pid, command = line.strip().split(None, 1)
            args = shlex.split(command)
        except (ValueError, IndexError):
            continue
        if args == expected:
            matches.append(int(pid))
    return matches


def pid_for_args(expected):
    matches = pids_for_args(expected)
    if len(matches) != 1:
        raise AssertionError(f"Expected one exact validation app process; found {len(matches)}")
    return matches[0]


def exact_app_pid(record):
    expected = [record["app"], "--new-instance", "--data", record["data"],
                "--left", record["source"], "--right", record["destination"]]
    return pid_for_args(expected)


def stop_previous(record):
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("The previous validation app's fixture is not idle and exact")
    previous = [record["app"], "--new-instance", "--data", str(ROOT / "data-clipboard-replay-fixed"),
                "--left", record["source"], "--right", record["destination"]]
    pid = pid_for_args(previous)
    if ax_find("Review decision") or ax_find("interrupted operation needs attention"):
        raise AssertionError("The previous validation app has an unresolved operation")
    os.kill(pid, signal.SIGTERM)
    wait_for(lambda: not is_process_alive(pid), "the previous exact validation app to close", seconds=15)
    return {"passed": True, "phase": "stop-previous", "pid": pid,
            "sourceAndTargetBytesExact": True}


def journal_entries(record):
    folder = Path(record["data"]) / "local" / "journal"
    return sorted(path.name for path in folder.glob("*.fcj")) if folder.is_dir() else []


def prepare(record):
    pid = exact_app_pid(record)
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("The Move fixture changed before the interruption")
    clipboard["require_no_marks"]()
    clipboard["osa"]('set the clipboard to ' + clipboard["apple_string"]("FileCat interrupted Move " + str(uuid4())))
    before = clipboard["pasteboard_files"](PASTEBOARD_HELPER)
    if before["filePaths"]:
        raise AssertionError("The test sentinel exposed file URLs")
    clipboard["focus_source"](str(SOURCE))
    clipboard["filecat_action"]('key code 35 using {command down, shift down}')
    clipboard["filecat_action"]('key code 0 using {command down}')
    clipboard["filecat_action"]('key code 51')
    clipboard["filecat_action"]('keystroke "Select all"')
    clipboard["filecat_action"]('key code 36')
    wait_for(lambda: clipboard["filecat_action"]('return (my hasMarkedItems(window 1, 0) as text)') == "true",
             "both source roots to be selected")
    clipboard["send_key"](KEY_HELPER, "cut")
    expected = {str(SOURCE / "a.txt"), str(SOURCE / "b.txt")}
    wait_for(lambda: set(clipboard["pasteboard_files"](PASTEBOARD_HELPER)["filePaths"]) == expected,
             "the exact two-file Cut publication")
    cut = clipboard["pasteboard_files"](PASTEBOARD_HELPER)
    if cut["changeCount"] <= before["changeCount"]:
        raise AssertionError("The owned Cut did not publish a new generation")
    clipboard["paste_into_target"](str(DESTINATION), KEY_HELPER)
    wait_for(lambda: ax_find("Review decision"), "the held Move conflict review")
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("The held conflict mutated source or destination before approval")
    journals = journal_entries(record)
    if len(journals) != 1:
        raise AssertionError("The held Move did not create exactly one durable journal")
    if exact_app_pid(record) != pid:
        raise AssertionError("The validation process changed before interruption")
    os.kill(pid, signal.SIGKILL)
    wait_for(lambda: not is_process_alive(pid),
             "the exact validation process to exit", seconds=15)
    clipboard["osa"]('set the clipboard to "FileCat interrupted Move test paused"')
    return {"passed": True, "phase": "prepare", "pid": pid,
            "cutPublishedBothExactFiles": True, "conflictHeldBeforeMutation": True,
            "sourceAndTargetBytesExact": manifest(SOURCE) == ORIGINAL_SOURCE and manifest(DESTINATION) == ORIGINAL_TARGET,
            "journalEntries": journals}


def is_process_alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False


def inspect(record):
    pid = exact_app_pid(record)
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("Restart changed source or destination bytes")
    wait_for(lambda: ax_find("interrupted operation needs attention"), "the interrupted operation notice")
    if not ax_find("Continue move") or not ax_find(str(DESTINATION)):
        raise AssertionError("Recovery did not expose the exact Move destination and action")
    journals = journal_entries(record)
    if len(journals) != 1:
        raise AssertionError("The exact interrupted journal was not retained")
    return {"passed": True, "phase": "inspect", "pid": pid,
            "sourceAndTargetBytesExact": True, "explicitContinuation": True,
            "retainedDestination": str(DESTINATION), "journalEntries": journals}


def review(record):
    inspect(record)
    if not ax_find("Continue move", click=True):
        raise AssertionError("Could not open the interrupted Move review")
    wait_for(lambda: ax_find("Destination: " + str(DESTINATION)), "the exact reviewed destination")
    if not ax_find("Continue move", exact=True) or not ax_find("Cancel", exact=True):
        raise AssertionError("The reviewed Move has no explicit Continue and Cancel actions")
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("Opening the review changed source or target bytes")
    return {"passed": True, "phase": "review", "reviewedDestination": str(DESTINATION),
            "sourceAndTargetBytesExact": True, "explicitActions": True}


def cancel_review(record):
    if not ax_find("Cancel", exact=True, click=True):
        raise AssertionError("Could not cancel the reviewed Move")
    wait_for(lambda: not ax_find("Destination: " + str(DESTINATION)), "review to close")
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("Cancelling the review changed source or target bytes")
    return {"passed": True, "phase": "cancel-review", "sourceAndTargetBytesExact": True,
            "journalEntries": journal_entries(record)}


def continue_review(record):
    review(record)
    if not ax_find("Continue move", exact=True, click=True):
        raise AssertionError("Could not approve the exact reviewed Move")
    return {"passed": True, "phase": "continue-review", "explicitApproval": True,
            "reviewedDestination": str(DESTINATION)}


def inspect_completion(record):
    exact_app_pid(record)
    wait_for(lambda: manifest(SOURCE) == FINISHED_SOURCE and manifest(DESTINATION) == FINISHED_TARGET,
             "the exact conservative Move result", seconds=45)
    if not ax_find("1 done, 1 skipped"):
        raise AssertionError("The UI did not show the exact done/skipped outcome")
    if not ax_find("recovery tracking closed"):
        raise AssertionError("The UI did not close recovery tracking")
    return {"passed": True, "phase": "inspect-completion", "sourceBytesExact": True,
            "destinationBytesExact": True, "oneDoneOneSkipped": True,
            "recoveryTrackingClosed": True}


def restore(record):
    inspect_completion(record)
    (DESTINATION / "b.txt").unlink()
    (SOURCE / "b.txt").write_bytes(b"beta\n")
    clipboard["osa"]('set the clipboard to "FileCat interrupted Move test complete"')
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("The test-owned fixture did not restore exactly")
    return {"passed": True, "phase": "restore", "fixtureRestoredExact": True,
            "clipboardRetired": True}


def reseed_after_crash(record, path):
    """Preserve a crashed profile and reset only this runner's known two-file fixture."""
    args = [record["app"], "--new-instance", "--data", record["data"],
            "--left", record["source"], "--right", record["destination"]]
    if pids_for_args(args):
        raise AssertionError("The exact crashed validation app is still running")
    if manifest(SOURCE) != FINISHED_SOURCE or manifest(DESTINATION) != FINISHED_TARGET:
        raise AssertionError("The expected post-approval effect is not exact; preserve for inspection")
    journals = journal_entries(record)
    if not journals:
        raise AssertionError("The crashed profile has no recovery journal to preserve")
    archived = ROOT / ("mac-interrupted-move-crash-" + Path(record["data"]).parent.name + ".json")
    if archived.exists():
        raise AssertionError("The crashed profile manifest was already archived")
    archived.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    os.replace(DESTINATION / "b.txt", SOURCE / "b.txt")
    if manifest(SOURCE) != ORIGINAL_SOURCE or manifest(DESTINATION) != ORIGINAL_TARGET:
        raise AssertionError("The isolated fixture did not restore exactly")
    data = ROOT / "MacInterruptedMove" / uuid4().hex / "data"
    data.mkdir(parents=True, exist_ok=False)
    fresh = {**record, "data": str(data)}
    path.write_text(json.dumps(fresh, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "phase": "reseed-after-crash", "archivedManifest": str(archived),
            "oldJournals": journals, "data": str(data), "fixtureRestoredExact": True}


def main():
    phases = {"seed", "stop-previous", "prepare", "inspect", "review", "cancel-review",
              "continue-review", "inspect-completion", "restore", "reseed-after-crash"}
    if len(sys.argv) != 4 or sys.argv[1] not in phases:
        raise SystemExit(__doc__)
    phase, fixture_path, report_path = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    try:
        record = None if phase == "seed" else fixture(fixture_path)
        result = (seed(fixture_path) if phase == "seed" else
                  stop_previous(record) if phase == "stop-previous" else
                  prepare(record) if phase == "prepare" else
                  inspect(record) if phase == "inspect" else
                  review(record) if phase == "review" else
                  cancel_review(record) if phase == "cancel-review" else
                  continue_review(record) if phase == "continue-review" else
                  inspect_completion(record) if phase == "inspect-completion" else
                  reseed_after_crash(record, fixture_path) if phase == "reseed-after-crash" else
                  restore(record))
    except Exception as exc:
        result = {"passed": False, "phase": phase, "error": f"{type(exc).__name__}: {exc}"}
    report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
