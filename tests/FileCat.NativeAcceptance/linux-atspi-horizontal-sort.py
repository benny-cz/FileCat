#!/usr/bin/env python3
"""Exercise native horizontal wheel, clipped sort and focused-row speech.

Usage: linux-atspi-horizontal-sort.py APP REPORT.json KEY_HELPER POINTER_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD transiently. Run with no other FileCat/Orca.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def run(app_path: Path, report: Path, key_helper: Path, pointer_helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, pointer_helper = (p.resolve(strict=True)
                                             for p in (app_path, key_helper, pointer_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper != validation / "linux-uinput-key.py" or
            pointer_helper != validation / "linux-uinput-pointer.py" or
            report.parent != validation or report.exists()):
        raise AssertionError("Use an isolated Git app, input helpers and fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient native input credential required")
    for name in ("FileCat", "orca"):
        if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", name],
                          capture_output=True).returncode == 0:
            raise AssertionError(f"Stop other {name} processes before this owned test")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "press", "stop", "desktop_environment"))
    mouse_type = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-hex-resize.py"))["PhysicalMouse"]
    speech_source = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-orca-speech.py"))
    root = validation / "HorizontalSort" / uuid4().hex
    data, source, destination = root / "data", root / "source", root / "destination"
    for folder in (data, source, destination):
        folder.mkdir(parents=True)
    for i, name in enumerate(("alpha.txt", "bravo.txt", "charlie.txt")):
        path = source / name
        path.write_bytes(bytes([65 + i]) * (i + 1))
        timestamp = 1_700_000_000 + i * 60
        os.utime(path, (timestamp, timestamp))
    (data / "settings.json").write_text(json.dumps({"SchemaVersion": 1,
                                                      "FontSize": 20,
                                                      "ShowDriveButtons": False}),
                                       encoding="utf-8")
    original = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.iterdir()}
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "listFontSize": 20}
    process = orca = None
    speech = speech_source["SpeechLog"](root / "orca-debug.log")

    def find(app, role, name):
        return next((node for node in tree(app) if node.get_role_name() == role and
                     node.get_name() == name and
                     node.get_state_set().contains(Atspi.StateType.SHOWING)), None)

    def listing(app):
        return next((node for node in tree(app) if node.get_role_name() == "list" and
                     (node.get_name() or "").startswith("Panel 1,")), None)

    def focused(app):
        node = listing(app)
        return [child.get_name() or "" for child in tree(node)
                if child.get_state_set().contains(Atspi.StateType.FOCUSED)] if node else []

    def row_order(app):
        node = listing(app)
        return [child.get_name().split(",", 1)[0] for child in tree(node)
                if child.get_role_name() == "list item" and
                (child.get_name() or "").split(",", 1)[0] in
                ("alpha.txt", "bravo.txt", "charlie.txt")]

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        orca_env = {**env, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        with (root / "orca.err").open("wb") as orca_error:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_source["ORCA_WRAPPER"],
                                     str(speech.path)], env=orca_env,
                                    stdout=subprocess.DEVNULL, stderr=orca_error)
        wait_for(lambda: [x for x in speech.take() if "screen reader on" in x.casefold()],
                 "owned Orca startup", seconds=20)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(destination)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        wait_for(lambda: listing(app) if listing(app) and str(source) in listing(app).get_name() else None,
                 "source listing")
        display = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                                 capture_output=True, text=True, timeout=10).stdout
        scale_match = re.search(r"\bScale: ([0-9.]+)", display)
        scale = float(scale_match.group(1)) if scale_match else None
        result["displayScale"] = scale
        if scale not in (1.5, 2.0):
            raise AssertionError("The test requires an enlarged native display scale")
        windows = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                                 capture_output=True, text=True, timeout=5).stdout.splitlines()
        window = next((parts[0] for line in windows if
                       len(parts := line.split()) > 2 and parts[2] == str(process.pid)), None)
        if window is None:
            raise AssertionError("The test-owned main window was not in WM")
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e", "0,20,20,1440,1062"],
                       check=True, timeout=5)
        subprocess.run(["/usr/bin/wmctrl", "-ia", window], check=True, timeout=5)
        time.sleep(0.5)
        result["stage"] = "focus the retained item"
        bravo = wait_for(lambda: next((n for n in tree(listing(app))
                                      if n.get_role_name() == "list item" and
                                      (n.get_name() or "").startswith("bravo.txt,")), None),
                         "bravo row")
        if not bravo.grab_focus():
            raise AssertionError("Could not focus the retained row")
        result["focusBefore"] = focused(app)
        result["rowOrderBefore"] = row_order(app)
        result["descriptionBefore"] = listing(app).get_description()
        scroll_nodes = [(n.get_role_name(), n.get_name() or "") for n in tree(app)
                        if "scroll" in n.get_role_name().casefold() or
                        "Listing columns" in (n.get_name() or "")]
        result["scrollCandidates"] = scroll_nodes[:20]
        bar = find(app, "scroll bar", "Listing columns")
        bounds = listing(app).get_component().get_extents(Atspi.CoordType.SCREEN)
        result["listBounds"] = [bounds.x, bounds.y, bounds.width, bounds.height]
        if bar:
            area = bar.get_component().get_extents(Atspi.CoordType.SCREEN)
            result["barBounds"] = [area.x, area.y, area.width, area.height]
            try:
                result["scrollBefore"] = bar.get_current_value()
            except Exception as error:
                result["scrollValueError"] = f"{type(error).__name__}: {error}"
        result["stage"] = "physical horizontal wheel and clipped header"
        speech.take()
        with mouse_type(pointer_helper, sudo_password, scale) as mouse:
            mouse.align(bounds.x + bounds.width // 2, bounds.y + bounds.height // 2)
            mouse.send("SHIFT_DOWN")
            try:
                for _ in range(8):
                    mouse.send("WHEEL -1")
            finally:
                mouse.send("SHIFT_UP")
            time.sleep(0.35)
            result["descriptionAfterWheel"] = listing(app).get_description()
            bar = find(app, "scroll bar", "Listing columns")
            try:
                result["scrollAfter"] = bar.get_current_value() if bar else None
            except Exception as error:
                result["scrollAfterError"] = f"{type(error).__name__}: {error}"
            result["focusAfterWheel"] = focused(app)
            # At 20-point list text and a 720-DIP two-panel layout, the
            # Modified header starts beyond the initial viewport. A full
            # negative horizontal-wheel step brings its center near here.
            mouse.align(bounds.x + min(bounds.width - 35, round(145 * scale)),
                        bounds.y + round(18 * scale))
            mouse.send("DOWN")
            mouse.send("UP")
        result["descriptionAfter"] = wait_for(lambda: listing(app).get_description()
                                               if "Sort: Modified" in listing(app).get_description() else None,
                                               "Modified column sort", seconds=10)
        result["focusAfterSort"] = focused(app)
        result["rowOrderAfter"] = row_order(app)
        result["speechAfterSort"] = speech.take()[-35:]
        press(key_helper, sudo_password, "down", process.pid)
        press(key_helper, sudo_password, "up", process.pid)
        time.sleep(0.8)
        result["focusAfterKeyboardRoundTrip"] = focused(app)
        result["speechAfterKeyboardRoundTrip"] = speech.take()[-25:]
        result["sourceExact"] = original == {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                              for p in source.iterdir()}
        result["targetEmpty"] = not any(destination.iterdir())
        spoke_item = " ".join(result["speechAfterKeyboardRoundTrip"]).casefold()
        result["passed"] = ("bravo.txt" in " ".join(result["focusAfterSort"]) and
                            "bravo.txt" in " ".join(result["focusAfterKeyboardRoundTrip"]) and
                            result["rowOrderBefore"] == ["alpha.txt", "bravo.txt", "charlie.txt"] and
                            result["rowOrderAfter"] == ["charlie.txt", "bravo.txt", "alpha.txt"] and
                            "bravo.txt" in spoke_item and "modified" in spoke_item and
                            result["sourceExact"] and result["targetEmpty"])
        result["stage"] = "complete"
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["appExit"] = process.poll() if process else None
        result["speechAtFailure"] = speech.take()[-20:]
        return result
    finally:
        stop(process)
        if orca and orca.poll() is None:
            orca.terminate()
            try:
                orca.wait(timeout=8)
            except subprocess.TimeoutExpired:
                orca.kill()
                orca.wait(timeout=8)
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report_path, Path(sys.argv[3]), Path(sys.argv[4]))
    report_path.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(outcome))
    raise SystemExit(0 if outcome["passed"] else 1)
