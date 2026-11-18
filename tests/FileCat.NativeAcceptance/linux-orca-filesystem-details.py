#!/usr/bin/env python3
"""Exercise FileCat's details window through Orca and XTEST keys on an isolated Xvfb display.

Build FileCat in Release in an owned checkout below ~/FileCatValidation, then run:
FILECAT_ORCA_ISOLATED_DISPLAY=1 dbus-run-session -- xvfb-run -a /usr/bin/python3 \
    linux-orca-filesystem-details.py CHECKOUT REPORT.json

The report destination must be below ~/FileCatValidation. The runner reads Orca's actual
speech log, sets only the owned top-level window focus, and keeps the fixture and log.
"""

import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import time

def main():
    checkout = Path(sys.argv[1]).resolve(strict=True)
    report_path = Path(sys.argv[2]).resolve()
    approved = (Path.home() / "FileCatValidation").resolve(strict=True)
    if approved not in checkout.parents or approved not in report_path.parents:
        raise ValueError("Use a dedicated checkout and result below ~/FileCatValidation.")
    if os.environ.get("FILECAT_ORCA_ISOLATED_DISPLAY") != "1" or not os.environ.get("DISPLAY"):
        raise RuntimeError("Run inside the dedicated Xvfb and D-Bus session.")
    native = checkout / "tests" / "FileCat.NativeAcceptance"
    app = checkout / "src" / "FileCat.App" / "bin" / "Release" / "net10.0" / "FileCat"
    helpers = runpy.run_path(str(native / "linux-orca-speech.py"))
    os.environ.setdefault("DOTNET_ROOT", str(Path.home() / ".dotnet"))
    for name in ("FileCat", "orca"):
        running = subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", name], capture_output=True)
        if running.returncode == 0:
            raise RuntimeError(f"An unrelated {name} process is already active.")

    fixture = Path(tempfile.mkdtemp(prefix="details-orca-", dir=report_path.parent))
    source = fixture / "source"
    target = fixture / "target"
    data = fixture / "state"
    for directory in (source, target, data):
        directory.mkdir()
    focused = source / "alpha-details.txt"
    other = source / "beta-not-selected.txt"
    focused.write_text("FILECAT-DATA-PAYLOAD-CANARY", encoding="utf-8")
    other.write_text("another item", encoding="utf-8")
    os.setxattr(focused, "user.filecat-details", b"FILECAT-XATTR-PAYLOAD-CANARY")
    sha256 = helpers["sha256"]
    expected_digest = sha256(focused)
    speech = helpers["SpeechLog"](fixture / "orca-debug.log")
    evidence = dict(passed=False, appDllSha256=sha256(app.with_name("FileCat.dll")), fixture=str(fixture),
                    speechLog=str(speech.path), input="XTEST on dedicated Xvfb display", steps=[])
    orca = application = None
    os.environ["GSETTINGS_BACKEND"] = "memory"
    os.environ["XDG_CONFIG_HOME"] = str(fixture / "xdg-config")
    os.environ.pop("WAYLAND_DISPLAY", None)

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

    def key(chord):
        keys = {"down": "Down", "ctrl+alt+enter": "ctrl+alt+Return", "tab": "Tab", "space": "space", "escape": "Escape"}
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

    try:
        evidence["orca"] = subprocess.check_output(["/usr/bin/orca", "--version"], text=True, timeout=10).strip()
        with (fixture / "orca.err").open("wb") as errors:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", helpers["ORCA_WRAPPER"], str(speech.path)],
                                    env={**os.environ, "GDK_BACKEND": "x11"},
                                    stdout=subprocess.DEVNULL, stderr=errors)
        hear("Screen reader starts", "Screen reader on", seconds=20)
        with (fixture / "app.log").open("wb") as app_log:
            application = subprocess.Popen([str(app), "--new-instance", "--data", str(data), "--left", str(source),
                                            "--right", str(target)], cwd=fixture, stdout=app_log, stderr=subprocess.STDOUT)
        app_window = wait(lambda: window_named("source"), "the native FileCat window", seconds=40)
        focus(app_window)
        hear("Native file panel announces its starting row", "Go to parent location")
        key("down")
        hear("Keyboard focuses the selected file", focused.name)
        key("ctrl+alt+enter")
        details_window = wait(lambda: window_named("File-system details"), "the separate details window")
        focus(details_window)
        hear("Details window opens from the keyboard", "File-system details")
        focus_button("Read full record")
        key("space")
        hear("Orca announces the full record", "Full · observation")
        focus_button("Copy all")
        key("space")

        def copied_metadata():
            result = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                                    capture_output=True, text=True, timeout=5)
            if result.returncode == 0 and str(focused) in result.stdout and "Full record" in result.stdout:
                return result.stdout
            return None

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
        key("escape")
        wait(lambda: window_named("File-system details") is None, "the details window to close")
        focus(app_window)
        hear("Focus returns to the selected row", focused.name)
        assert sha256(focused) == expected_digest and os.getxattr(focused, "user.filecat-details") == b"FILECAT-XATTR-PAYLOAD-CANARY"
        evidence["passed"] = True
    except Exception as error:
        evidence["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        for process in (application, orca):
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(5)
        report_path.write_text(json.dumps(evidence, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
