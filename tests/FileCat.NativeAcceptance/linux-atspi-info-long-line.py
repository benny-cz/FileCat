#!/usr/bin/env python3
"""Check native Info long-line access, reflow and Wrap on an owned PNG.

Usage: linux-atspi-info-long-line.py GIT_APP REPORT.json
Run on the signed-in Ubuntu validation desktop. This creates no archive and
does not use or repeat the already accepted Text/Hex clipboard route.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import struct
import subprocess
import sys
import time
from uuid import uuid4
import zlib

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
common = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
find, wait_for, checked_action, descendants = (common[name] for name in
                                               ("find", "wait_for", "checked_action", "descendants"))
viewer_window_id = common["exact_viewer_window_id"]
TAIL = "INFO-PATH-END-9C4D"
NOTE = "C:/inspection/" + "nested/" * 22 + TAIL


def png_chunk(kind, data):
    payload = kind + data
    return struct.pack(">I", len(data)) + payload + struct.pack(">I", zlib.crc32(payload))


def fixture_png():
    return (b"\x89PNG\r\n\x1a\n"
            + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0))
            + png_chunk(b"tEXt", b"Description\0" + NOTE.encode("ascii"))
            + png_chunk(b"IDAT", zlib.compress(b"\0\0\0\0"))
            + png_chunk(b"IEND", b""))


def info(viewer):
    document = find(viewer, "entry", "File information")
    if document is None:
        raise AssertionError("Native Info document is missing")
    text = document.get_text_iface()
    if text is None:
        raise AssertionError("Info has no accessible read-only text")
    return document, Atspi.Text.get_text(text, 0, -1), document.get_description()


def resize(pid, title, width, height):
    window = viewer_window_id(title, pid)
    subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-b",
                    "remove,maximized_vert,maximized_horz"], check=True, timeout=5)
    subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e",
                    f"0,40,40,{width},{height}"], check=True, timeout=5)
    def settled():
        output = subprocess.run(["/usr/bin/xdotool", "getwindowgeometry", "--shell", window],
                                capture_output=True, text=True, check=True, timeout=5).stdout
        values = dict(line.split("=", 1) for line in output.splitlines() if "=" in line)
        actual = [int(values["WIDTH"]), int(values["HEIGHT"])]
        return actual if all(abs(a - b) <= 35 for a, b in zip(actual, (width, height))) else None
    return wait_for(settled, f"owned Info viewer resize to {width}x{height}", seconds=6)


def run(app_path, report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use a Git-built isolated app and fresh guest-local report")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Another FileCat process is running")
    root = validation / "InfoLongLine" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    file = source / "info-line.png"
    original = fixture_png()
    file.write_bytes(original)
    os.environ.update(runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))["desktop_environment"]())
    environment = os.environ.copy()
    environment["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    result = {"passed": False, "stage": "startup", "sourceSha256": hashlib.sha256(original).hexdigest(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
              "metadataSuffix": TAIL, "metadataLength": len(NOTE)}
    process = None
    try:
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
                       (node.get_name() or "").startswith("info-line.png, file,")), None),
                       "test-owned PNG row")
        if not row.grab_focus():
            raise AssertionError("The PNG row could not take native focus")
        checked_action(find(app, "button", "View"), "owned PNG View")
        title = "info-line.png — FileCat Viewer"
        viewer = wait_for(lambda: find(app, "frame", title), "owned viewer")
        result["stage"] = "Info long-line access"
        checked_action(find(viewer, "toggle button", "Info"), "Info mode")
        wait_for(lambda: find(viewer, "label", "Mode: Info"), "Info mode")
        wait_for(lambda: document if "Description" in (document := info(viewer))[1] else None,
                 "PNG metadata report")
        result["narrowSize"] = resize(process.pid, title, 480, 300)
        for _ in range(4):
            checked_action(find(viewer, "button", "Increase text and hex size"), "enlarged Info text")
        scrollbar = find(viewer, "scroll bar", "Text horizontal position")
        if scrollbar is None:
            raise AssertionError("The long unwrapped Info line has no reachable horizontal scrollbar")
        checked_action(find(viewer, "button", "Show or hide viewer Find bar"), "Info Find toggle")
        query = wait_for(lambda: find(viewer, "entry", "Find in file"), "Info Find query")
        if not query.get_editable_text().set_text_contents(TAIL):
            raise AssertionError("Could not set the owned Info suffix query")
        checked_action(find(viewer, "button", "Find next match"), "Info Find Next")
        def revealed():
            document, value, status = info(viewer)
            return (document, value, status) if TAIL in value and "unwrapped, horizontal position" in status else None
        narrow = wait_for(revealed, "long Info suffix in the narrow viewport")
        result["narrowStatus"] = narrow[2]
        if narrow[0].get_state_set().contains(Atspi.StateType.EDITABLE):
            raise AssertionError("Info document was exposed as editable")
        result["wideSize"] = resize(process.pid, title, 900, 650)
        wide = wait_for(revealed, "same Info suffix after wider reflow")
        result["wideStatus"] = wide[2]
        wrap = find(viewer, "check box", "Wrap")
        if wrap is None or wrap.get_state_set().contains(Atspi.StateType.CHECKED):
            raise AssertionError("Info Wrap control was absent or already active")
        checked_action(wrap, "Info Wrap")
        wrapped = wait_for(lambda: state if "wrapped" in (state := info(viewer))[2] else None,
                           "wrapped Info report")
        result["wrappedStatus"] = wrapped[2]
        # Returning focus to the current report must leave complete source
        # metadata accessible without mutating the inspected file.
        if not wrapped[0].grab_focus() or file.read_bytes() != original or list(target.iterdir()):
            raise AssertionError("Info focus, source bytes or target changed")
        result.update({"passed": True, "stage": "complete", "sourceUnchanged": True,
                       "targetEmpty": True})
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if (root / "app.log").is_file():
            result["appLogTail"] = (root / "app.log").read_text(
                encoding="utf-8", errors="replace")[-900:]
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report)
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(outcome))
    raise SystemExit(0 if outcome["passed"] else 1)
