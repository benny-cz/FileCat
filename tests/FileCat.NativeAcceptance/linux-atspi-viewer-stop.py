#!/usr/bin/env python3
"""Cancel a native Ubuntu Hex or Text viewer search over an owned sparse file.

Usage: linux-atspi-viewer-stop.py APP REPORT.json [--text]
Run in the logged-in desktop with AT-SPI, wmctrl and xclip available.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
from uuid import uuid4


common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-viewer-find.py")))
find = common["find"]
descendants = common["descendants"]
wait_for = common["wait_for"]
checked_action = common["checked_action"]
close_viewer = common["close_viewer"]

SENTINEL = "FileCat long-search Stop clipboard sentinel"
LENGTH = 2 * 1024 * 1024 * 1024


def clipboard_text():
    done = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                          capture_output=True, timeout=5)
    return done.stdout.decode("utf-8") if done.returncode == 0 else ""


def set_clipboard(text):
    subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-in"],
                   input=text.encode("utf-8"), stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=5, check=True)


def sampled_source_intact(path):
    if path.stat().st_size != LENGTH:
        return False
    with path.open("rb") as source:
        for offset in (0, LENGTH // 2, LENGTH - 32):
            source.seek(offset)
            if source.read(32) != bytes(32):
                return False
    return True


def run(app_path, text_mode):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise AssertionError("Use an isolated Git-built FileCat below FileCatValidation")
    root = validation / "SearchStopUbuntu" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    test_file = source / "search-stop.bin"
    with test_file.open("xb") as output:
        output.truncate(LENGTH)
    if not sampled_source_intact(test_file):
        raise AssertionError("The sparse search fixture was not created exactly")
    with app_path.open("rb") as binary:
        app_hash = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    if (validation / "dotnet-sdk" / "dotnet").is_file():
        os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    original_clipboard = clipboard_text()
    process = None
    viewer = None
    app = None
    title = "search-stop.bin — FileCat Viewer"
    try:
        set_clipboard(SENTINEL)
        with (root / "app.log").open("wb") as log:
            child_env = os.environ.copy()
            child_env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=child_env, stdout=log,
                                       stderr=subprocess.STDOUT)
            command = Path(f"/proc/{process.pid}/cmdline").read_bytes()
            if os.fsencode(str(app_path)) not in command or os.fsencode(str(source)) not in command:
                raise AssertionError("The long-search app does not own its exact fixture")
            import gi
            gi.require_version("Atspi", "2.0")
            from gi.repository import Atspi
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((node for node in
                           (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                           if node.get_name() == "FileCat" and
                           find(node, "list", f"Panel 1, source, {source}")), None),
                           "isolated long-search app")
            panel = find(app, "list", f"Panel 1, source, {source}")
            row = wait_for(lambda: next((item for item in descendants(panel)
                           if item.get_role_name() == "list item" and
                           (item.get_name() or "").startswith("search-stop.bin, file,")), None),
                           "exact sparse-file row")
            wait_for(lambda: row.grab_focus() and
                     row.get_state_set().contains(Atspi.StateType.FOCUSED),
                     "sparse-file row focus")
            checked_action(find(app, "button", "View"), "View command")
            viewer = wait_for(lambda: find(app, "frame", title), "long-search viewer")
            if text_mode:
                checked_action(find(viewer, "toggle button", "Text"), "Text mode toggle")
                wait_for(lambda: find(viewer, "label", "Mode: Text"), "Text mode status")
            else:
                wait_for(lambda: find(viewer, "label", "Mode: Hex"), "Hex mode status")
            checked_action(find(viewer, "button", "Show or hide viewer Find bar"), "Find toggle")
            scale_info = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True,
                                        text=True, check=True, timeout=5).stdout
            scale_match = re.search(r"\bScale: ([0-9.]+)", scale_info)
            display_scale = float(scale_match.group(1)) if scale_match else None
            query = wait_for(lambda: find(viewer, "entry", "Find in file"), "Find query")
            if not query.set_text_contents("NO_MATCH_SEARCH_STOP"):
                raise AssertionError("AT-SPI could not enter the search query")
            if clipboard_text() != SENTINEL:
                raise AssertionError("Opening Find changed the clipboard")
            checked_action(find(viewer, "button", "Find next match"), "Find Next")
            stop = wait_for(lambda: find(viewer, "button", "Stop search"), "visible Stop control")
            checked_action(stop, "Stop search")
            wait_for(lambda: find(viewer, "label", "Search canceled."), "canceled search status")
            wait_for(lambda: find(viewer, "button", "Stop search") is None,
                     "Stop control to retire")
            if clipboard_text() != SENTINEL:
                raise AssertionError("Stopping search changed the clipboard")
            if not sampled_source_intact(test_file) or list(target.iterdir()):
                raise AssertionError("Stopping search changed the source or target")
            return {"passed": True, "platform": "Ubuntu", "mode": "Text" if text_mode else "Hex",
                    "app": str(app_path), "appSha256": app_hash, "fixture": str(root),
                    "sourceLength": LENGTH, "displayScale": display_scale,
                    "modeActivation": "AT-SPI Text toggle" if text_mode else "default Hex mode",
                    "visibleStopActivated": True,
                    "canceledStatusVisible": True, "stopRetired": True,
                    "clipboardPreserved": True, "sourceAndTargetUnchanged": True}
    finally:
        if viewer is not None and app is not None and process is not None:
            try:
                close_viewer(app, title, process.pid)
            except (AssertionError, subprocess.CalledProcessError):
                pass
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        set_clipboard(original_clipboard)
        if test_file.exists():
            test_file.unlink()


def main():
    if len(sys.argv) not in (3, 4) or len(sys.argv) == 4 and sys.argv[3] != "--text":
        raise SystemExit("Usage: linux-atspi-viewer-stop.py APP REPORT.json [--text]")
    report = Path(sys.argv[2])
    if report.parent.resolve() != (Path.home() / "FileCatValidation").resolve(strict=True):
        raise SystemExit("Write the native report directly below FileCatValidation")
    try:
        result = run(Path(sys.argv[1]), text_mode=len(sys.argv) == 4)
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
