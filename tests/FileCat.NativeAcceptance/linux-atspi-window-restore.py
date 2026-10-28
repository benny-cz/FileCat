#!/usr/bin/env python3
"""The main window's normal bounds, maximized state and a stranded position across reopening, under GNOME's mutter.

Usage: linux-atspi-window-restore.py APP_PATH REPORT.json
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other
FileCat running. The window manager does what its own controls do (wmctrl's EWMH requests: move and resize,
maximize, unmaximize, close), and FileCat's own AT-SPI tree gives the controls' extents.

1. Given normal bounds, maximized and unmaximized, the window returns to those bounds.
2. Closed maximized, it reopens maximized, and unmaximized it has the same normal bounds.
3. Pushed off the right of the screen (where a second monitor would be), it is kept on the screen by mutter itself;
   so the saved position is then put beyond the screen's right edge, as a monitor that went away leaves it, and the
   window reopens wholly on the screen.
Every time, the window lies inside the work area and the menu, command search, both panels, the command line and
the twelve keys of the key bar lie inside the window.
"""

import json
import os
import re
from pathlib import Path
import runpy
import subprocess
import sys
import time
from uuid import uuid4

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
base = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))
wait_for, tree, desktop_environment, stop = (base[name] for name in ("wait_for", "tree", "desktop_environment", "stop"))
KEYS = ("Keyboard reference", "Rename in place", "View", "Edit", "Copy…", "Move / rename…", "Create folder…",
        "Delete…", "User commands…", "Menu bar", "Maximize or restore panel", "Focus panel…")


def run(*args):
    return subprocess.run(list(args), capture_output=True, text=True, timeout=10).stdout


def geometry(window):
    # The client area in root coordinates, as xwininfo reports it.
    info = run("/usr/bin/xwininfo", "-id", window)
    value = {}
    for line in info.splitlines():
        line = line.strip()
        for key, name in (("Absolute upper-left X:", "x"), ("Absolute upper-left Y:", "y"), ("Width:", "w"), ("Height:", "h")):
            if line.startswith(key):
                value[name] = int(line.split(":")[1])
    return value


def maximized(window):
    return "_NET_WM_STATE_MAXIMIZED_HORZ" in run("/usr/bin/xprop", "-id", window, "_NET_WM_STATE")


def work_area():
    # _NET_WORKAREA: x, y, width, height of the first desktop.
    values = run("/usr/bin/xprop", "-root", "_NET_WORKAREA").split("=")[1].replace(",", " ").split()
    x, y, w, h = (int(v) for v in values[:4])
    return {"x": x, "y": y, "w": w, "h": h}


def inside(inner, outer, slack=2):
    return (inner["x"] >= outer["x"] - slack and inner["y"] >= outer["y"] - slack and
            inner["x"] + inner["w"] <= outer["x"] + outer["w"] + slack and
            inner["y"] + inner["h"] <= outer["y"] + outer["h"] + slack)


def near(a, b, slack=4):
    return all(abs(a[k] - b[k]) <= slack for k in ("x", "y", "w", "h"))


def extents(node):
    box = node.get_extents(Atspi.CoordType.SCREEN)
    return {"x": box.x, "y": box.y, "w": box.width, "h": box.height}


def showing(node):
    try:
        return node.get_state_set().contains(Atspi.StateType.SHOWING)
    except Exception:
        return False


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    report_path = Path(sys.argv[2]).resolve()
    if app_path.name != "FileCat" or validation not in app_path.parents or report_path.parent != validation or report_path.exists():
        raise SystemExit("Use the isolated FileCat build and a fresh report under ~/FileCatValidation")
    for program in ("FileCat", "filecat"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", program], capture_output=True).returncode == 0:
            raise SystemExit(f"Another {program} is running; stop it so this test owns the only one")
    root = validation / "WindowRestoreFixture" / uuid4().hex
    data, left, right = (root / part for part in ("data", "left", "right"))
    for folder in (data, left, right):
        folder.mkdir(parents=True)
    os.environ.update(desktop_environment())
    report = {"passed": False, "stage": "starting", "fixture": str(root), "looks": [], "saved": [],
              "xftDpi": run("/usr/bin/xrdb", "-query").strip(), "error": None}
    state = {"process": None, "window": None, "app": None}

    def start():
        log = (root / "app.log").open("ab")
        process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data), "--left", str(left),
                                    "--right", str(right)], cwd=root, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                                     if node is not None and node.get_process_id() == process.pid
                                     and node.get_name() == "FileCat"), None), "test-owned FileCat", 40)
        window = wait_for(lambda: next((line.split()[0] for line in run("/usr/bin/wmctrl", "-lp").splitlines()
                                        if len(line.split()) > 2 and line.split()[2] == str(process.pid)), None), "its window", 20)
        time.sleep(2.5)
        state.update(process=process, window=window, app=app)

    def close():
        process = state["process"]
        subprocess.run(["/usr/bin/wmctrl", "-ic", state["window"]], timeout=5)
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            raise AssertionError("FileCat did not exit when its window was closed")
        saved = json.loads((data / "workspace.json").read_text(encoding="utf-8")).get("Window")
        report["saved"].append(saved)
        return saved

    def wm(*args, settle=1.5):
        subprocess.run(["/usr/bin/wmctrl", "-ir", state["window"], *args], timeout=5)
        time.sleep(settle)

    def look(label):
        window, app = state["window"], state["app"]
        frame, work = geometry(window), work_area()
        frames = [node for node in tree(app) if node.get_role_name() == "frame" and showing(node)]
        controls, keys = {}, set()
        for node in tree(app):
            if not showing(node):
                continue
            role, name = node.get_role_name(), node.get_name() or ""
            # A key-bar button is named for its command and described by its key: "F5: Copy…".
            key = role == "button" and (name in KEYS or re.match(r"F(1[0-2]|[1-9]):", node.get_description() or ""))
            if (role in ("menu", "menu item") and name in ("File", "Help")) or \
                    (role in ("entry", "text") and name in ("Search commands", "Command line")) or \
                    (role == "list" and name.startswith("Panel ")) or key:
                controls.setdefault(name, extents(node))
                if key:
                    keys.add(name)
        outside = [name for name, box in controls.items() if not inside(box, frame, 3)]
        entry = {"label": label, "frame": frame, "work": work, "maximized": maximized(window),
                 "frames": [extents(node) for node in frames][:2], "controls": len(controls),
                 "keys": len(keys), "outside": outside}
        report["looks"].append(entry)
        if not inside(frame, work, 4):
            raise AssertionError(f"{label}: the window {frame} leaves the work area {work}")
        if entry["keys"] < 12 or len(controls) < 17:
            raise AssertionError(f"{label}: only {len(controls)} main controls ({entry['keys']} keys) are exposed: {sorted(controls)}")
        if outside:
            raise AssertionError(f"{label}: controls outside the window: {outside}")
        return entry

    try:
        # 1. Normal bounds, maximize and unmaximize.
        report["stage"] = "maximize and restore"
        start()
        wm("-b", "remove,maximized_vert,maximized_horz")
        work = work_area()
        normal = (work["x"] + 120, work["y"] + 90, min(1500, work["w"] - 300), min(950, work["h"] - 220))
        wm("-e", "0,{},{},{},{}".format(*normal), settle=2)
        given = look("normal")
        wm("-b", "add,maximized_vert,maximized_horz")
        big = look("maximized")
        if not big["maximized"] or big["frame"]["w"] < work["w"] - 4:
            raise AssertionError(f"Maximized does not span the work area: {big['frame']} in {work}")
        wm("-b", "remove,maximized_vert,maximized_horz")
        back = look("unmaximized")
        if not near(back["frame"], given["frame"]):
            raise AssertionError(f"Unmaximizing went to {back['frame']}, not {given['frame']}")

        # 2. Closed maximized: reopened maximized, with the same normal bounds behind it.
        report["stage"] = "reopen maximized"
        wm("-b", "add,maximized_vert,maximized_horz")
        saved = close()
        if not saved or not saved.get("Maximized"):
            raise AssertionError(f"The maximized state was not saved: {saved}")
        start()
        reopened = look("reopened maximized")
        if not reopened["maximized"]:
            raise AssertionError("The window did not reopen maximized")
        wm("-b", "remove,maximized_vert,maximized_horz")
        behind = look("unmaximized after reopening")
        if not near(behind["frame"], given["frame"], 6):
            raise AssertionError(f"Behind the maximized window lay {behind['frame']}, not {given['frame']}")

        # 3. Off the right of the screen: mutter keeps a window it is asked to move there on the screen; a position
        # saved on a monitor that went away is beyond the edge, and the window reopens on the screen.
        report["stage"] = "stranded"
        wm("-e", "0,{},{},{},{}".format(work["x"] + work["w"] - 80, work["y"] + 90, normal[2], normal[3]), settle=2)
        report["moveOffScreen"] = geometry(state["window"])
        close()
        workspace_file = data / "workspace.json"
        workspace = json.loads(workspace_file.read_text(encoding="utf-8"))
        workspace["Window"].update(X=work["x"] + work["w"] + 300, Y=work["y"] + 90, Maximized=False)
        workspace_file.write_text(json.dumps(workspace, indent=2), encoding="utf-8")
        report["strandedSaved"] = workspace["Window"]
        start()
        back_on = look("reopened after its monitor went")
        if back_on["frame"]["w"] < normal[2] - 8:
            raise AssertionError(f"Reopening shrank the window to {back_on['frame']}")
        close()
        report["passed"] = True
        report["stage"] = "done"
    except Exception as error:
        report["error"] = str(error)
    finally:
        stop(state["process"])
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
