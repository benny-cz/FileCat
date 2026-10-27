#!/usr/bin/env python3
"""Native 200% Clear finished history/Keep history acceptance for an unused Undo.

Usage: linux-atspi-clear-history.py APP_PATH REPORT.json
Run in the logged-in Ubuntu VM session against a Git-built FileCat under
~/FileCatValidation. Owns its FileCat, Orca, and filesystem fixture.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
import traceback
from uuid import uuid4

helpers = runpy.run_path(str(Path(__file__).with_name("linux-atspi-undo-narrow.py")))
speech_helpers = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
Atspi = helpers["Atspi"]
wait_for = helpers["wait_for"]
invoke = helpers["invoke"]
resize_owned = helpers["resize_owned"]
bounds = helpers["bounds"]
inside = helpers["inside"]


def descendants(root):
    """Snapshot a changing AT-SPI tree without following vanished children."""
    pending = [root]
    while pending:
        node = pending.pop()
        if node is None:
            continue
        yield node
        try:
            pending.extend(node.get_child_at_index(i)
                           for i in reversed(range(node.get_child_count())))
        except (AttributeError, RuntimeError):
            continue


def one(root, role, name):
    for node in descendants(root):
        try:
            if node.get_role_name() == role and node.get_name() == name:
                return node
        except Exception:
            # Avalonia can retire an accessibility object during a listing
            # refresh while AT-SPI still holds its reference.
            continue
    return None


def dialog_button(app, name):
    for node in descendants(app):
        try:
            matches = node.get_role_name() == "button" and node.get_name() == name
        except Exception:
            continue
        if not matches:
            continue
        parent = node
        for _ in range(8):
            try:
                parent = parent.get_parent() if parent is not None else None
            except (AttributeError, RuntimeError):
                break
            if parent is None:
                break
            try:
                if parent.get_name().endswith(" dialog"):
                    return node
            except Exception:
                break
    return None


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation):
        raise SystemExit("Use an isolated Git-built FileCat and guest-local report")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    runtime = Path(os.environ["XDG_RUNTIME_DIR"]).resolve(strict=True)
    authorities = list(runtime.glob(".mutter-Xwaylandauth.*"))
    if len(authorities) != 1 or authorities[0].stat().st_uid != os.getuid():
        raise SystemExit("The logged-in Xwayland authority is unavailable")
    os.environ["XAUTHORITY"] = str(authorities[0])
    os.environ["WAYLAND_DISPLAY"] = "wayland-0"
    os.environ["XDG_SESSION_TYPE"] = "wayland"
    os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    os.environ["PATH"] = os.environ["DOTNET_ROOT"] + os.pathsep + os.environ.get("PATH", "")
    display = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                             capture_output=True, text=True, timeout=5).stdout
    if "Scale: 2.0" not in display:
        raise SystemExit("Isolated Ubuntu display must be at 200%")
    if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                      capture_output=True).returncode == 0:
        raise SystemExit("Stop the existing Orca instance before this owned test")

    fixture = validation / "ClearHistoryNarrow" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    name = "owned-history-undo-file.txt"
    source_file, target_file = source / name, target / name
    source_file.write_bytes(b"owned clear-history unused undo\n")
    digest = hashlib.sha256(source_file.read_bytes()).hexdigest()
    with app_path.with_name("FileCat.dll").open("rb") as assembly:
        assembly_hash = hashlib.file_digest(assembly, "sha256").hexdigest()
    result = {"passed": False, "fixture": str(fixture), "appDllSha256": assembly_hash,
              "displayScale": 2.0, "source": str(source_file), "target": str(target_file)}
    speech = speech_helpers["SpeechLog"](fixture / "orca-debug.log")
    result["orcaSpeechLog"] = str(speech.path)
    process = orca = None
    try:
        with (fixture / "orca.err").open("wb") as log:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_helpers["ORCA_WRAPPER"],
                                     str(speech.path)], stdout=subprocess.DEVNULL,
                                    stderr=log, env=os.environ.copy())
        wait_for(lambda: "Screen reader on" in " ".join(speech.take()),
                 "owned Orca startup", seconds=20)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            one(candidate, "list", f"Panel 1, source, {source}")), None),
            "isolated source panel")
        resize_owned(process.pid, 1440, 1080)
        try:
            row = wait_for(lambda: next((node for node in descendants(app)
                if node.get_role_name() == "list item" and
                node.get_name().startswith(name + ", file,")), None),
                "owned source row")
        except AssertionError:
            result["accessibleSnapshot"] = [{"role": node.get_role_name(),
                "name": node.get_name()} for node in descendants(app)
                if node.get_role_name() in ("list", "list item", "button", "frame")][:80]
            result["orcaAliveAtFailure"] = orca.poll() is None
            raise
        if not row.grab_focus():
            raise AssertionError("Could not focus owned source row")
        invoke(wait_for(lambda: one(app, "button", "Move / rename…"), "Move command"),
               "Move command")
        invoke(wait_for(lambda: dialog_button(app, "Move"), "Move approval"),
               "Move approval")
        wait_for(lambda: target_file.is_file() and not source_file.exists(),
                 "exact completed Move", seconds=60)
        if hashlib.sha256(target_file.read_bytes()).hexdigest() != digest:
            raise AssertionError("Move changed fixture bytes")
        undo = wait_for(lambda: one(app, "button", "Undo"), "available unused Undo")
        result["unusedUndoBeforeReview"] = True
        clear = wait_for(lambda: one(app, "button", "Clear finished history…"),
                         "Clear finished history command")
        speech.take()
        invoke(clear, "Clear finished history")
        keep = wait_for(lambda: dialog_button(app, "Keep history"),
                        "Keep history decision")
        review = next((node for node in descendants(app)
                       if node.get_role_name() == "frame" and
                       "Remove operation history" in node.get_name()), None)
        if review is None:
            raise AssertionError("Clear finished history review is not exposed")
        names = [node.get_name() for node in descendants(review)]
        consequence = next((name for name in names if "unused Undo record" in name and
                            "Removing these rows" in name), None)
        if consequence is None:
            raise AssertionError("Review omitted the unused Undo consequence")
        result["reviewConsequence"] = consequence
        result["reviewBounds"] = {"frame": bounds(review), "keep": bounds(keep)}
        if not inside(bounds(review), bounds(keep)):
            raise AssertionError("Keep history decision is clipped")
        # Moving speech focus from the decision back to the consequence forces
        # Orca to announce the entire exposed text in this actual dialog.
        consequence_node = next(node for node in descendants(review)
                                if node.get_name() == consequence)
        if not keep.grab_focus():
            raise AssertionError("Could not focus Keep history")
        time.sleep(0.25)
        speech.take()
        if not consequence_node.grab_focus():
            raise AssertionError("Consequence is not keyboard/AT focusable")
        heard, deadline = [], time.monotonic() + 8
        while time.monotonic() < deadline:
            heard += speech.take()
            if "unused Undo record" in " ".join(heard) and "removes their reports" in " ".join(heard):
                break
            time.sleep(0.15)
        result["consequenceSpeech"] = heard
        if not ("unused Undo record" in " ".join(heard) and
                "removes their reports" in " ".join(heard)):
            raise AssertionError("Orca did not speak the unused Undo/report consequence")
        # AT-SPI action is equivalent to keyboard activation of the focused
        # Keep history decision; it must dismiss review without retiring Undo.
        if not keep.grab_focus():
            raise AssertionError("Could not focus Keep history decision")
        invoke(keep, "Keep history")
        wait_for(lambda: dialog_button(app, "Keep history") is None,
                 "review dismissed")
        result["unusedUndoAfterKeep"] = bool(one(app, "button", "Undo"))
        result["targetBytesIntact"] = (target_file.is_file() and
            hashlib.sha256(target_file.read_bytes()).hexdigest() == digest and
            not source_file.exists())
        if not result["unusedUndoAfterKeep"] or not result["targetBytesIntact"]:
            raise AssertionError("Keep history lost unused Undo or changed files")
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        result["traceback"] = traceback.format_exc()[-2000:]
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    owned.kill()
                    owned.wait(timeout=5)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
