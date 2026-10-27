#!/usr/bin/env python3
"""Exercise Quick View's local empty, picture and fallback states in Ubuntu.

Usage: linux-atspi-quick-view-local.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD only for the isolated physical keyboard.
The runner owns its files, FileCat process and profile inside the validation VM.
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
import zlib
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def png(width, height):
    def chunk(kind, payload):
        body = kind + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body))

    # A valid, compressible picture whose decoded dimensions exceed the pane.
    pixel = b"\x22\x89\xcc"
    scanline = b"\x00" + pixel * width
    return (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(scanline * height)) + chunk(b"IEND", b""))


def run(app_path: Path, report: Path, helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (p.resolve(strict=True) for p in (app_path, helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper != validation / "linux-uinput-key.py" or report.parent != validation or
            report.exists()):
        raise AssertionError("Use the isolated Git app, physical keyboard and fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient physical-input credential is required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before this isolated check")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    common = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, action, press, stop, desktop_environment = (
        common[name] for name in ("wait_for", "tree", "action", "press", "stop",
                                    "desktop_environment"))
    viewer_close = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-viewer-find.py"))[
        "close_viewer"]
    root = validation / "QuickViewLocal" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    fixtures = {
        "a-empty.txt": b"",
        "b-normal.txt": b"owned quick-view current text\n",
        "c-broken.png": b"\x89PNG\r\n\x1a\n" + b"broken picture body" * 3,
        "d-valid.png": png(1024, 768),
    }
    for name, content in fixtures.items():
        (source / name).write_bytes(content)
    with (source / "e-oversize.png").open("wb") as output:
        output.write(b"\x89PNG\r\n\x1a\n")
        output.seek(64 * 1024 * 1024)
        output.write(b"X")
    original_hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                       for p in source.iterdir()}
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "sourceSha256": original_hashes}
    process = None

    def matching(app, role, name):
        return [node for node in tree(app) if node.get_role_name() == role and
                node.get_name() == name and node.get_state_set().contains(Atspi.StateType.SHOWING)]

    def visible(app):
        return [(node.get_role_name(), node.get_name()) for node in tree(app)
                if node.get_state_set().contains(Atspi.StateType.SHOWING) and
                node.get_role_name() in ("button", "label", "image", "entry")]

    def shown_text(app, fragment):
        return next((name for _, name in visible(app) if fragment in (name or "")), None)

    def select(app, name):
        row = wait_for(lambda: next((node for node in tree(app) if
                       node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith(name + ",")), None), name)
        if not row.grab_focus():
            raise AssertionError(f"Could not focus {name}")
        return row

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        main_ids = [line.split()[0] for line in subprocess.check_output(
            ["/usr/bin/wmctrl", "-lp"], text=True).splitlines()
            if len(line.split()) > 2 and line.split()[2] == str(process.pid)]
        if len(main_ids) != 1:
            raise AssertionError(f"Expected one owned main window: {main_ids}")
        subprocess.run(["/usr/bin/wmctrl", "-ir", main_ids[0], "-b",
                        "remove,maximized_vert,maximized_horz"], check=True)
        subprocess.run(["/usr/bin/wmctrl", "-ir", main_ids[0], "-e",
                        "0,20,20,900,540"], check=True)
        select(app, "a-empty.txt")
        press(helper, sudo_password, "ctrl+q", process.pid)
        result["stage"] = "small pane and empty file"
        result["emptyMessage"] = wait_for(lambda: shown_text(app, "This file is empty"),
                                          "explicit empty preview")
        full = wait_for(lambda: matching(app, "button", "Open focused file in full viewer"),
                        "accessible full-viewer action")[0]
        result["fullViewerAction"] = full.get_name()
        result["quickViewName"] = next((node.get_name() for node in tree(app)
            if node.get_name() == "Quick view"), None)
        if not result["quickViewName"]:
            raise AssertionError("Quick view has no accessible pane name")
        pane = next(node for node in tree(app) if node.get_name() == "Quick view")
        pane_bounds = pane.get_component().get_extents(Atspi.CoordType.SCREEN)
        if pane_bounds.width > 550:
            # Resize the actual panel boundary using its supported keyboard
            # route; whole-window resize can be clamped by the saved layout.
            splitter = next((node for node in tree(app) if node.get_role_name() == "slider" and
                             "Vertical panel boundary" in (node.get_name() or "")), None)
            if splitter is None or not splitter.grab_focus():
                raise AssertionError("Could not focus the accessible panel splitter")
            result["splitterName"] = splitter.get_name()
            for _ in range(4):
                press(helper, sudo_password, "shift+right", process.pid)
            pane_bounds = wait_for(lambda: pane.get_component().get_extents(
                Atspi.CoordType.SCREEN) if pane.get_component().get_extents(
                Atspi.CoordType.SCREEN).width <= 550 else None,
                "narrowed native Quick View pane", seconds=8)
        result["quickViewBounds"] = [pane_bounds.x, pane_bounds.y,
                                     pane_bounds.width, pane_bounds.height]
        if pane_bounds.width > 550 or pane_bounds.width < 240:
            raise AssertionError("Native Quick View did not occupy a small, usable pane")
        full_bounds = full.get_component().get_extents(Atspi.CoordType.SCREEN)
        result["fullViewerBounds"] = [full_bounds.x, full_bounds.y,
                                      full_bounds.width, full_bounds.height]
        action(full, "accessible full viewer")
        result["emptyViewerWindow"] = wait_for(lambda: next((node.get_name() for node in tree(app)
            if node.get_role_name() == "frame" and
            (node.get_name() or "").startswith("a-empty.txt — FileCat Viewer")), None),
            "accessible-action full viewer")
        viewer_close(app, result["emptyViewerWindow"], process.pid)
        result["stage"] = "damaged picture fallback"
        select(app, "c-broken.png")
        result["damagedNotice"] = wait_for(lambda: shown_text(app, "Picture preview failed:"),
                                           "damaged picture reason", seconds=20)
        if not shown_text(app, "Hex bytes are shown"):
            raise AssertionError("The damaged picture did not explain Hex fallback")
        result["stage"] = "bounded decoded picture"
        select(app, "d-valid.png")
        result["pictureInfo"] = wait_for(lambda: shown_text(app, "PNG, 1,024 × 768"),
                                         "native decoded picture", seconds=25)
        image = next((node for node in tree(app) if node.get_role_name() == "image" and
                      node.get_name() == "Thumbnail" and
                      node.get_state_set().contains(Atspi.StateType.SHOWING)), None)
        result["thumbnail"] = image.get_name() if image else None
        if not result["thumbnail"]:
            raise AssertionError("Decoded picture lacks an accessible image")
        image_bounds = image.get_component().get_extents(Atspi.CoordType.SCREEN)
        result["thumbnailBounds"] = [image_bounds.x, image_bounds.y,
                                      image_bounds.width, image_bounds.height]
        if image_bounds.width > pane_bounds.width or image_bounds.height > pane_bounds.height:
            raise AssertionError("Decoded image extends beyond the small pane")
        result["stage"] = "64 MiB picture cap"
        select(app, "e-oversize.png")
        result["oversizeNotice"] = wait_for(lambda: shown_text(app,
            "Automatic picture preview is limited to 64 MiB"), "picture cap", seconds=20)
        if not shown_text(app, "Hex bytes are shown"):
            raise AssertionError("The oversized picture did not explain Hex fallback")
        result["stage"] = "physical full viewer keyboard route"
        select(app, "b-normal.txt")
        wait_for(lambda: shown_text(app, "b-normal.txt"), "current preview identity")
        result["currentTextControl"] = wait_for(lambda: matching(app, "entry", "Text viewer"),
                                                 "current native Text preview")[0].get_name()
        press(helper, sudo_password, "f3", process.pid)
        result["viewerWindow"] = wait_for(lambda: next((node.get_name() for node in tree(app)
            if node.get_role_name() == "frame" and
            (node.get_name() or "").startswith("b-normal.txt — FileCat Viewer")), None),
            "physical F3 full viewer")
        result["sourceUnchanged"] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                      for p in source.iterdir()} == original_hashes
        result["targetEmpty"] = not any(target.iterdir())
        result["passed"] = result["sourceUnchanged"] and result["targetEmpty"]
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if process is not None and process.poll() is None:
            try:
                result["visibleAtFailure"] = visible(app)[-100:]
            except Exception:
                pass
    finally:
        stop(process)
        shutil.rmtree(root)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(run(*(Path(arg) for arg in sys.argv[1:])))
