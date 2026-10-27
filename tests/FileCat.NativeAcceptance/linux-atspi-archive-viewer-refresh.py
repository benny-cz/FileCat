#!/usr/bin/env python3
"""Refresh an owned read-only TAR member in the native Ubuntu Text viewer.

Usage: linux-atspi-archive-viewer-refresh.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD transiently for physical Enter/F3.
"""

import hashlib
import io
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tarfile
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def make_tar(path: Path, data: bytes):
    with tarfile.open(path, "w") as archive:
        info = tarfile.TarInfo("member.txt")
        info.size = len(data)
        info.mode = 0o644
        archive.addfile(info, io.BytesIO(data))


def run(app_path: Path, report: Path, helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (path.resolve(strict=True) for path in (app_path, helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper != validation / "linux-uinput-key.py" or report.parent != validation or
            report.exists()):
        raise AssertionError("Use the isolated Git app, keyboard and fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient physical-input credential is required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before this isolated check")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "press", "stop", "desktop_environment"))
    viewer_id = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-viewer-find.py"))[
        "exact_viewer_window_id"]
    root = validation / "ArchiveViewerRefresh" / uuid4().hex
    source, target, data, staging = (root / name for name in ("source", "target", "data", "staging"))
    for folder in (source, target, data, staging):
        folder.mkdir(parents=True)
    old, new = b"old archive revision\n", b"new archive revision\n"
    archive_path = source / "owned.tar"
    make_tar(archive_path, old)
    original_hash = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    replacement = staging / "replacement.tar"
    make_tar(replacement, new)
    replacement_hash = hashlib.sha256(replacement.read_bytes()).hexdigest()
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "originalTarSha256": original_hash,
        "replacementTarSha256": replacement_hash}
    process = None

    def find(root_node, role, name):
        return next((node for node in tree(root_node) if node.get_role_name() == role and
                     node.get_name() == name), None)

    def row_named(root_node, prefix):
        return next((node for node in tree(root_node) if node.get_role_name() == "list item" and
                     (node.get_name() or "").startswith(prefix)), None)

    def text_of(document):
        interface = document.get_text_iface() if document is not None else None
        return Atspi.Text.get_text(interface, 0, -1) if interface is not None else ""

    def labels(root_node):
        return [node.get_name() for node in tree(root_node) if node.get_role_name() == "label" and
                node.get_state_set().contains(Atspi.StateType.SHOWING)]

    try:
        os.environ.update(desktop_environment())
        child_env = os.environ.copy()
        child_env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=child_env, stdout=output,
                                       stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "open archive member"
        tar_row = wait_for(lambda: row_named(app, "owned.tar,"), "owned TAR row")
        if not tar_row.grab_focus():
            raise AssertionError("Could not focus the owned TAR")
        press(helper, sudo_password, "enter", process.pid)
        archive_list = wait_for(lambda: next((node for node in tree(app)
            if node.get_role_name() == "list" and "owned.tar" in (node.get_name() or "")), None),
            "read-only archive panel")
        member = wait_for(lambda: row_named(archive_list, "member.txt,"), "archive member")
        if not member.grab_focus():
            raise AssertionError("Could not focus the archive member")
        press(helper, sudo_password, "f3", process.pid)
        title = "member.txt — FileCat Viewer"
        viewer = wait_for(lambda: find(app, "frame", title), "same-window Text viewer")
        initial_viewer_id = viewer_id(title, process.pid)
        document = wait_for(lambda: find(viewer, "entry", "Text viewer"), "accessible Text document")
        original_text = wait_for(lambda: value if "old archive revision" in
                                 (value := text_of(document)) else None, "original TAR content")
        result["originalText"] = original_text
        result["stage"] = "replace and refresh"
        os.replace(replacement, archive_path)
        if hashlib.sha256(archive_path.read_bytes()).hexdigest() != replacement_hash:
            raise AssertionError("The replacement archive bytes differ")
        result["beforeRefreshText"] = text_of(document)
        if "old archive revision" not in result["beforeRefreshText"]:
            raise AssertionError("The old snapshot vanished before explicit Refresh")
        result["preRefreshBanners"] = [name for name in labels(viewer) if "changed" in name.lower()]
        refresh = wait_for(lambda: find(viewer, "push button", "Refresh viewer content") or
                           find(viewer, "button", "Refresh viewer content"), "Refresh viewer action")
        if refresh.get_n_actions() < 1 or not refresh.do_action(0):
            raise AssertionError("The native viewer Refresh action is unavailable")
        refreshed = wait_for(lambda: value if "new archive revision" in
                             (value := text_of(document)) else None, "replacement TAR content", seconds=25)
        result["refreshedText"] = refreshed
        result["sameViewer"] = viewer_id(title, process.pid) == initial_viewer_id
        result["afterRefreshBanners"] = [name for name in labels(viewer)
                                         if "Refresh failed" in name or "File changed" in name]
        if not result["sameViewer"] or result["afterRefreshBanners"]:
            raise AssertionError("Successful Refresh replaced the window or left a stale warning")
        result["stage"] = "missing archive rollback"
        archived = staging / "last-good.tar"
        os.replace(archive_path, archived)
        if refresh.get_n_actions() < 1 or not refresh.do_action(0):
            raise AssertionError("Refresh could not be retried after the archive vanished")
        failed = wait_for(lambda: [name for name in labels(viewer) if name.startswith("Refresh failed:")],
                          "visible failed-refresh banner", seconds=25)
        result["failedRefreshBanner"] = failed
        if any(".. The previous view" in name for name in failed):
            raise AssertionError("The provider error and recovery sentence have duplicate punctuation")
        result["retainedAfterFailure"] = text_of(document)
        if "new archive revision" not in result["retainedAfterFailure"]:
            raise AssertionError("Failed Refresh discarded the last good snapshot")
        os.replace(archived, archive_path)
        result["restoredArchiveSha256"] = hashlib.sha256(archive_path.read_bytes()).hexdigest()
        result["targetEmpty"] = not any(target.iterdir())
        result["passed"] = (result["restoredArchiveSha256"] == replacement_hash and result["targetEmpty"])
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        stop(process)
        shutil.rmtree(root)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(run(*(Path(arg) for arg in sys.argv[1:])))
