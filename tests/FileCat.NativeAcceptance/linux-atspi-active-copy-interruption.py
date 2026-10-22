#!/usr/bin/env python3
"""Exercise a rate-limited Copy interrupted in the isolated Ubuntu GUI.

Usage: linux-atspi-active-copy-interruption.py seed|stop-completed|open-form|open-options|inspect-form|start-fast|interrupt|interrupt-running|inspect-interruption|inspect-copy-state|inspect-recovery|review|cancel-review|continue-review|inspect-completion|inspect-finished-details FIXTURE.json REPORT.json
The seed phase creates only a new guest-local FileCatValidation fixture. Later
phases require the exact FileCat executable, profile and panel arguments.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
import sys
import time
import uuid

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-clipboard-owned-move.py")))
descendants = common["descendants"]
find = common["find"]
find_panel = common["find_panel"]
send_key = common["send_key"]
wait_for = common["wait_for"]

ROOT = (Path.home() / "FileCatValidation").resolve(strict=True)
PAYLOAD = b"FileCat interrupted active Copy fixture\n" * 500_000


def seed(fixture_path):
    expected = ROOT / "active-copy-fixture.json"
    if fixture_path != expected:
        raise AssertionError("The fixture manifest must stay inside FileCatValidation")
    if fixture_path.exists():
        previous = checked_fixture(fixture_path)
        target = Path(previous["destination"]) / "payload.bin"
        if not target.is_file() or target.stat().st_size != previous["size"] or hashlib.sha256(target.read_bytes()).hexdigest() != previous["sha256"]:
            raise AssertionError("A previous active-copy fixture still needs review or completion")
        try:
            exact_app_pid(previous)
        except AssertionError as exc:
            if "found 0" not in str(exc):
                raise
        else:
            raise AssertionError("Stop the completed validation app before creating a new fixture")
    root = ROOT / "ActiveCopy" / uuid.uuid4().hex
    source = root / "source"
    destination = root / "destination"
    data = root / "data"
    for folder in (source, destination, data):
        folder.mkdir(parents=True, exist_ok=False)
    (source / "payload.bin").write_bytes(PAYLOAD)
    fixture = {"root": str(root), "source": str(source), "destination": str(destination),
               "data": str(data), "size": len(PAYLOAD),
               "sha256": hashlib.sha256(PAYLOAD).hexdigest(),
               "app": str(ROOT / "app" / "FileCat")}
    fixture_path.write_text(json.dumps(fixture, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "phase": "seed", **fixture}


def checked_fixture(fixture_path):
    if fixture_path != ROOT / "active-copy-fixture.json":
        raise AssertionError("Use only the dedicated active-copy fixture manifest")
    fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
    root = Path(fixture["root"]).resolve(strict=True)
    if root.parent != ROOT / "ActiveCopy" or len(root.name) != 32:
        raise AssertionError("Fixture root is outside the owned active-copy folder")
    for field in ("source", "destination", "data"):
        if Path(fixture[field]).resolve(strict=True) != root / field:
            raise AssertionError(f"Unexpected fixture {field} path")
    if Path(fixture["app"]).resolve(strict=True) != ROOT / "app" / "FileCat":
        raise AssertionError("Unexpected validation executable")
    payload = Path(fixture["source"]) / "payload.bin"
    if payload.stat().st_size != fixture["size"] or hashlib.sha256(payload.read_bytes()).hexdigest() != fixture["sha256"]:
        raise AssertionError("Source payload changed")
    return fixture


def app_for(fixture):
    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find_panel(app, 1, Path(fixture["source"]))
               and find_panel(app, 2, Path(fixture["destination"]))]
    if len(matches) != 1:
        raise AssertionError(f"Expected one exact active-copy FileCat window; found {len(matches)}")
    return matches[0]


def inspect_form(fixture):
    app = app_for(fixture)
    copies = [node for node in descendants(app)
              if node.get_role_name() == "button" and node.get_name() == "Copy"]
    copy = copies[0] if copies else None
    def ancestry(node):
        result = []
        for _ in range(5):
            if node is None:
                break
            result.append(f"{node.get_role_name()}: {node.get_name()}")
            node = node.get_parent()
        return result
    fields = sorted({f"{node.get_role_name()}: {node.get_name()}"
                     for node in descendants(app) if node.get_role_name() in
                     ("entry", "button", "combo box", "expander", "toggle button") and node.get_name()})
    text = sorted({node.get_name() for node in descendants(app)
                   if node.get_role_name() == "label" and node.get_name()})
    return {"passed": True, "phase": "inspect-form", "formControls": fields[:100],
            "labels": text[:100], "sourceBytesExact": True,
            "copyEnabled": bool(copy and copy.get_state_set().contains(Atspi.StateType.ENABLED)),
            "copyButtons": [{"ancestry": ancestry(node), "enabled":
                             node.get_state_set().contains(Atspi.StateType.ENABLED)}
                            for node in copies],
            "destinationStillEmpty": not any(Path(fixture["destination"]).iterdir())}


def open_form(fixture):
    app = app_for(fixture)
    source = Path(fixture["source"])
    row = wait_for(lambda: find(find_panel(app, 1, source), "list item", "payload.bin"),
                   "source payload row")
    if not row.grab_focus():
        raise AssertionError("Could not focus the source payload")
    send_key(ROOT / "linux-uinput-key.py", "f5")
    result = inspect_form(fixture)
    result["phase"] = "open-form"
    return result


def open_options(fixture):
    app = app_for(fixture)
    options = find(app, "toggle button", "Options")
    if options is None or options.get_n_actions() != 1 or not options.do_action(0):
        raise AssertionError("The Copy form's Options toggle was unavailable")
    result = inspect_form(fixture)
    result["phase"] = "open-options"
    return result


def exact_app_pid(fixture):
    expected_args = [fixture["app"], "--new-instance", "--data", fixture["data"],
                     "--left", fixture["source"], "--right", fixture["destination"]]
    matches = []
    for proc in Path("/proc").iterdir():
        if not proc.name.isdecimal():
            continue
        try:
            args = (proc / "cmdline").read_bytes().rstrip(b"\0").decode().split("\0")
            executable = (proc / "exe").resolve(strict=True)
        except (OSError, UnicodeError):
            continue
        if args == expected_args and executable == Path(fixture["app"]):
            matches.append(int(proc.name))
    if len(matches) != 1:
        raise AssertionError(f"Expected one exact validation Copy process; found {len(matches)}")
    return matches[0]


def stop_completed(fixture):
    target = Path(fixture["destination"]) / "payload.bin"
    if target.stat().st_size != fixture["size"] or hashlib.sha256(target.read_bytes()).hexdigest() != fixture["sha256"]:
        raise AssertionError("Only a fully completed, exact-byte validation fixture may be stopped here")
    pid = exact_app_pid(fixture)
    os.kill(pid, signal.SIGTERM)
    wait_for(lambda: not Path(f"/proc/{pid}").exists(), "the completed validation process to exit", seconds=15)
    return {"passed": True, "phase": "stop-completed", "pid": pid,
            "completedTargetBytesExact": True}


def partial_state(fixture):
    destination = Path(fixture["destination"])
    staged = list(destination.glob(".fc-*.tmp"))
    if len(staged) != 1 or not 0 < staged[0].stat().st_size <= fixture["size"]:
        raise AssertionError("Expected one nonempty owned staging file")
    staged_bytes = staged[0].read_bytes()
    if staged_bytes == PAYLOAD:
        written = len(staged_bytes)
    else:
        # The local provider preallocates the full target length. Only the
        # leading bytes before its zero-filled unwritten tail are durable.
        written = staged_bytes.find(b"\0")
        if not 0 < written < fixture["size"] or staged_bytes[:written] != PAYLOAD[:written] or any(staged_bytes[written:]):
            raise AssertionError("The staged written prefix or unwritten tail differs from the source")
    if (destination / "payload.bin").exists():
        raise AssertionError("The still-running target was published")
    journal = Path(fixture["data"]) / "local" / "journal"
    if not journal.is_dir() or not any(journal.iterdir()):
        raise AssertionError("The active Copy has no durable journal")
    return {"stagedPath": str(staged[0]), "stagedBytes": len(staged_bytes),
            "stagedWrittenPrefixBytes": written,
            "sourceBytesExact": True, "targetUnpublished": True,
            "journalEntries": len(list(journal.iterdir()))}


def interrupt(fixture):
    app = app_for(fixture)
    pid = exact_app_pid(fixture)
    field = find(app, "entry", "Speed limit (MB/s):")
    if field is None or not field.get_editable_text().set_text_contents("0.0625"):
        raise AssertionError("Could not set the reviewed 0.0625 MB/s speed limit")
    submit_copy(app)
    wait_for(lambda: partial_state_if_ready(fixture), "nonempty staging and durable journal", seconds=35)
    return interrupt_running(fixture, expected_pid=pid, phase="interrupt")


def submit_copy(app):
    def enabled_copy():
        for button in descendants(app):
            if button.get_role_name() != "button" or button.get_name() != "Copy":
                continue
            parent = button
            for _ in range(8):
                parent = parent.get_parent() if parent is not None else None
                if parent is None:
                    break
                # Builds before 9479b280 exposed the card by class name; later ones name it "<title> dialog".
                if (parent.get_name() == "DialogCard" or parent.get_name().endswith(" dialog")) and \
                        button.get_state_set().contains(Atspi.StateType.ENABLED):
                    return button
        return None
    choice = wait_for(enabled_copy, "enabled Copy approval", seconds=15)
    if choice is None or choice.get_n_actions() != 1 or not choice.do_action(0):
        raise AssertionError("Could not start the reviewed Copy")


def start_fast(fixture):
    if any(Path(fixture["destination"]).iterdir()):
        raise AssertionError("The fresh target is not empty")
    submit_copy(app_for(fixture))
    wait_for(lambda: (Path(fixture["destination"]) / "payload.bin").is_file(),
             "the fresh Copy target", seconds=35)
    wait_for(lambda: any((node.get_name() or "").startswith("Completed ")
                         for node in descendants(app_for(fixture)) if node.get_role_name() == "label"),
             "the fresh Copy's completed status", seconds=35)
    completed_files(fixture)
    return {"passed": True, "phase": "start-fast", "sourceBytesExact": True,
            "destinationBytesExact": True}


def interrupt_running(fixture, expected_pid=None, phase="interrupt-running"):
    app = app_for(fixture)
    pid = exact_app_pid(fixture)
    if expected_pid is not None and pid != expected_pid:
        raise AssertionError("The validation Copy process changed before interruption")
    def visible_rate():
        return any("Copying" in (node.get_name() or "") and "limited to 65,536/s" in (node.get_name() or "")
                   for node in descendants(app) if node.get_role_name() == "label")
    wait_for(visible_rate, "visible rate-limited Copy", seconds=15)
    state = partial_state(fixture)
    os.kill(pid, signal.SIGKILL)
    wait_for(lambda: not Path(f"/proc/{pid}").exists(), "the exact validation process to exit")
    return {"passed": True, "phase": phase, "pid": pid, "rateLimitMbPerSecond": 0.0625,
            "visibleCopyInProgress": True, **partial_state(fixture)}


def partial_state_if_ready(fixture):
    try:
        return partial_state(fixture)
    except (AssertionError, OSError):
        return None


def inspect_interruption(fixture):
    return {"passed": True, "phase": "inspect-interruption", **partial_state(fixture)}


def inspect_copy_state(fixture):
    app = app_for(fixture)
    destination = Path(fixture["destination"])
    journal = Path(fixture["data"]) / "local" / "journal"
    def entries(folder):
        return [{"name": path.name, "size": path.stat().st_size if path.is_file() else None}
                for path in folder.iterdir()] if folder.is_dir() else []
    labels = [node.get_name() for node in descendants(app)
              if node.get_role_name() == "label" and node.get_name()]
    buttons = [node.get_name() for node in descendants(app)
               if node.get_role_name() == "button" and node.get_name()]
    return {"passed": True, "phase": "inspect-copy-state", "destination": entries(destination),
            "journal": entries(journal), "labels": labels[:100], "buttons": buttons[:100]}


def recovery_state(fixture):
    app = app_for(fixture)
    labels = [node.get_name() for node in descendants(app)
              if node.get_role_name() == "label" and node.get_name()]
    buttons = [node for node in descendants(app) if node.get_role_name() == "button"]
    if not any("interrupted operation needs attention" in name for name in labels):
        raise AssertionError("The restarted app did not announce the interrupted Copy")
    if not any(name.startswith('Copy "payload.bin" to ') and fixture["destination"] in name for name in labels):
        raise AssertionError("The retained Copy destination is missing")
    if not any((node.get_name() or "").startswith("Continue copy") for node in buttons):
        raise AssertionError("There is no explicit Copy continuation action")
    return app, labels, buttons


def inspect_recovery(fixture):
    app, labels, buttons = recovery_state(fixture)
    state = partial_state(fixture)
    return {"passed": True, "phase": "inspect-recovery", "pid": exact_app_pid(fixture),
            "sourceBytesExact": True, "targetUnpublished": True,
            "relevantAccessibleNames": [name[:240] for name in labels if any(term in name.lower()
                for term in ("interrupted", "continue", "copy", "source items"))][:20],
            "relevantButtons": [node.get_name() for node in buttons if "copy" in (node.get_name() or "").lower()],
            **state}


def review(fixture):
    app, _, buttons = recovery_state(fixture)
    action = next(node for node in buttons if (node.get_name() or "").startswith("Continue copy"))
    if action.get_n_actions() != 1 or not action.do_action(0):
        raise AssertionError("Could not open the interrupted Copy review")
    wait_for(lambda: find(app, "button", "Continue copy"), "the reviewed Copy approval")
    labels = [node.get_name() for node in descendants(app)
              if node.get_role_name() == "label" and node.get_name()]
    buttons = [node.get_name() for node in descendants(app)
               if node.get_role_name() == "button" and node.get_name()]
    if f'Destination: {fixture["destination"]}' not in labels:
        raise AssertionError("The Copy review omitted the complete destination")
    if "Cancel" not in buttons or "Continue copy" not in buttons:
        raise AssertionError("The Copy review omitted an explicit Cancel or Continue action")
    partial_state(fixture)
    return {"passed": True, "phase": "review", "sourceBytesExact": True,
            "targetUnpublished": True, "relevantAccessibleNames": [name[:240] for name in labels
                if any(term in name.lower() for term in ("destination", "source", "continue", "copy", "rate"))][:35],
            "buttons": buttons[:40]}


def cancel_review(fixture):
    app = app_for(fixture)
    choice = find(app, "button", "Cancel")
    if choice is None or choice.get_n_actions() != 1 or not choice.do_action(0):
        raise AssertionError("Could not cancel the reviewed Copy")
    wait_for(lambda: not find(app, "button", "Continue copy"), "Copy review to close")
    partial_state(fixture)
    return {"passed": True, "phase": "cancel-review", "sourceBytesExact": True,
            "targetUnpublished": True, "stagingAndJournalRetained": True}


def continue_review(fixture):
    review(fixture)
    app = app_for(fixture)
    choices = [node for node in descendants(app)
               if node.get_role_name() == "button" and node.get_name() == "Continue copy"]
    if len(choices) != 1 or choices[0].get_n_actions() != 1 or not choices[0].do_action(0):
        raise AssertionError("The reviewed Copy's exact Continue action was unavailable")
    return {"passed": True, "phase": "continue-review", "reviewedDestination": fixture["destination"],
            "sourceBytesExact": True, "explicitApproval": True}


def completed_files(fixture):
    app = app_for(fixture)
    source = Path(fixture["source"]) / "payload.bin"
    destination = Path(fixture["destination"])
    target = destination / "payload.bin"
    if source.stat().st_size != fixture["size"] or hashlib.sha256(source.read_bytes()).hexdigest() != fixture["sha256"]:
        raise AssertionError("The source bytes changed after Copy continuation")
    if not target.exists() or target.stat().st_size != fixture["size"] or hashlib.sha256(target.read_bytes()).hexdigest() != fixture["sha256"]:
        raise AssertionError("The resumed Copy did not publish the exact source bytes")
    if list(destination.glob(".fc-*.tmp")):
        raise AssertionError("A staging file remained after successful continuation")
    labels = [node.get_name() for node in descendants(app)
              if node.get_role_name() == "label" and node.get_name()]
    if not any(name.startswith("Completed ") and "planned bytes accounted for" in name
               for name in labels):
        raise AssertionError("The UI did not report the resumed Copy as completed")
    return labels


def inspect_completion(fixture):
    labels = completed_files(fixture)
    if not any("recovery tracking closed" in name.lower() for name in labels):
        raise AssertionError("The UI did not close recovery tracking")
    return {"passed": True, "phase": "inspect-completion", "sourceBytesExact": True,
            "destinationBytesExact": True, "stagingRemoved": True, "recoveryTrackingClosed": True,
            "relevantAccessibleNames": [name[:240] for name in labels
                if "completed" in name.lower() or "recovery tracking" in name.lower()][:12]}


def inspect_finished_details(fixture):
    completed_files(fixture)
    app = app_for(fixture)
    if not find(app, "label", "Operations"):
        details = find(app, "button", "Details (Ctrl+J)")
        if details is None or details.get_n_actions() != 1 or not details.do_action(0):
            raise AssertionError("Could not open the finished operation details")
    def average_label():
        return next((node.get_name() for node in descendants(app)
                     if node.get_role_name() == "label" and
                     (node.get_name() or "").startswith("Average transfer speed:")), None)
    average = wait_for(average_label, "the visible finished average transfer speed", seconds=15)
    if not average or average.partition(":")[2].strip() in ("", "0 bytes/s"):
        raise AssertionError("The finished details have no actual average rate")
    return {"passed": True, "phase": "inspect-finished-details", "averageTransferSpeed": average,
            "sourceBytesExact": True, "destinationBytesExact": True}


def main():
    if len(sys.argv) != 4 or sys.argv[1] not in ("seed", "stop-completed", "open-form", "open-options", "inspect-form", "start-fast", "interrupt", "interrupt-running", "inspect-interruption", "inspect-copy-state", "inspect-recovery", "review", "cancel-review", "continue-review", "inspect-completion", "inspect-finished-details"):
        raise SystemExit(__doc__)
    phase, fixture_path, report_path = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
    try:
        result = (seed(fixture_path) if phase == "seed" else
                  stop_completed(checked_fixture(fixture_path)) if phase == "stop-completed" else
                  open_form(checked_fixture(fixture_path)) if phase == "open-form" else
                  open_options(checked_fixture(fixture_path)) if phase == "open-options" else
                  start_fast(checked_fixture(fixture_path)) if phase == "start-fast" else
                  interrupt(checked_fixture(fixture_path)) if phase == "interrupt" else
                  interrupt_running(checked_fixture(fixture_path)) if phase == "interrupt-running" else
                  inspect_interruption(checked_fixture(fixture_path)) if phase == "inspect-interruption" else
                  inspect_copy_state(checked_fixture(fixture_path)) if phase == "inspect-copy-state" else
                  inspect_recovery(checked_fixture(fixture_path)) if phase == "inspect-recovery" else
                  review(checked_fixture(fixture_path)) if phase == "review" else
                  cancel_review(checked_fixture(fixture_path)) if phase == "cancel-review" else
                  continue_review(checked_fixture(fixture_path)) if phase == "continue-review" else
                  inspect_completion(checked_fixture(fixture_path)) if phase == "inspect-completion" else
                  inspect_finished_details(checked_fixture(fixture_path)) if phase == "inspect-finished-details" else
                  inspect_form(checked_fixture(fixture_path)))
    except Exception as exc:
        result = {"passed": False, "phase": phase, "error": f"{type(exc).__name__}: {exc}"}
    report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
