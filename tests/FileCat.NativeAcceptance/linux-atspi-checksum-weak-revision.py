#!/usr/bin/env python3
"""Inspect a narrow native checksum result for a provider with length-only revision.

Usage: linux-atspi-checksum-weak-revision.py APP REPORT.json UINPUT_KEY_HELPER
The ZIP is a test-owned content fixture in the isolated VM, not a code archive.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
from uuid import uuid4
import zipfile

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
helpers = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))
viewer_helpers = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
checksum_helpers = runpy.run_path(str(HERE / "linux-atspi-viewer-checksum.py"))
wait_for = helpers["wait_for"]
matching = helpers["matching"]
tree = helpers["tree"]
action = helpers["action"]
desktop_environment = helpers["desktop_environment"]
find = viewer_helpers["find"]
descendants = viewer_helpers["descendants"]
result_text = checksum_helpers["result_text"]
visible_controls = checksum_helpers["visible_controls"]

CONTENT = b"\x00ABC\n"


def send_key(helper, password, chord):
    sent = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(helper), chord],
                          input=password + "\n", capture_output=True, text=True, timeout=15)
    if sent.returncode:
        raise AssertionError(f"The owned {chord} input failed: {sent.stderr[-300:]}")


def run(app_path, key_helper, sudo_password):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents or \
            key_helper != HERE / "linux-uinput-key.py":
        raise AssertionError("Use the Git-built app and tracked input helper in the isolated VM")
    root = validation / "ChecksumWeakRevision" / uuid4().hex
    source, target, data = (root / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    archive = source / "weak-revision.zip"
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_STORED) as writer:
        writer.writestr("weak.bin", CONTENT)
    archive_hash = hashlib.sha256(archive.read_bytes()).hexdigest()
    (data / "settings.json").write_text(json.dumps({"SchemaVersion": 1,
                                                     "ViewerWidth": 480,
                                                     "ViewerHeight": 300}), encoding="utf-8")
    env = os.environ.copy()
    env.update(desktop_environment())
    env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    process = None
    try:
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        zip_row = wait_for(lambda: next((node for node in tree(app)
                           if node.get_role_name() == "list item" and
                           (node.get_name() or "").startswith("weak-revision.zip, file,")), None),
                           "owned ZIP fixture row")
        if not zip_row.grab_focus():
            raise AssertionError("The test ZIP row could not take focus")
        send_key(key_helper, sudo_password, "enter")
        member = wait_for(lambda: next((node for node in tree(app)
                          if node.get_role_name() == "list item" and
                          (node.get_name() or "").startswith("weak.bin, file,")), None),
                          "archive member row", seconds=30)
        if not member.grab_focus():
            raise AssertionError("The archive member could not take focus")
        action(wait_for(lambda: next(iter(matching(app, "button", "View")), None),
                        "View command"), "View command")
        viewer = wait_for(lambda: find(app, "frame", "weak.bin — FileCat Viewer"),
                          "member viewer")
        send_key(key_helper, sudo_password, "ctrl+k")
        action(wait_for(lambda: find(viewer, "button", "Compute"), "Compute"), "Compute")
        expected = hashlib.sha256(CONTENT).hexdigest()
        result = wait_for(lambda: value if f"SHA-256: {expected}" in (
                          value := result_text(viewer)) else None,
                          "complete length-only checksum")
        qualification = "only file length was compared; source consistency could not be verified"
        if qualification not in result:
            raise AssertionError(f"The narrow result omitted its weak-revision qualification: {result!r}")
        if f"{len(CONTENT)} of {len(CONTENT)} bytes read" not in result:
            raise AssertionError("The checksum result omitted its exact byte count")
        layout = visible_controls(viewer)
        frame = layout["frame"]
        if frame[2] > 760 or frame[3] > 540:
            raise AssertionError(f"The restored 480 × 300 viewer is not narrow: {frame}")
        if hashlib.sha256(archive.read_bytes()).hexdigest() != archive_hash or list(target.iterdir()):
            raise AssertionError("The checksum changed the archive or target")
        return {"passed": True, "build": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
                "source": str(archive), "member": "weak.bin", "contentSha256": expected,
                "result": result, "layout": layout, "archiveUnchanged": True,
                "nativeBackend": "Ubuntu Wayland AT-SPI, Git-built ZIP member viewer"}
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2])
    if report.parent.resolve() != (Path.home() / "FileCatValidation").resolve(strict=True):
        raise SystemExit("Write the report directly below FileCatValidation")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise SystemExit("The isolated native input credential is required")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[3]), password)
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
