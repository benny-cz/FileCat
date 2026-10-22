#!/usr/bin/env python3
"""Native Ubuntu bulk rename round trip through the configured external editor.

Usage: linux-atspi-rename-editor.py APP_PATH REPORT.json KEY_HELPER [--narrow|--rule-change]
Run in the logged-in desktop with a Git-built app under ~/FileCatValidation.
FILECAT_NATIVE_SUDO_PASSWORD is used only for one physical Ctrl+M gesture and
is removed from the child app/editor environment before either starts.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


shared = runpy.run_path(str(Path(__file__).with_name("linux-atspi-dialog-pointer-dismiss.py")))
descendants = shared["descendants"]
named = shared["named"]
wait_for = shared["wait_for"]
activate_window = shared["activate_window"]


def action(node, description):
    if node is None or node.get_n_actions() < 1 or not node.do_action(0):
        raise AssertionError(f"The {description} native accessibility action failed")


def one(root, role, name):
    found = named(root, role, name)
    if len(found) != 1:
        raise AssertionError(f"Expected one {role} {name!r}, found {len(found)}")
    return found[0]


def configure_editor(fixture):
    editor = fixture / "controlled-editor.py"
    editor.write_text('''#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parent
target = Path(sys.argv[1]).resolve(strict=True)
if target.parent != root / "data" / "local" / "temp" or not re.fullmatch(r"rename-[0-9a-f]{32}\\.txt", target.name):
    raise SystemExit("Controlled editor target left the isolated fixture")
if target.read_text(encoding="utf-8").splitlines() != ["reviewed_1.txt", "reviewed_2.txt"]:
    raise SystemExit("Controlled editor received another mapping")
target.write_text("editor_a.txt\\neditor_b.txt\\n", encoding="utf-8")
(root / "editor-done").write_text(target.name, encoding="utf-8")
''', encoding="utf-8")
    (fixture / "data" / "settings.json").write_text(json.dumps({
        "SchemaVersion": 1,
        "Editor": {"Name": "Controlled native rename editor",
                   "Executable": sys.executable,
                   "Arguments": [str(editor), "{file}"]},
    }), encoding="utf-8")


def run(app_path, report_path, key_helper, narrow, rule_change):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent.resolve() != validation):
        raise AssertionError("Use an isolated Git-built app, owned input helper and report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient guest authentication is required for physical Ctrl+M")
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "RenameEditorFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in original.items():
        (source / name).write_bytes(content)
    configure_editor(fixture)
    app_sha = hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest()
    process = None
    result = {"passed": False, "platform": "Ubuntu", "narrow": narrow,
              "ruleChangeAfterEditor": rule_change,
              "appSha256": app_sha, "fixture": str(fixture), "steps": []}
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat" and
                named(candidate, "list", f"Panel 1, source, {source}")), None),
                "the exact isolated source panel", seconds=25)
            activate_window(process.pid)
            if narrow:
                shown = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                                       capture_output=True, text=True, timeout=5).stdout
                match = re.search(r"\bScale: ([0-9.]+)", shown)
                if not match:
                    raise AssertionError("Native display scale unavailable for narrow review")
                scale = float(match.group(1))
                if not 0.5 <= scale <= 4:
                    raise AssertionError(f"Unexpected native display scale: {scale}")
                result["displayScale"] = scale
                requested = (round(720 * scale), round(540 * scale))
                listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                                         capture_output=True, text=True, timeout=5).stdout
                ids = [line.split()[0] for line in listing.splitlines()
                       if len(line.split()) > 2 and line.split()[2] == str(process.pid)]
                if len(ids) != 1:
                    raise AssertionError(f"Expected one owned window, found {ids}")
                subprocess.run(["/usr/bin/wmctrl", "-ir", ids[0], "-b",
                                "remove,maximized_vert,maximized_horz"], check=True, timeout=5)
                subprocess.run(["/usr/bin/wmctrl", "-ir", ids[0], "-e",
                                f"0,100,80,{requested[0]},{requested[1]}"],
                               check=True, timeout=5)
                def settled_geometry():
                    geometry = subprocess.run(["/usr/bin/xdotool", "getwindowgeometry", "--shell", ids[0]],
                                              check=True, capture_output=True, text=True, timeout=5).stdout
                    values = dict(line.split("=", 1) for line in geometry.splitlines() if "=" in line)
                    result["windowGeometry"] = values
                    result["windowSizeDip"] = [int(values["WIDTH"]) / scale,
                                               int(values["HEIGHT"]) / scale]
                    if (700 <= result["windowSizeDip"][0] <= 800 and
                            500 <= result["windowSizeDip"][1] <= 600):
                        return values
                    return None
                try:
                    result["windowGeometry"] = wait_for(settled_geometry, "720 by 540 narrow window", seconds=2)
                except AssertionError:
                    subprocess.run(["/usr/bin/xdotool", "windowsize", ids[0],
                                    str(requested[0]), str(requested[1])],
                                   check=True, timeout=5)
                    result["windowGeometry"] = wait_for(settled_geometry, "720 by 540 DIP narrow window")
            row = wait_for(lambda: next(iter(named(app, "list item", "a.txt")), None), "a.txt row")
            if not row.grab_focus():
                raise AssertionError("Could not focus the exact source row")

            def send(chord):
                activate_window(process.pid)
                sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                      input=sudo_password + "\n", capture_output=True,
                                      text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")

            wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED), "a.txt focus")
            send("insert")
            wait_for(lambda: one(app, "list item", "a.txt").get_state_set().contains(Atspi.StateType.SELECTED)
                     and one(app, "list item", "b.txt").get_state_set().contains(Atspi.StateType.FOCUSED),
                     "marked a.txt and focused b.txt")
            send("insert")
            wait_for(lambda: all(one(app, "list item", name).get_state_set().contains(Atspi.StateType.SELECTED)
                                 for name in original), "both exact marks")
            result["steps"].append("Physical Insert marked two source identities")
            send("ctrl+m")
            wait_for(lambda: named(app, "entry", "Name mask") and
                     named(app, "button", "Edit names in editor…"), "bulk rename review")
            mask = one(app, "entry", "Name mask")
            if not mask.get_editable_text().set_text_contents("reviewed_[C]"):
                raise AssertionError("Could not set the reviewed rename mask")
            summary = "2 of 2 names change"
            wait_for(lambda: any(node.get_name().startswith(summary) for node in descendants(app)),
                     "two-name preview")
            result["steps"].append("Two-name preview and editor action available")
            action(one(app, "button", "Edit names in editor…"), "editor")
            wait_for(lambda: (fixture / "editor-done").is_file(), "controlled editor save")
            action(wait_for(lambda: next(iter(named(app, "button", "Load names")), None),
                            "nested Load names decision"), "Load names")
            wait_for(lambda: any("names come from the editor" in node.get_name()
                                 for node in descendants(app)), "editor-derived preview")
            if list((data / "local" / "temp").glob("rename-*.txt")):
                raise AssertionError("Private editor list was not removed")
            result["steps"].append("Editor names loaded and private list removed")
            if rule_change:
                if not one(app, "entry", "Name mask").get_editable_text().set_text_contents("rule_[C]"):
                    raise AssertionError("Could not change the rule after editor names were loaded")
                wait_for(lambda: any(node.get_name().startswith(summary) and
                                     "names come from the editor" not in node.get_name()
                                     for node in descendants(app)), "rule-derived preview to replace editor names")
                result["steps"].append("Changing a rule retired the editor-derived mapping")
            rename = named(app, "button", "Rename")
            def ancestry(node):
                chain = []
                while node is not None and len(chain) < 16:
                    chain.append((node.get_role_name(), node.get_name()))
                    node = node.get_parent()
                return chain
            result["renameCandidates"] = [ancestry(node) for node in rename]
            approvals = [node for node in rename if ("panel", "Bulk rename dialog") in ancestry(node)
                         or ("group", "Bulk rename dialog") in ancestry(node)]
            if len(approvals) != 1:
                raise AssertionError(f"Expected one modal-owned Rename approval, found {len(approvals)}")
            action(approvals[0], "Rename approval")

            expected_names = {"rule_1.txt", "rule_2.txt"} if rule_change else {
                "editor_a.txt", "editor_b.txt"}
            mapped = {"a.txt": "rule_1.txt" if rule_change else "editor_a.txt",
                      "b.txt": "rule_2.txt" if rule_change else "editor_b.txt"}

            def renamed():
                return ({entry.name for entry in source.iterdir()} == expected_names and
                        all((source / mapped[name]).read_bytes() == content
                            for name, content in original.items()) and
                        not list(target.iterdir()))

            wait_for(renamed, "exact two-file rename with unchanged bytes", seconds=25)
            result.update(passed=True, sourceNames=sorted(entry.name for entry in source.iterdir()),
                          targetEmpty=True, privateListRemoved=True,
                          originalSha256={name: hashlib.sha256(content).hexdigest()
                                          for name, content in original.items()})
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
    report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) not in (4, 5) or len(sys.argv) == 5 and sys.argv[4] not in ("--narrow", "--rule-change"):
        raise SystemExit(__doc__)
    raise SystemExit(run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]),
                         len(sys.argv) == 5 and sys.argv[4] == "--narrow",
                         len(sys.argv) == 5 and sys.argv[4] == "--rule-change"))
