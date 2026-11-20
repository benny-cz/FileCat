#!/usr/bin/env python3
"""Exercise FileCat's details window through Orca and XTEST keys on an isolated Xvfb display.

Build FileCat in Release in an owned checkout below ~/FileCatValidation, then run:
FILECAT_ORCA_ISOLATED_DISPLAY=1 dbus-run-session -- xvfb-run -a /usr/bin/python3 \
    linux-orca-filesystem-details.py CHECKOUT REPORT.json [--link-target | --find-results] [--report-size WIDTH HEIGHT] [--properties]

The report destination must be below ~/FileCatValidation. The runner reads Orca's actual
speech log, sets only the owned top-level window focus, and keeps the fixture and log.
"""

import argparse
import json
import os
from pathlib import Path
import runpy
import subprocess
import tempfile
import time

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkout", type=Path)
    parser.add_argument("report", type=Path)
    origin_group = parser.add_mutually_exclusive_group()
    origin_group.add_argument("--link-target", action="store_true")
    origin_group.add_argument("--find-results", action="store_true")
    parser.add_argument("--report-size", nargs=2, type=int, metavar=("WIDTH", "HEIGHT"))
    parser.add_argument("--properties", action="store_true")
    args = parser.parse_args()
    checkout = args.checkout.resolve(strict=True)
    report_path = args.report.resolve()
    approved = (Path.home() / "FileCatValidation").resolve(strict=True)
    if approved not in checkout.parents or approved not in report_path.parents:
        raise ValueError("Use a dedicated checkout and result below ~/FileCatValidation.")
    if os.environ.get("FILECAT_ORCA_ISOLATED_DISPLAY") != "1" or not os.environ.get("DISPLAY"):
        raise RuntimeError("Run inside the dedicated Xvfb and D-Bus session.")
    native = checkout / "tests" / "FileCat.NativeAcceptance"
    app = checkout / "src" / "FileCat.App" / "bin" / "Release" / "net10.0" / "FileCat"
    helpers = runpy.run_path(str(native / "linux-orca-speech.py"))
    os.environ.setdefault("DOTNET_ROOT", str(Path.home() / ".dotnet"))
    for name in ("dotnet", "testhost", "FileCat", "orca"):
        running = subprocess.run(["/usr/bin/pgrep", "-x", name], capture_output=True)
        if running.returncode == 0:
            raise RuntimeError(f"An unrelated {name} process is already active.")

    fixture = Path(tempfile.mkdtemp(prefix="details-orca-", dir=report_path.parent))
    source = fixture / "source"
    target = fixture / "target"
    data = fixture / "state"
    for directory in (target, data):
        directory.mkdir()
    if args.link_target:
        destination = fixture / "actual-parent"
        subfolder = destination / "subfolder"
        subfolder.mkdir(parents=True)
        source.symlink_to(subfolder, target_is_directory=True)
        observed_file = destination / "landed.txt"
        (fixture / observed_file.name).write_text("unexpected lexical target", encoding="utf-8")
        focused = source / "alpha-link"
        stored_target = f"../{observed_file.name}"
        focused.symlink_to(stored_target)
    else:
        source.mkdir()
        focused = observed_file = source / "alpha-details.txt"
    other = source / "beta-not-selected.txt"
    observed_file.write_text("FILECAT-DATA-PAYLOAD-CANARY", encoding="utf-8")
    other.write_text("another item", encoding="utf-8")
    os.setxattr(observed_file, "user.filecat-details", b"FILECAT-XATTR-PAYLOAD-CANARY")
    sha256 = helpers["sha256"]
    expected_digest = sha256(observed_file)
    speech = helpers["SpeechLog"](fixture / "orca-debug.log")
    evidence = dict(passed=False, appDllSha256=sha256(app.with_name("FileCat.dll")), fixture=str(fixture),
                    propertiesShortcut=args.properties, findResults=args.find_results,
                    speechLog=str(speech.path), input="XTEST on dedicated Xvfb display", steps=[])
    dispatcher = orca = application = None
    speech_runtime = tempfile.TemporaryDirectory(prefix="filecat-details-speech-", dir="/tmp")
    os.environ["GSETTINGS_BACKEND"] = "memory"
    os.environ["XDG_CONFIG_HOME"] = str(fixture / "xdg-config")
    os.environ.pop("WAYLAND_DISPLAY", None)
    os.environ.pop("AT_SPI_BUS_ADDRESS", None)

    def wait(check, description, seconds=12):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            result = check()
            if result:
                return result
            time.sleep(0.15)
        raise AssertionError(f"Timed out waiting for {description}")

    def hear(label, text, previous=(), seconds=8):
        phrases = list(previous)
        def heard():
            if orca.poll() is not None:
                raise RuntimeError(f"Orca stopped during {label}")
            phrases.extend(speech.take())
            return any(text.casefold() in phrase.casefold() for phrase in phrases)
        try:
            wait(heard, label, seconds)
        finally:
            evidence["steps"].append(dict(step=label, heard=phrases))

    def windows():
        result = subprocess.run(["/usr/bin/xdotool", "search", "--onlyvisible", "--pid", str(application.pid)],
                                capture_output=True, text=True, timeout=5)
        return result.stdout.splitlines() if result.returncode == 0 else []

    def window_named(text):
        for window in windows():
            result = subprocess.run(["/usr/bin/xdotool", "getwindowname", window], capture_output=True, text=True, timeout=5)
            if result.returncode == 0 and text.casefold() in result.stdout.casefold():
                return window
        return None

    def focus(window):
        if window not in windows():
            raise AssertionError("The requested focus is outside the owned FileCat windows.")
        subprocess.run(["/usr/bin/xdotool", "windowfocus", "--sync", window], check=True, timeout=5)

    def size_report(window):
        if args.report_size is None:
            return
        width, height = map(str, args.report_size)
        subprocess.run(["/usr/bin/xdotool", "windowsize", "--sync", window, width, height], check=True, timeout=5)

        def at_size():
            geometry = subprocess.check_output(["/usr/bin/xdotool", "getwindowgeometry", "--shell", window],
                                               text=True, timeout=5).splitlines()
            return f"WIDTH={width}" in geometry and f"HEIGHT={height}" in geometry

        wait(at_size, "the requested report size")
        evidence["reportSize"] = args.report_size

    def key(chord):
        keys = {"home": "Home", "down": "Down", "enter": "Return", "alt+f7": "alt+F7",
                "ctrl+alt+enter": "ctrl+alt+Return", "alt+enter": "alt+Return",
                "tab": "Tab", "space": "space", "escape": "Escape"}
        current = subprocess.run(["/usr/bin/xdotool", "getwindowfocus"], check=True, capture_output=True, text=True, timeout=5)
        if current.stdout.strip() not in windows():
            raise AssertionError("The focused X11 window is outside the owned FileCat windows.")
        subprocess.run(["/usr/bin/xdotool", "key", "--delay", "50", keys[chord]], check=True, timeout=10)

    def focus_button(label):
        spoken = []
        speech.take()
        for _ in range(26):
            key("tab")
            limit = time.monotonic() + 1.5
            while time.monotonic() < limit:
                spoken.extend(speech.take())
                if any(label.casefold() in phrase.casefold() for phrase in spoken):
                    evidence["steps"].append(dict(step=f"Tab reaches {label}", heard=spoken))
                    return
                time.sleep(0.12)
        evidence["steps"].append(dict(step=f"Tab reaches {label}", heard=spoken))
        raise AssertionError(f"Orca never spoke the {label} action")

    def clipboard():
        result = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                                capture_output=True, text=True, timeout=5)
        return result.stdout if result.returncode == 0 else ""

    def open_find_result():
        from gi import require_version
        require_version("Atspi", "2.0")
        from gi.repository import Atspi
        tree = runpy.run_path(str(native / "linux-atspi-sftp-delayed-listing.py"))["tree"]
        key("alt+f7")
        find_window = wait(lambda: window_named("Find files"), "the standalone Find window")
        focus(find_window)
        desktop = Atspi.get_desktop(0)

        def find_form():
            for index in range(desktop.get_child_count()):
                app_node = desktop.get_child_at_index(index)
                if app_node is None or app_node.get_process_id() != application.pid:
                    continue
                return next((node for node in tree(app_node) if node.get_role_name() in ("frame", "window", "dialog")
                             and node.get_name() == "Find files"), None)
            return None

        form = wait(find_form, "the owned accessible Find window")
        def entry(name):
            return wait(lambda: next((node for node in tree(form) if node.get_role_name() == "entry"
                                      and node.get_name() == name), None), f"the accessible {name} field")
        names = entry("Names")
        look_in = entry("Look in")
        if not look_in.get_editable_text().set_text_contents(str(source)) or not names.get_editable_text().set_text_contents(focused.name):
            raise AssertionError("Could not set the original Find scope and name.")
        if not names.get_component().grab_focus():
            raise AssertionError("Could not focus the Find Names field.")
        key("enter")
        row = wait(lambda: next((node for node in tree(form) if node.get_role_name() == "list item"
                                 and (node.get_name() or "").startswith(focused.name + ",")), None),
                   "the original file in standalone Find results")
        if not row.get_parent().get_component().grab_focus():
            raise AssertionError("Could not focus the Find results list.")
        speech.take()
        key("home")
        hear("Orca speaks the standalone Find result", focused.name)
        return find_window

    try:
        evidence["orca"] = subprocess.check_output(["/usr/bin/orca", "--version"], text=True, timeout=10).strip()
        runtime = Path(speech_runtime.name)
        socket = runtime / "speech.sock"
        speech_config = Path(os.environ["XDG_CONFIG_HOME"]) / "speech-dispatcher"
        speech_config.mkdir(parents=True)
        # Synthesize through the real dispatcher without taking over the desktop's sound device.
        (speech_config / "speechd.conf").write_text('AudioOutputMethod "alsa"\nAudioALSADevice "null"\n', encoding="utf-8")
        with (fixture / "speech-dispatcher.log").open("wb") as dispatcher_log:
            dispatcher = subprocess.Popen(["/usr/bin/speech-dispatcher", "--run-single", "--timeout", "0", "--pid-file",
                                           str(runtime / "speech.pid"), "--communication-method", "unix_socket",
                                           "--socket-path", str(socket), "--log-dir", str(fixture), "--config-dir", str(speech_config)],
                                          stdout=dispatcher_log, stderr=subprocess.STDOUT)
        wait(socket.is_socket, "the dedicated speech socket", seconds=8)
        with (fixture / "orca.err").open("wb") as errors:
            isolated_wrapper = helpers["ORCA_WRAPPER"].replace(', "--replace"', "")
            orca = subprocess.Popen(["/usr/bin/python3", "-c", isolated_wrapper, str(speech.path)],
                                    env={**os.environ, "GDK_BACKEND": "x11", "SPEECHD_ADDRESS": f"unix_socket:{socket}"},
                                    stdout=subprocess.DEVNULL, stderr=errors)
        hear("Screen reader starts", "Screen reader on", seconds=20)
        with (fixture / "app.log").open("wb") as app_log:
            application = subprocess.Popen([str(app), "--new-instance", "--data", str(data), "--left", str(source),
                                            "--right", str(target)], cwd=fixture, stdout=app_log, stderr=subprocess.STDOUT)
        app_window = wait(lambda: window_named("source"), "the native FileCat window", seconds=40)
        focus(app_window)
        hear("Native file panel opens on the owned source", "Panel 1, source")
        key("home")
        key("down")
        hear("Keyboard focuses the selected file", focused.name)
        origin_window = open_find_result() if args.find_results else app_window
        key("alt+enter" if args.properties else "ctrl+alt+enter")
        details_window = wait(lambda: window_named("File-system details"), "the separate details window")
        size_report(details_window)
        focus(details_window)
        hear("Details window opens from the keyboard", "File-system details")
        if args.link_target:
            focus_button("Copy stored target")
            key("space")
            wait(lambda: clipboard() == stored_target, "the exact relative target on the clipboard")
            assert window_named("actual-parent") is None
            focus_button("Open target location")
            key("space")
            expected_window = wait(lambda: window_named(destination.name), "the real target folder tab")
            assert expected_window == app_window
            focus(app_window)
            hear("Workspace selects the file in the resolved folder", observed_file.name)
            assert sha256(observed_file) == expected_digest and os.readlink(focused) == stored_target
            evidence.update(passed=True, storedTarget=stored_target, destination=str(destination))
            return
        focus_button("Read full record")
        key("space")
        hear("Orca announces the full record", "Full · observation")
        focus_button("Copy all")
        key("space")

        def copied_metadata():
            copied = clipboard()
            return copied if str(focused) in copied and "Full record" in copied else None

        copied = wait(copied_metadata, "the full record on the clipboard")
        assert "metadata only" in copied
        assert "supplementary-data values and previews" in copied
        assert "FILECAT-DATA-PAYLOAD-CANARY" not in copied and "FILECAT-XATTR-PAYLOAD-CANARY" not in copied
        assert other.name not in copied
        evidence["fullReportClipboardBytes"] = len(copied.encode("utf-8"))
        focus_button("Show summary")
        key("space")
        hear("Orca announces the summary", "Summary · observation")
        focus_button("Show full record")
        if args.find_results:
            key("space")
            hear("Orca announces the cached full observation", "Full · observation")
        key("escape")
        wait(lambda: window_named("File-system details") is None, "the details window to close")
        if args.find_results:
            restored = wait(lambda: window_named("Find:"), "the retained standalone Find results window")
            if restored != origin_window:
                raise AssertionError("The original Find window was replaced.")
            evidence["findResultWindowRetained"] = True
        focus(origin_window)
        hear("Focus returns to the selected row", focused.name)
        assert sha256(focused) == expected_digest and os.getxattr(focused, "user.filecat-details") == b"FILECAT-XATTR-PAYLOAD-CANARY"
        evidence["passed"] = True
    except Exception as error:
        evidence["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        for process in (application, orca, dispatcher):
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(5)
        speech_runtime.cleanup()
        report_path.write_text(json.dumps(evidence, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
