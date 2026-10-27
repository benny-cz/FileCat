#!/usr/bin/env python3
"""Stop a native interrupted-Copy review while its source probe is delayed.

Usage: linux-atspi-recovery-stop-held.py APP REPORT.json
The app, fixture and injected stat belong only to this isolated Ubuntu test.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
import subprocess
import sys
import time
import traceback
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

active = runpy.run_path(str(Path(__file__).with_name("linux-atspi-active-copy-interruption.py")))
wait_for = active["wait_for"]


def descendants(node):
    # A dialog can detach between the child-count and child lookup calls.
    if node is None:
        return
    yield node
    try:
        count = node.get_child_count()
    except Exception:
        return
    for index in range(count):
        try:
            child = node.get_child_at_index(index)
        except Exception:
            continue
        yield from descendants(child)


def button(root, prefix):
    return next((node for node in descendants(root) if node.get_role_name() == "button"
                 and (node.get_name() or "").startswith(prefix)), None)


def label(root, prefix):
    return next((node.get_name() for node in descendants(root) if node.get_role_name() == "label"
                 and (node.get_name() or "").startswith(prefix)), None)


def hashes(folder):
    if not folder.is_dir():
        raise AssertionError(f"Expected owned folder {folder}")
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in folder.iterdir() if p.is_file()}


def maybe_call(callback):
    try:
        return callback()
    except AssertionError:
        return None


def open_copy_with_options(fixture, validation):
    app = active["app_for"](fixture)
    panel = active["find_panel"](app, 1, Path(fixture["source"]))
    row = wait_for(lambda: next((node for node in descendants(panel)
        if node.get_role_name() == "list item" and
        (node.get_name() or "").startswith("payload.bin, file,")), None),
        "source payload row", seconds=20)
    if not row.grab_focus():
        raise AssertionError("Could not focus the exact source payload")
    active["send_key"](validation / "linux-uinput-key.py", "f5")
    options = wait_for(lambda: next((node for node in descendants(app)
        if node.get_role_name() == "toggle button" and node.get_name() == "Options"), None),
        "Copy options")
    if not options.do_action(0):
        raise AssertionError("Could not expand Copy options")
    wait_for(lambda: active["find"](app, "entry", "Speed limit (MB/s):"), "Copy speed option")


def trigger_review(source, ready):
    desktop = Atspi.get_desktop(0)
    app = wait_for(lambda: next((candidate for candidate in
        (desktop.get_child_at_index(index) for index in range(desktop.get_child_count()))
        if candidate is not None and candidate.get_name() == "FileCat" and
        any(node.get_role_name() == "list" and
            (node.get_name() or "").startswith("Panel 1, ") and
            (node.get_name() or "").endswith(f", {source}")
            for node in descendants(candidate))), None), "exact recovery app", seconds=20)
    action = wait_for(lambda: button(app, "Continue copy"), "retained Copy action")
    ready.write_text(str(time.monotonic()), encoding="ascii")
    if not action.do_action(0):
        raise AssertionError("Could not invoke the exact recovery action")
    ready.with_suffix(".done").write_text(str(time.monotonic()), encoding="ascii")


def main(app_path, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent.resolve() != validation or
            not report_path.name.startswith("ac-s2-042-recovery-") or report_path.suffix != ".json" or
            report_path.exists()):
        raise AssertionError("Use an isolated Git app and fresh owned report")
    password = os.environ.get("FILECAT_NATIVE_SUDO_PASSWORD")
    if not password:
        raise AssertionError("Transient guest authentication is needed for physical F5 and strace")
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    root = validation / "RecoveryHeldFixture" / uuid4().hex
    source, target, data = (root / leaf for leaf in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    payload = source / "payload.bin"
    payload.write_bytes(active["PAYLOAD"])
    expected_hash = hashlib.sha256(payload.read_bytes()).hexdigest()
    fixture = {"root": str(root), "source": str(source), "destination": str(target),
               "data": str(data), "size": payload.stat().st_size,
               "sha256": expected_hash, "app": str(app_path)}
    result = {"passed": False, "buildDllSha256": hashlib.sha256(
        app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "sourceSha256": expected_hash, "steps": []}
    process = tracer = trigger_process = None
    capture_marker = validation / "ac-s2-042-capture.ready"
    if capture_marker.exists():
        raise AssertionError("A previous owned capture marker remains")
    try:
        child_env = os.environ.copy()
        child_env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)

        def launch():
            with (root / "app.log").open("ab") as log:
                return subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                         "--left", str(source), "--right", str(target)],
                                        cwd=root, env=child_env, stdout=log,
                                        stderr=subprocess.STDOUT, start_new_session=True)

        process = launch()
        wait_for(lambda: maybe_call(lambda: active["app_for"](fixture)),
                 "initial FileCat panels", seconds=25)
        open_copy_with_options(fixture, validation)
        current_app = active["app_for"](fixture)
        speed = active["find"](current_app, "entry", "Speed limit (MB/s):")
        if speed is None or not speed.get_editable_text().set_text_contents("0.0625"):
            raise AssertionError("Could not set the owned Copy's bounded speed")
        active["submit_copy"](current_app)
        wait_for(lambda: active["partial_state_if_ready"](fixture),
                 "nonempty staging and durable journal", seconds=35)
        interrupted = active["partial_state"](fixture)
        if active["exact_app_pid"](fixture) != process.pid:
            raise AssertionError("The exact validation process changed before interruption")
        os.kill(process.pid, signal.SIGKILL)
        process.wait(timeout=10)
        process = None
        result["steps"].append({"interrupted": {key: interrupted[key] for key in
            ("stagedBytes", "journalEntries", "targetUnpublished")}})

        process = launch()
        result["restartedPid"] = process.pid
        app = wait_for(lambda: maybe_call(lambda: active["app_for"](fixture)),
                       "restarted FileCat panels", seconds=25)
        wait_for(lambda: maybe_call(lambda: active["recovery_state"](fixture)),
                 "interrupted Copy actions", seconds=20)
        if not label(app, "Operations"):
            details = button(app, "Details (Ctrl+J)")
            if details is None or not details.do_action(0):
                raise AssertionError("Could not open the interrupted Operations drawer")
        action = wait_for(lambda: button(app, "Continue copy"), "retained Continue copy action")
        if not action.grab_focus():
            raise AssertionError("Could not focus the retained Continue copy action")
        scopes = []
        parent = action.get_parent()
        for _ in range(6):
            if parent is None:
                break
            scopes.append(parent)
            parent = parent.get_parent()
        result["actionAncestors"] = [{"role": scope.get_role_name(),
                                       "name": scope.get_name(),
                                       "children": scope.get_child_count()} for scope in scopes]
        action_rect = action.get_component().get_extents(Atspi.CoordType.SCREEN)
        result["actionBefore"] = {
            "bounds": [action_rect.x, action_rect.y, action_rect.width, action_rect.height],
            "showing": action.get_state_set().contains(Atspi.StateType.SHOWING),
            "enabled": action.get_state_set().contains(Atspi.StateType.ENABLED),
        }
        stop = button(app, "Stop review")
        if stop is not None:
            raise AssertionError("Idle recovery incorrectly exposes Stop review")

        journal = data / "local" / "journal"
        journal_before = hashes(journal)
        staged_before = hashes(target)
        if not journal_before or not staged_before or (target / "payload.bin").exists():
            raise AssertionError("The interrupted file/journal state is incomplete")
        trace = root / "source-stat.log"
        tracer = subprocess.Popen(["sudo", "-S", "/usr/bin/strace", "-f", "-T", "-p",
                                   str(process.pid), "-o", str(trace), "-e", "trace=%file",
                                   "-e", "inject=lstat:delay_enter=8s", "-P", str(payload)],
                                  stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                  stderr=(root / "strace.err").open("wb"),
                                  start_new_session=True)
        tracer.stdin.write((password + "\n").encode())
        tracer.stdin.flush()
        tracer.stdin.close()
        wait_for(lambda: any(line.startswith("TracerPid:") and int(line.split()[1]) > 0
                             for line in Path(f"/proc/{process.pid}/status").read_text().splitlines()),
                 "tracer attachment", seconds=8)

        trigger_ready = root / "trigger.ready"
        trigger_process = subprocess.Popen([sys.executable, str(Path(__file__).resolve()),
                                            "--trigger", str(source), str(trigger_ready)], cwd=root, env=child_env,
                                           stdout=(root / "trigger.log").open("wb"),
                                           stderr=subprocess.STDOUT, start_new_session=True)
        wait_for(lambda: trigger_ready.is_file() or trigger_process.poll() is not None,
                 "recovery action client preparation", seconds=20)
        if not trigger_ready.is_file():
            raise AssertionError("The independent recovery action client exited before invocation")
        started = float(trigger_ready.read_text(encoding="ascii"))
        capture_marker.write_text(str(root), encoding="utf-8")
        time.sleep(0.5)
        result["actionReturnedWithinHalfSecond"] = trigger_ready.with_suffix(".done").exists()
        state_started = time.monotonic()
        result["continueDisabledWhileHeld"] = not action.get_state_set().contains(Atspi.StateType.ENABLED)
        result["directStateQuerySeconds"] = round(time.monotonic() - state_started, 3)
        status = wait_for(lambda: next((found for scope in scopes[:4]
            if (found := label(scope, "Reviewing recovery sources"))), None),
                          "immediate Reviewing status", seconds=4)
        stop = wait_for(lambda: next((found for scope in scopes[:4]
            if (found := button(scope, "Stop review"))), None),
            "Stop review action", seconds=4)
        latency = time.monotonic() - started
        action_enabled = action.get_state_set().contains(Atspi.StateType.ENABLED)
        result["steps"].append({"busyStatus": status,
                                "busyVisibleAfterSeconds": round(latency, 3),
                                "actionCallPendingDuringBusy": trigger_process.poll() is None,
                                "duplicateContinueEnabled": action_enabled,
                                "stopVisible": True})
        if latency >= 4 or action_enabled:
            raise AssertionError("The slow review did not promptly expose busy/disabled controls")
        capture_pause = float(os.environ.get("FILECAT_NATIVE_CAPTURE_PAUSE", "0"))
        if not 0 <= capture_pause <= 3:
            raise AssertionError("Capture pause must stay within the owned source delay")
        if capture_pause:
            time.sleep(capture_pause)
        if not stop.do_action(0):
            raise AssertionError("Could not Stop the busy review")
        result["steps"][-1]["stopClickedBeforeApproval"] = True
        if trigger_process.wait(timeout=15):
            raise AssertionError("The recovery action subprocess did not finish cleanly")
        wait_for(lambda: label(app, "Review stopped.") and not button(app, "Stop review"),
                 "canceled recovery to retire", seconds=20)
        time.sleep(0.4)
        trace_text = trace.read_text(errors="replace")
        if (f'lstat("{payload}"' not in trace_text or "<8." not in trace_text or
                button(app, "Cancel") is not None):
            raise AssertionError("The owned delayed stat or lack of late approval is unproven")
        journal_after, staged_after = hashes(journal), hashes(target)
        if (journal_after != journal_before or staged_after != staged_before or
                hashlib.sha256(payload.read_bytes()).hexdigest() != expected_hash or
                (target / "payload.bin").exists()):
            raise AssertionError("Stop changed the source, staging, published target or journal")
        result["steps"].append({"delayedSourceStatSeconds": 8,
                                "finalStatus": label(app, "Review stopped."),
                                "journalSha256": journal_after,
                                "stagedSha256": staged_after,
                                "sourcePreserved": True, "targetUnpublished": True,
                                "lateApprovalAbsent": True})
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        result["traceback"] = traceback.format_exc(limit=7)
        raise
    finally:
        capture_marker.unlink(missing_ok=True)
        if trigger_process is not None and trigger_process.poll() is None:
            trigger_process.terminate()
            try:
                trigger_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                trigger_process.kill()
        if tracer is not None and tracer.poll() is None:
            tracer.terminate()
            try:
                tracer.wait(timeout=5)
            except subprocess.TimeoutExpired:
                tracer.kill()
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            process.wait(timeout=10)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "--trigger":
        trigger_review(Path(sys.argv[2]), Path(sys.argv[3]))
        raise SystemExit(0)
    if len(sys.argv) != 3:
        raise SystemExit("Usage: linux-atspi-recovery-stop-held.py APP REPORT.json")
    main(Path(sys.argv[1]), Path(sys.argv[2]))
