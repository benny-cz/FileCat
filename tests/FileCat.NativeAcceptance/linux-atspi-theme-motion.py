#!/usr/bin/env python3
"""Stage native GNOME motion frames for one owned Psychedelic FileCat window.

Usage: linux-atspi-theme-motion.py GIT_APP REPORT.json
At each READY line, capture two guest-screen frames, then create the printed
.done marker. The script restores GNOME's original animation setting and
removes its disposable files before exit. The host judges pixel evidence.
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
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
common = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
wait_for, descendants = common["wait_for"], common["descendants"]
desktop_environment = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))["desktop_environment"]
SCHEMA = "org.gnome.desktop.interface"
KEY = "enable-animations"


def settings(env, value=None):
    command = ["/usr/bin/gsettings", "get" if value is None else "set", SCHEMA, KEY]
    if value is not None:
        command.append(value)
    result = subprocess.run(command, env=env, text=True, capture_output=True, timeout=8)
    if result.returncode:
        raise AssertionError(f"GNOME motion setting failed: {result.stderr[-240:]}")
    return result.stdout.strip() if value is None else value


def run(app_path, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents or \
            report_path.parent != validation or report_path.exists():
        raise AssertionError("Use a Git-built app and a fresh guest-local receipt")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Another FileCat is already running")
    root = validation / "ThemeMotion" / uuid4().hex
    source, target, data = (root / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / "visible.txt").write_text("VISIBLE CONTENT STAYS FIXED\n", encoding="utf-8")
    (source / ".hidden.txt").write_text("Hidden name stays legible\n", encoding="utf-8")
    (data / "settings.json").write_text(json.dumps({"SchemaVersion": 1,
        "Theme": "Psychedelic", "ThemeAnimations": True}), encoding="utf-8")
    env = os.environ.copy()
    env.update(desktop_environment())
    shell_pids = subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "gnome-shell"],
                                capture_output=True, text=True, timeout=5).stdout.split()
    if len(shell_pids) != 1:
        raise AssertionError("Expected one logged-in GNOME shell")
    shell_env = dict(part.split("=", 1) for part in
                     Path(f"/proc/{shell_pids[0]}/environ").read_bytes().decode(
                         encoding="utf-8", errors="strict").split("\0") if "=" in part)
    desktop_name = shell_env.get("XDG_CURRENT_DESKTOP", "")
    if "GNOME" not in desktop_name.split(":") or shell_env.get("XDG_SESSION_TYPE") != "wayland":
        raise AssertionError("The logged-in display is not the supported GNOME Wayland session")
    env["XDG_CURRENT_DESKTOP"] = desktop_name
    env["XDG_SESSION_TYPE"] = "wayland"
    env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    original = settings(env)
    if original not in ("true", "false"):
        raise AssertionError(f"Unexpected GNOME motion setting: {original!r}")
    result = {"passed": False, "appDllSha256": hashlib.sha256(
              (app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
              "originalGnomeAnimations": original, "desktop": desktop_name,
              "fixture": str(root), "phases": []}
    process = None
    try:
        settings(env, "true")
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid), None),
                       "owned FileCat accessibility root", seconds=35)
        wait_for(lambda: any("visible.txt" in (node.get_name() or "") for node in descendants(app)),
                 "owned visible file row", seconds=25)
        window = wait_for(lambda: next((parts[0] for line in
                          subprocess.run(["/usr/bin/wmctrl", "-lp"], env=env,
                                         text=True, capture_output=True, timeout=5).stdout.splitlines()
                          if (parts := line.split(maxsplit=4)) and len(parts) == 5 and
                          parts[2] == str(process.pid)), None), "owned window ID")
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e", "0,100,75,1000,660"],
                       env=env, check=True, timeout=8)
        for phase, desired in (("moving", "true"), ("reduced", "false"),
                               ("resumed", "true")):
            settings(env, desired)
            time.sleep(6)  # The live poll is three seconds plus bounded provider read.
            if process.poll() is not None:
                raise AssertionError(f"FileCat exited before {phase} capture")
            observed = settings(env)
            if observed != desired:
                raise AssertionError(f"GNOME refused {phase} motion setting")
            ready = report_path.with_name(report_path.stem + f".{phase}.ready")
            done = report_path.with_name(report_path.stem + f".{phase}.done")
            ready.write_text(phase + "\n", encoding="utf-8")
            result["phases"].append({"name": phase, "gnomeAnimations": observed,
                                     "windowId": window})
            print(f"READY {phase} {done}", flush=True)
            deadline = time.monotonic() + 240
            while not done.exists():
                if process.poll() is not None or time.monotonic() >= deadline:
                    raise AssertionError(f"The {phase} screenshot handoff expired")
                time.sleep(0.2)
            ready.unlink(missing_ok=True)
            done.unlink(missing_ok=True)
        if (source / "visible.txt").read_text(encoding="utf-8") != "VISIBLE CONTENT STAYS FIXED\n" or \
                list(target.iterdir()):
            raise AssertionError("Theme/motion check changed the owned file fixture")
        result["sourceUnchanged"] = True
        result["targetEmpty"] = True
        result["passed"] = True
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        settings(env, original)
        result["restoredGnomeAnimations"] = settings(env)
        for phase in ("moving", "reduced", "resumed"):
            report_path.with_name(report_path.stem + f".{phase}.ready").unlink(missing_ok=True)
            report_path.with_name(report_path.stem + f".{phase}.done").unlink(missing_ok=True)
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    receipt = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), receipt)
    receipt.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(outcome), flush=True)
    raise SystemExit(0 if outcome["passed"] else 1)
