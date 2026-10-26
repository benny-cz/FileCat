#!/usr/bin/env python3
"""Exercise a Git-built Ubuntu Hex viewer's checksum scope and clipboard.

Usage: linux-atspi-viewer-checksum.py APP REPORT.json UINPUT_HELPER [--stale-scope|--narrow]
Run in the logged-in desktop session with AT-SPI and a guest-only uinput key helper.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import time
from uuid import uuid4
import zlib

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-viewer-find.py")))
descendants = common["descendants"]
find = common["find"]
wait_for = common["wait_for"]
checked_action = common["checked_action"]
send_key = common["send_key"]
close_viewer = common["close_viewer"]
viewer_window_id = common["exact_viewer_window_id"]

CONTENT = b"\x00ABC\n"
SENTINEL = "FileCat checksum clipboard sentinel"


def clipboard_text():
    completed = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                               capture_output=True, timeout=5, env=os.environ.copy())
    return completed.stdout.decode("utf-8") if completed.returncode == 0 else ""


def set_clipboard(value):
    subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-in"],
                   input=value.encode("utf-8"), stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=5,
                   check=True, env=os.environ.copy())


def result_text(viewer):
    result = find(viewer, "entry", "Checksum results")
    if result is None:
        seen = [(node.get_role_name(), node.get_name()) for node in descendants(viewer)
                if "checksum" in (node.get_name() or "").lower() or "sha-256" in (node.get_name() or "").lower()]
        raise AssertionError(f"Checksum result has no accessible entry: {seen!r}")
    return result.get_description() or ""


def action(viewer, name):
    checked_action(find(viewer, "button", name), name)


def rect(node):
    bounds = node.get_component().get_extents(Atspi.CoordType.SCREEN)
    return [bounds.x, bounds.y, bounds.width, bounds.height]


def visible_controls(viewer):
    frame = rect(viewer)
    controls = {}
    for role, name in (("combo box", "Checksum scope"), ("button", "Compute"),
                       ("button", "Copy results"), ("button", "Close checksums"),
                       ("entry", "Checksum results")):
        node = find(viewer, role, name)
        assert node is not None, f"Missing accessible {name}"
        area = rect(node)
        assert area[2] > 0 and area[3] > 0, f"{name} has no size: {area}"
        assert (area[0] >= frame[0] and area[1] >= frame[1] and
                area[0] + area[2] <= frame[0] + frame[2] and
                area[1] + area[3] <= frame[1] + frame[3]), \
            f"{name} escapes the viewer frame: {area} outside {frame}"
        controls[name] = area
    return {"frame": frame, "controls": controls}


def checksum(viewer, helper, scope, content, measure=False):
    send_key(helper, "ctrl+k")
    wait_for(lambda: find(viewer, "button", "Compute"), "checksum controls")
    assert clipboard_text() == SENTINEL, "Opening checksum changed clipboard"
    action(viewer, "Compute")
    expected = hashlib.sha256(content).hexdigest()
    crc = f"{zlib.crc32(content):08x}"
    try:
        result = wait_for(lambda: text if (text := result_text(viewer))
                          and f"SHA-256: {expected}" in text else None,
                          "complete checksum result")
    except AssertionError as error:
        node = find(viewer, "entry", "Checksum results")
        raise AssertionError(f"{error}; displayed={result_text(viewer)!r}; "
                             f"name={node.get_name()!r}; description={node.get_description()!r}; "
                             f"attributes={node.get_attributes()!r}") from error
    assert f"CRC-32: {crc}" in result and f"{len(content)} of {len(content)} bytes read" in result
    assert scope in result, result
    layout = visible_controls(viewer) if measure else None
    assert clipboard_text() == SENTINEL, "Computing checksum changed clipboard"
    action(viewer, "Copy results")
    try:
        copied = wait_for(lambda: text if (text := clipboard_text()) != SENTINEL else None,
                          "explicit checksum Copy")
    except AssertionError as error:
        status = [(node.get_role_name(), node.get_name()) for node in descendants(viewer)
                  if "checksum" in (node.get_name() or "").lower() or "copied" in (node.get_name() or "").lower()]
        raise AssertionError(f"{error}; clipboard={clipboard_text()!r}; status={status!r}") from error
    assert copied == result, "Copied checksum differed from visible complete result"
    action(viewer, "Close checksums")
    set_clipboard(SENTINEL)
    return {"scope": scope, "result": result, "copiedExactly": True, "layout": layout}


def run(app_path, helper, stale_scope=False, narrow=False):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    helper = helper.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents or validation not in helper.parents:
        raise AssertionError("Use the isolated Git-built app and guest-only input helper")
    root = validation / "ChecksumUbuntu" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    fixture = source / "native-checksum.bin"
    fixture.write_bytes(CONTENT)
    original_clipboard = clipboard_text()
    set_clipboard(SENTINEL)
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    if (validation / "dotnet-sdk" / "dotnet").is_file():
        os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    env = os.environ.copy()
    env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    process = None
    viewer = None
    title = "native-checksum.bin — FileCat Viewer"
    try:
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((node for node in
                           (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                           if node.get_name() == "FileCat" and
                           find(node, "list", f"Panel 1, source, {source}")), None),
                           "test-owned FileCat instance")
            source_list = find(app, "list", f"Panel 1, source, {source}")
            row = wait_for(lambda: next((item for item in descendants(source_list)
                           if item.get_role_name() == "list item" and
                           (item.get_name() or "").startswith(f"{fixture.name}, file,")), None),
                           "the exact checksum source row")
            wait_for(lambda: row.grab_focus() and
                     row.get_state_set().contains(Atspi.StateType.FOCUSED),
                     "source-row focus")
            checked_action(find(app, "button", "View"), "View command")
            viewer = wait_for(lambda: find(app, "frame", title), "binary viewer")
            # Hex mode is selected automatically for the leading zero byte.
            wait_for(lambda: find(viewer, "label", "Mode: Hex"), "Hex mode")
            geometry = None
            if narrow:
                scale_info = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True,
                                            text=True, check=True, timeout=5).stdout
                match = re.search(r"\bScale: ([0-9.]+)", scale_info)
                scale = float(match.group(1)) if match else None
                assert scale == 2.0, f"Expected the test VM's 200% scale, got {scale}"
                window_id = viewer_window_id(title, process.pid)
                subprocess.run(["/usr/bin/wmctrl", "-i", "-r", window_id, "-e",
                                "0,150,100,1440,1000"], check=True, timeout=5)
                subprocess.run(["/usr/bin/wmctrl", "-ia", window_id], check=True, timeout=5)
                time.sleep(.4)
                geometry = {"displayScale": scale, "requestedPhysicalSize": [1440, 1000],
                            "frame": rect(viewer)}
            send_key(helper, "shift+left")
            send_key(helper, "ctrl+k")
            wait_for(lambda: find(viewer, "button", "Compute"), "selected-range checksum controls")
            choices = [node.get_name() for node in descendants(viewer)
                       if "Selected range" in (node.get_name() or "")]
            assert choices, "The one-byte selection is absent from checksum scope"
            if narrow:
                geometry["selectedScope"] = visible_controls(viewer)
            if stale_scope:
                before = fixture.stat()
                fixture.write_bytes(b"\x00ABD\n")
                os.utime(fixture, ns=(before.st_atime_ns, before.st_mtime_ns + 2_000_000_000))
                action(viewer, "Compute")
                refused = wait_for(lambda: text if (text := result_text(viewer))
                                   and "changed" in text.lower() else None,
                                   "changed-source checksum refusal")
                assert "SHA-256:" not in refused, "A stale scope published a hash"
                copy = find(viewer, "button", "Copy results")
                assert copy is not None and not copy.get_state_set().contains(Atspi.StateType.ENABLED), \
                    "A stale scope left Copy results enabled"
                assert clipboard_text() == SENTINEL, "A stale checksum replaced the clipboard"
                assert not list(target.iterdir()), "A stale checksum changed the destination"
                fixture.write_bytes(CONTENT)
                assert fixture.read_bytes() == CONTENT, "The test fixture was not restored"
                with app_path.with_name("FileCat.dll").open("rb") as library:
                    dll_hash = hashlib.file_digest(library, "sha256").hexdigest()
                return {"passed": True, "platform": "Ubuntu", "appDllSha256": dll_hash,
                        "fixture": str(root),
                        "staleScope": {"refusal": refused, "copyDisabled": True,
                                       "clipboardUnchanged": True, "sourceBytesRestored": True}}
            action(viewer, "Close checksums")
            selected = checksum(viewer, helper, "selected range 0x0–0x0", CONTENT[:1], measure=narrow)
            send_key(helper, "right")
            whole = checksum(viewer, helper, "whole file", CONTENT, measure=narrow)
            assert fixture.read_bytes() == CONTENT and not list(target.iterdir()), "Checksum changed fixture bytes"
            with app_path.open("rb") as binary:
                app_hash = hashlib.file_digest(binary, "sha256").hexdigest()
            with app_path.with_name("FileCat.dll").open("rb") as library:
                dll_hash = hashlib.file_digest(library, "sha256").hexdigest()
            return {"passed": True, "platform": "Ubuntu", "appSha256": app_hash,
                    "appDllSha256": dll_hash, "fixture": str(root),
                    "selectedByte": selected, "wholeFile": whole, "geometry": geometry,
                    "sourceBytesUnchanged": True}
    finally:
        if viewer is not None and process is not None:
            try:
                close_viewer(app, title, process.pid)
            except (AssertionError, subprocess.CalledProcessError):
                pass
        if process is not None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        if stale_scope and fixture.exists():
            fixture.write_bytes(CONTENT)
        set_clipboard(original_clipboard)


def main():
    if len(sys.argv) not in (4, 5) or (len(sys.argv) == 5 and sys.argv[4] not in ("--stale-scope", "--narrow")):
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    try:
        mode = sys.argv[4] if len(sys.argv) == 5 else ""
        result = run(Path(sys.argv[1]), Path(sys.argv[3]), stale_scope=mode == "--stale-scope",
                     narrow=mode == "--narrow")
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
