#!/usr/bin/env python3
"""Delete one extended attribute through FileCat with physical keys and check its file's Attributes count on return.

Usage: linux-atspi-xattr-delete.py APP_PATH REPORT.json KEY_HELPER
Run in the logged-in Ubuntu session. The isolated Git-built app, report and test input helper must live below
~/FileCatValidation. Pass the guest's sudo password only through FILECAT_NATIVE_SUDO_PASSWORD for the physical uinput
helper; the runner removes it before starting FileCat. It switches the source panel to the Full column profile
(Alt+2) and scrolls to its Attributes column, opens tool.bin's attributes (Alt+Shift+Enter), deletes user.filecat.payload (Delete, Enter) and returns
(Backspace) without rereading. The attributes left on disk must be exactly the sibling ones, the file's bytes and
other.bin's attribute unchanged. Captures beside the report (REPORT.before.png, REPORT.dialog.png, REPORT.after.png)
show the Attributes cell before the deletion, the confirmation and the cell on return.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


shared = runpy.run_path(str(Path(__file__).with_name("linux-atspi-dialog-pointer-dismiss.py")))
descendants = shared["descendants"]
wait_for = shared["wait_for"]
named = shared["named"]
activate_window = shared["activate_window"]

CAPTURE = """import sys, gi
gi.require_version("Gdk", "3.0"); gi.require_version("GdkX11", "3.0")
from gi.repository import Gdk, GdkX11
window = GdkX11.X11Window.foreign_new_for_display(Gdk.Display.get_default(), int(sys.argv[1], 16))
image = Gdk.pixbuf_get_from_window(window, 0, 0, window.get_width(), window.get_height())
image.savev(sys.argv[2], "png", [], [])
print(image.get_width(), image.get_height())
"""
PAYLOAD, ORIGIN, OTHER = "user.filecat.payload", "user.xdg.origin.url", "user.filecat.other"


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def attributes(path):
    return {name: os.getxattr(path, name).decode("utf-8") for name in sorted(os.listxattr(path))}


def focused_item(app):
    return next((node.get_name() for node in descendants(app) if node.get_role_name() == "list item"
                 and node.get_state_set().contains(Atspi.StateType.FOCUSED)), None)


def focused_any(app):
    return next(((node.get_role_name(), node.get_name()) for node in descendants(app)
                 if node.get_state_set().contains(Atspi.StateType.FOCUSED)), None)


def item_name(spoken):
    """A row's accessible name starts with its item name and goes on with its details."""
    return spoken.split(",")[0] if spoken else None


def listed(app, name):
    return any(node.get_role_name() == "list item" and item_name(node.get_name()) == name for node in descendants(app))


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (Path(value).resolve(strict=True) for value in (sys.argv[1], sys.argv[3]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper.name != "linux-uinput-key.py" or validation not in helper.parents or
            report_path.parent != validation):
        raise SystemExit("Use only the owned app, input helper and report under FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("Transient guest sudo authentication is required for physical key input")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "XattrDeleteFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    tool, other = source / "tool.bin", source / "other.bin"
    tool.write_bytes(b"tool contents\n")
    other.write_bytes(b"other contents\n")
    os.setxattr(tool, ORIGIN, b"https://example.com/tool.bin")
    os.setxattr(tool, PAYLOAD, b"hidden payload")
    os.setxattr(other, OTHER, b"untouched")
    before = {"tool": attributes(tool), "other": attributes(other), "toolSha256": sha256(tool),
              "otherSha256": sha256(other)}
    shown = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True, text=True, timeout=5)
    scale = next((line.split(":")[1].strip() for line in shown.stdout.splitlines() if "Scale:" in line), None)
    report = {"passed": False, "appDllSha256": sha256(app_path.with_name("FileCat.dll")), "displayScale": scale,
              "fixture": str(fixture), "input": "physical uinput keyboard", "before": before, "steps": []}
    process = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat" and
                named(candidate, "list", f"Panel 1, source, {source}")), None),
                "the isolated FileCat source panel", seconds=25)
            activate_window(process.pid)
            windows = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True, timeout=5).stdout
            window = next(line.split()[0] for line in windows.splitlines()
                          if len(line.split()) > 2 and line.split()[2] == str(process.pid))

            def send(chord):
                sent = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(helper), chord],
                                      input=sudo_password + "\n", capture_output=True, text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} failed with exit {sent.returncode}")
                report["steps"].append({"key": chord, "focused": focused_item(app)})

            def scroll_to_attributes():
                # Attributes is the Full profile's last column: Right scrolls a list wider than its panel.
                for _ in range(10):
                    send("right")
                time.sleep(1.5)

            def capture(name):
                path = report_path.with_suffix(f".{name}.png")
                done = subprocess.run(["/usr/bin/python3", "-c", CAPTURE, window, str(path)],
                                      env={**os.environ, "GDK_BACKEND": "x11"}, capture_output=True, text=True,
                                      timeout=30)
                report[f"{name}Capture"] = {"path": str(path), "size": done.stdout.strip() or done.stderr.strip()[-300:]}

            send("alt+2")
            for _ in range(2):
                send("down")
            wait_for(lambda: item_name(focused_item(app)) == "tool.bin", "focus on tool.bin")
            scroll_to_attributes()
            capture("before")
            send("alt+shift+enter")
            wait_for(lambda: listed(app, PAYLOAD) and listed(app, ORIGIN), "tool.bin's attributes listed")
            send("down")
            wait_for(lambda: item_name(focused_item(app)) == PAYLOAD, f"focus on {PAYLOAD}")
            send("delete")
            wait_for(lambda: named(app, "button", "Delete"), "the Delete confirmation")
            time.sleep(0.5)
            capture("dialog")
            report["dialogFocus"] = focused_any(app)
            # The confirmation of a permanent deletion may start on Cancel: move to Delete before Enter.
            for _ in range(4):
                if (focused_any(app) or ("", ""))[1] == "Delete":
                    break
                send("tab")
            if (focused_any(app) or ("", ""))[1] != "Delete":
                raise AssertionError(f"Delete is not focusable from the confirmation: {focused_any(app)}")
            send("enter")
            wait_for(lambda: not listed(app, PAYLOAD) and listed(app, ORIGIN), f"{PAYLOAD} gone from the attribute list")
            report["afterDelete"] = attributes(tool)
            send("backspace")
            wait_for(lambda: named(app, "list", f"Panel 1, source, {source}") and
                     item_name(focused_item(app)) == "tool.bin", "back in the folder on tool.bin")
            scroll_to_attributes()
            capture("after")
            after = {"tool": attributes(tool), "other": attributes(other), "toolSha256": sha256(tool),
                     "otherSha256": sha256(other)}
            report["after"] = after
            expected_tool = {name: value for name, value in before["tool"].items() if name != PAYLOAD}
            if after["tool"] != expected_tool:
                raise AssertionError(f"tool.bin keeps {after['tool']}, expected {expected_tool}")
            if (after["other"], after["toolSha256"], after["otherSha256"]) != (
                    before["other"], before["toolSha256"], before["otherSha256"]) or any(target.iterdir()):
                raise AssertionError("Deleting one attribute changed file bytes, the other file or the target")
            report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
