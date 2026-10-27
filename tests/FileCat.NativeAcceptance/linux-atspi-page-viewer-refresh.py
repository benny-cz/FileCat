#!/usr/bin/env python3
"""Check native Ubuntu WebKit Page refresh and missing-source rollback.

Usage: linux-atspi-page-viewer-refresh.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD transiently for physical F3.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


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
    wait_for, tree, action, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "action", "press", "stop",
                                     "desktop_environment"))
    viewer_id = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-viewer-find.py"))[
        "exact_viewer_window_id"]
    root = validation / "PageViewerRefresh" / uuid4().hex
    source, target, data, staging = (root / name for name in ("source", "target", "data", "staging"))
    for folder in (source, target, data, staging):
        folder.mkdir(parents=True)
    old = b"# Native Page revision\n\noldpagetoken appears once.\n"
    new = b"# Native Page revision\n\nnewpagetoken appears once.\n"
    page_path = source / "owned.md"
    page_path.write_bytes(old)
    replacement = staging / "new.md"
    replacement.write_bytes(new)
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "oldSha256": hashlib.sha256(old).hexdigest(),
        "newSha256": hashlib.sha256(new).hexdigest()}
    process = None

    def find(root_node, role, name):
        return next((node for node in tree(root_node) if node.get_role_name() == role and
                     node.get_name() == name), None)

    def banner(viewer):
        return next((node.get_name() for node in tree(viewer) if
                     node.get_role_name() == "label" and
                     node.get_state_set().contains(Atspi.StateType.SHOWING) and
                     (node.get_name() or "").startswith("Refresh failed:")), None)

    def page_find(viewer, query):
        entry = wait_for(lambda: find(viewer, "entry", "Find in file"), "Page Find field")
        # Repeating the same query after a failed refresh must issue a fresh
        # native-engine search rather than reuse a prior result/status.
        if not entry.get_editable_text().set_text_contents(""):
            raise AssertionError("Could not reset the Page Find query")
        if not entry.get_editable_text().set_text_contents(query):
            raise AssertionError("Could not enter the Page Find query")
        next_button = wait_for(lambda: find(viewer, "button", "Find next match"), "Page Find next")
        action(next_button, "Page Find next")
        wait_for(lambda: find(viewer, "label", "1 match"), f"one rendered {query!r} match",
                 seconds=20)
        status = wait_for(lambda: next((node.get_name() for node in tree(viewer)
            if node.get_role_name() == "label" and
            f'Found "{query}" in the page.' in (node.get_name() or "")), None),
            f"rendered {query!r} status", seconds=20)
        return {"count": "1 match", "status": status}

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
        result["stage"] = "open native Page"
        row = wait_for(lambda: next((node for node in tree(app) if
                       node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith("owned.md,")), None),
                       "owned Markdown row")
        if not row.grab_focus():
            raise AssertionError("Could not focus owned Markdown")
        press(helper, sudo_password, "f3", process.pid)
        title = "owned.md — FileCat Viewer"
        viewer = wait_for(lambda: find(app, "frame", title), "native Page viewer")
        initial_id = viewer_id(title, process.pid)
        wait_for(lambda: find(viewer, "label", "Mode: Page"), "Page mode")
        action(wait_for(lambda: find(viewer, "button", "Show or hide viewer Find bar"),
                        "Page Find toggle"), "show Page Find")
        result["oldPageFind"] = page_find(viewer, "oldpagetoken")
        result["stage"] = "changed Page refresh"
        os.replace(replacement, page_path)
        refresh = wait_for(lambda: find(viewer, "button", "Refresh viewer content") or
                           find(viewer, "push button", "Refresh viewer content"), "Page Refresh")
        action(refresh, "Page Refresh")
        wait_for(lambda: refresh.get_state_set().contains(Atspi.StateType.ENABLED),
                 "Page Refresh completion", seconds=25)
        result["newPageFind"] = page_find(viewer, "newpagetoken")
        result["sameViewer"] = viewer_id(title, process.pid) == initial_id
        if banner(viewer) or not result["sameViewer"]:
            raise AssertionError("Successful Page Refresh left a failure or changed window")
        result["stage"] = "missing Page rollback"
        retired = staging / "last-good.md"
        os.replace(page_path, retired)
        action(refresh, "missing Page Refresh")
        result["failedRefreshBanner"] = wait_for(lambda: banner(viewer),
                                                 "missing Page failure", seconds=25)
        try:
            result["retainedPageFind"] = page_find(viewer, "newpagetoken")
        except AssertionError:
            result["labelsAfterFailedRefresh"] = [node.get_name() for node in tree(viewer)
                if node.get_role_name() == "label" and
                node.get_state_set().contains(Atspi.StateType.SHOWING)]
            result["pageNodesAfterFailedRefresh"] = [(node.get_role_name(), node.get_name())
                for node in tree(viewer) if "document" in node.get_role_name().lower() or
                "web" in node.get_role_name().lower()]
            raise
        result["sameViewerAfterFailure"] = viewer_id(title, process.pid) == initial_id
        os.replace(retired, page_path)
        result["restoredSourceSha256"] = hashlib.sha256(page_path.read_bytes()).hexdigest()
        result["targetEmpty"] = not any(target.iterdir())
        result["passed"] = (result["sameViewerAfterFailure"] and result["targetEmpty"] and
                            result["restoredSourceSha256"] == result["newSha256"])
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
