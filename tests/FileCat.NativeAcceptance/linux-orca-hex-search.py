#!/usr/bin/env python3
"""Assert Orca speaks Hex match counts during the native Ubuntu Hex journey.

Usage: linux-orca-hex-search.py APP_PATH REPORT.json HEX_RUNNER KEY_HELPER ORCA_RUNNER
Run on the logged-in Ubuntu desktop with FILECAT_NATIVE_SUDO_PASSWORD supplied
only to this process for the physical-key helper. All inputs and output must
stay under ~/FileCatValidation. The source fixture is disposable and owned.
"""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
from uuid import uuid4


EXPECTED = ("1 distinct match visited", "2 distinct matches visited",
            "1 distinct match visited")


def owned_path(value, validation, name=None):
    path = Path(value).resolve(strict=True)
    if validation not in path.parents or name is not None and path.name != name:
        raise AssertionError(f"The {name or 'input'} must be below ~/FileCatValidation")
    return path


def load_speech_reader(path):
    spec = importlib.util.spec_from_file_location("filecat_orca_speech", path)
    if spec is None or spec.loader is None:
        raise AssertionError("The owned Orca speech reader is unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.ORCA_WRAPPER, module.SpeechLog


def windows_of(pid, env):
    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                             capture_output=True, text=True, timeout=5, env=env).stdout
    return [line.split()[0] for line in listing.splitlines()
            if len(line.split()) > 2 and line.split()[2] == str(pid)]


def desktop_xauthority(uid, runtime):
    found = subprocess.run(["/usr/bin/pgrep", "-u", str(uid), "-x", "Xwayland"],
                           capture_output=True, text=True, timeout=5)
    pids = found.stdout.split()
    if len(pids) != 1:
        raise AssertionError("Expected one logged-in Xwayland desktop")
    command = Path(f"/proc/{pids[0]}/cmdline").read_bytes().split(b"\0")
    if b"-auth" not in command:
        raise AssertionError("The logged-in desktop has no X authorization file")
    index = command.index(b"-auth")
    if index + 1 >= len(command):
        raise AssertionError("The desktop X authorization path is missing")
    auth = Path(os.fsdecode(command[index + 1])).resolve(strict=True)
    if auth.parent != runtime or auth.stat().st_uid != uid:
        raise AssertionError("The desktop X authorization file is outside this user session")
    return auth


def main():
    if len(sys.argv) != 6:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app = owned_path(sys.argv[1], validation, "FileCat")
    report_path = Path(sys.argv[2]).resolve()
    hex_runner = owned_path(sys.argv[3], validation, "linux-atspi-hex-editor-find.py")
    key_helper = owned_path(sys.argv[4], validation, "linux-uinput-key.py")
    orca_runner = owned_path(sys.argv[5], validation, "linux-orca-speech.py")
    if report_path.parent != validation or report_path.exists():
        raise SystemExit("Use a fresh report file in ~/FileCatValidation")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise SystemExit("A transient guest sudo credential is required for physical input")
    for process_name in ("orca", "FileCat"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", process_name],
                          capture_output=True).returncode == 0:
            raise SystemExit(f"Another {process_name} process is running")

    wrapper, SpeechLog = load_speech_reader(orca_runner)
    fixture = validation / "HexEditorFind" / uuid4().hex
    native_report = fixture / "native.json"
    seeded = subprocess.run(["/usr/bin/python3", str(hex_runner), "seed", str(fixture)],
                            capture_output=True, text=True, timeout=30)
    if seeded.returncode:
        raise AssertionError(f"Could not seed the owned Hex fixture: {seeded.stderr[-500:]}")
    speech = SpeechLog(fixture / "orca-debug.log")
    result = {"passed": False, "fixture": str(fixture),
              "appDllSha256": hashlib.sha256(app.with_name("FileCat.dll").read_bytes()).hexdigest(),
              "nativeReport": str(native_report), "speechLog": str(speech.path),
              "expectedSpeech": EXPECTED}
    env = os.environ.copy()
    env.setdefault("DISPLAY", ":0")
    env.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    env.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={env['XDG_RUNTIME_DIR']}/bus")
    runtime = Path(env["XDG_RUNTIME_DIR"]).resolve(strict=True)
    env["XAUTHORITY"] = str(desktop_xauthority(os.getuid(), runtime))
    wayland = runtime / "wayland-0"
    if wayland.stat().st_uid != os.getuid() or not stat.S_ISSOCK(wayland.stat().st_mode):
        raise AssertionError("The logged-in Wayland display is unavailable")
    env["WAYLAND_DISPLAY"] = wayland.name
    env["XDG_SESSION_TYPE"] = "wayland"
    orca = process = None
    try:
        orca = subprocess.Popen(["/usr/bin/python3", "-c", wrapper, str(speech.path)],
                                env=env, stdout=subprocess.DEVNULL,
                                stderr=(fixture / "orca.err").open("wb"))
        deadline = time.monotonic() + 20
        while not any("Screen reader on" in phrase for phrase in speech.take()):
            if time.monotonic() > deadline or orca.poll() is not None:
                raise AssertionError("Orca did not start within 20 seconds")
            time.sleep(0.2)
        with (fixture / "app.log").open("wb") as app_log:
            process = subprocess.Popen([str(app), "--new-instance", "--data", str(fixture / "data"),
                                        "--left", str(fixture / "source"),
                                        "--right", str(fixture / "destination")],
                                       cwd=fixture, env=env, stdout=app_log,
                                       stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 40
        while len(windows_of(process.pid, env)) != 1:
            if time.monotonic() > deadline or process.poll() is not None:
                raise AssertionError("The isolated FileCat window did not appear")
            time.sleep(0.25)
        time.sleep(2)
        check_env = env.copy()
        check_env["FILECAT_NATIVE_SUDO_PASSWORD"] = password
        checked = subprocess.run(["/usr/bin/python3", str(hex_runner), "check",
                                  str(fixture), str(native_report), str(key_helper), "--scaled"],
                                 env=check_env, capture_output=True, text=True, timeout=240)
        check_env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
        if not native_report.is_file():
            raise AssertionError(f"The native Hex runner wrote no report: {checked.stderr[-500:]}")
        native = json.loads(native_report.read_text(encoding="utf-8"))
        result["nativePassed"] = checked.returncode == 0 and native.get("passed") is True
        result["nativeSourceIntact"] = native.get("sourceIntact")
        result["nativeDisplayScale"] = native.get("displayScale")
        result["orcaExitAfterNative"] = orca.poll()
        if not result["nativePassed"]:
            raise AssertionError(f"The native Hex journey failed: {native.get('error', checked.stderr[-500:])}")
        if orca.poll() is not None:
            raise AssertionError(f"Orca exited during the native Hex journey ({orca.returncode})")
        time.sleep(2)
        phrases = speech.take()
        hits = []
        cursor = 0
        for expected in EXPECTED:
            pattern = re.compile(r"(?<!\d)" + re.escape(expected) + r"(?!\d)")
            found = next((index for index in range(cursor, len(phrases))
                          if pattern.search(phrases[index])), None)
            if found is None:
                raise AssertionError(f"Orca did not speak {expected!r} in order")
            hits.append({"expected": expected, "logIndex": found, "spoken": phrases[found]})
            cursor = found + 1
        result["spokenMatches"] = hits
        result["passed"] = True
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        for name, owned in (("app", process), ("orca", orca)):
            if owned is not None:
                if owned.poll() is None:
                    owned.terminate()
                    try:
                        owned.wait(10)
                    except subprocess.TimeoutExpired:
                        owned.kill()
                        owned.wait(5)
                result[name + "Stopped"] = owned.poll() is not None
        if not result.get("appStopped") or not result.get("orcaStopped"):
            result["passed"] = False
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
