#!/usr/bin/env python3
"""Verify native UTF-8 Text and Hex selection, reflow, Copy, and read-only AT-SPI.

Usage: linux-atspi-viewer-copy-reflow.py APP REPORT.json
Run on the logged-in Ubuntu validation desktop. The runner owns its fixture,
FileCat process, and X11 clipboard sentinel; it never edits the source.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib

HERE = Path(__file__).resolve().parent
common = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
find, wait_for, checked_action, descendants = (common[name] for name in
                                               ("find", "wait_for", "checked_action", "descendants"))
viewer_window_id = common["exact_viewer_window_id"]
send_key = common["send_key"]
hex_value = runpy.run_path(str(HERE / "linux-atspi-hex-resize.py"))["hex_value"]
NAME = "mixed-source.txt"
PHRASE = "café 🍣"
PHRASE_BYTES = PHRASE.encode("utf-8")
CONTENT = ("A" * 180 + " " + PHRASE + " " + "B" * 180 + "\r\n").encode("utf-8")
OFFSET = CONTENT.index(PHRASE_BYTES)
HEX = PHRASE_BYTES.hex().upper()


def clipboard():
    result = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                            capture_output=True, timeout=6)
    return result.stdout if result.returncode == 0 else None


def set_clipboard(value):
    subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-in"],
                   input=value, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                   check=True, timeout=6)


def text_value(viewer):
    control = find(viewer, "entry", "Text viewer")
    if control is None:
        raise AssertionError("The read-only native Text document is missing")
    text = control.get_text_iface()
    if text is None:
        raise AssertionError("The Text document has no accessible text interface")
    return control, Atspi.Text.get_text(text, 0, -1), control.get_description()


def resize(pid, title, dimensions):
    window = viewer_window_id(title, pid)
    subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-b",
                    "remove,maximized_vert,maximized_horz"], check=True, timeout=5)
    subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e",
                    f"0,40,40,{dimensions[0]},{dimensions[1]}"], check=True, timeout=5)
    def settled():
        output = subprocess.run(["/usr/bin/xdotool", "getwindowgeometry", "--shell", window],
                                capture_output=True, text=True, check=True, timeout=5).stdout
        values = dict(line.split("=", 1) for line in output.splitlines() if "=" in line)
        actual = [int(values["WIDTH"]), int(values["HEIGHT"])]
        return actual if all(abs(a - b) <= 35 for a, b in zip(actual, dimensions)) else None
    return wait_for(settled, f"owned viewer resize to {dimensions}", seconds=6)


def run(app_path, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use a Git-built isolated app and a fresh guest-local report")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before the isolated viewer test")
    root = validation / "ViewerCopyReflow" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    file = source / NAME
    file.write_bytes(CONTENT)
    os.environ.update(runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))["desktop_environment"]())
    original_clipboard = None
    try:
        original_clipboard = clipboard()
    except subprocess.TimeoutExpired:
        pass
    environment = os.environ.copy()
    runtime = validation / "dotnet-sdk"
    if not (runtime / "dotnet").is_file():
        raise AssertionError("Isolated .NET runtime is unavailable")
    environment["DOTNET_ROOT"] = str(runtime)
    result = {"passed": False, "stage": "startup", "sourceSha256": hashlib.sha256(CONTENT).hexdigest(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
              "phraseSourceByteOffset": OFFSET, "phraseByteCount": len(PHRASE_BYTES)}
    process = None
    try:
        set_clipboard(b"FileCat native Copy sentinel")
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "exact test-owned FileCat", seconds=35)
        row = wait_for(lambda: next((node for node in descendants(app)
                       if node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith(NAME + ", file,")), None),
                       "test-owned source row")
        if not row.grab_focus():
            raise AssertionError("The source row could not take native focus")
        checked_action(find(app, "button", "View"), "source View")
        title = f"{NAME} — FileCat Viewer"
        viewer = wait_for(lambda: find(app, "frame", title), "test-owned viewer")
        wait_for(lambda: find(viewer, "label", "Mode: Text"), "Text mode")
        result["stage"] = "Text selection and reflow"
        checked_action(find(viewer, "button", "Show or hide viewer Find bar"), "Find toggle")
        query = wait_for(lambda: find(viewer, "entry", "Find in file"), "Find field")
        if not query.get_editable_text().set_text_contents(PHRASE):
            raise AssertionError("Could not set the UTF-8 Text query")
        checked_action(find(viewer, "button", "Find next match"), "Text Find Next")
        # Find highlights and reveals a match, but deliberately does not turn
        # it into an editable selection. Select explicitly through the native
        # keyboard, then verify the full source range including multibyte data.
        control, value, _ = wait_for(
            lambda: state if PHRASE in (state := text_value(viewer))[1] else None,
            "UTF-8 match revealed in the Text viewport")
        if not control.grab_focus():
            raise AssertionError("The Text document could not take native focus")
        send_key(validation / "linux-uinput-key.py", "ctrl+a")
        def text_selection():
            control, value, status = text_value(viewer)
            expected = f"Selected source bytes 0 through {len(CONTENT) - 1}, {len(CONTENT)} bytes"
            return (control, value, status) if PHRASE in value and expected in status else None
        try:
            text_before = wait_for(text_selection, "exact native UTF-8 source selection")
        except AssertionError as error:
            _, value, status = text_value(viewer)
            labels = [node.get_name() for node in descendants(viewer)
                      if node.get_role_name() == "label" and node.get_name()]
            raise AssertionError(f"{error}; value={value[:250]!r}; status={status!r}; "
                                 f"labels={labels[-10:]!r}") from error
        if text_before[0].get_state_set().contains(Atspi.StateType.EDITABLE):
            raise AssertionError("Text viewer was exposed as editable")
        wrap = find(viewer, "check box", "Wrap")
        if wrap is not None and not wrap.get_state_set().contains(Atspi.StateType.CHECKED):
            checked_action(wrap, "Text Wrap")
        for _ in range(2):
            checked_action(find(viewer, "button", "Increase text and hex size"), "larger text")
        result["textSizes"] = {}
        for label, dimensions in (("narrow", [480, 300]), ("wide", [900, 650])):
            result["textSizes"][label] = resize(process.pid, title, dimensions)
            wait_for(text_selection, f"source selection after {label} Text reflow")
        result["textSelectionStatus"] = text_selection()[2]
        if not text_selection()[0].grab_focus():
            raise AssertionError("The selected Text document could not regain focus after resize")
        send_key(validation / "linux-uinput-key.py", "ctrl+c")
        wait_for(lambda: clipboard() == CONTENT, "exact UTF-8 source text on native clipboard")
        result["textClipboardUtf8"] = clipboard().decode("utf-8")
        result["stage"] = "Hex selection and reflow"
        checked_action(find(viewer, "toggle button", "Hex"), "Hex mode")
        wait_for(lambda: find(viewer, "label", "Mode: Hex"), "Hex mode")
        query = wait_for(lambda: find(viewer, "entry", "Find in file"), "Hex Find field")
        hex_bytes = wait_for(lambda: find(viewer, "check box", "Hex bytes"), "Hex byte query choice")
        if not hex_bytes.get_state_set().contains(Atspi.StateType.CHECKED):
            checked_action(hex_bytes, "Hex byte query choice")
        if not query.get_editable_text().set_text_contents(" ".join(
                HEX[index:index + 2] for index in range(0, len(HEX), 2))):
            raise AssertionError("Could not set the owned Hex byte query")
        checked_action(find(viewer, "button", "Find next match"), "Hex Find Next")
        expected = f"Selected bytes {OFFSET} through {OFFSET + len(PHRASE_BYTES) - 1}, {len(PHRASE_BYTES)} bytes"
        wait_for(lambda: value if expected in (value := hex_value(viewer)) else None,
                 "exact native Hex source selection")
        result["hexSizes"] = {}
        for label, dimensions in (("narrow", [480, 300]), ("wide", [900, 650])):
            result["hexSizes"][label] = resize(process.pid, title, dimensions)
            wait_for(lambda: value if expected in (value := hex_value(viewer)) else None,
                     f"source selection after {label} Hex reflow")
        hex_control = find(viewer, "entry", "Hex view")
        if hex_control is None or hex_control.get_state_set().contains(Atspi.StateType.EDITABLE):
            raise AssertionError("Hex view is missing or incorrectly editable")
        result["hexSelectionStatus"] = hex_value(viewer)[:400]
        if not hex_control.grab_focus():
            raise AssertionError("The selected Hex document could not regain focus after resize")
        send_key(validation / "linux-uinput-key.py", "ctrl+c")
        wait_for(lambda: clipboard() == HEX.encode("ascii"), "exact source bytes on native clipboard as hex")
        result["hexClipboard"] = clipboard().decode("ascii")
        if file.read_bytes() != CONTENT or list(target.iterdir()):
            raise AssertionError("Viewer Copy changed the owned source or target")
        result.update({"passed": True, "stage": "complete", "sourceUnchanged": True, "targetEmpty": True})
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["appExit"] = process.poll() if process is not None else None
        if (root / "app.log").is_file():
            result["appLogTail"] = (root / "app.log").read_text(
                encoding="utf-8", errors="replace")[-1200:]
        return result
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        if original_clipboard is not None:
            try:
                set_clipboard(original_clipboard)
            except (OSError, subprocess.SubprocessError):
                pass
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report)
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items()
                      if key not in ("textSelectionStatus", "hexSelectionStatus")}))
    raise SystemExit(0 if outcome["passed"] else 1)
