#!/usr/bin/env python3
"""Native macOS hidden-mark bulk rename through a configured external editor.

Usage: python3 mac-ax-rename-editor.py BUNDLE.app REPORT.json [--narrow]
Runs only in the unlocked, isolated ~/FileCatValidation desktop session.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import signal
import subprocess
import sys
import time
from uuid import uuid4


shared = runpy.run_path(str(Path(__file__).with_name("mac-ax-panel-sort.py")))
capture = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))["capture"]
osa = shared["osa"]
click = shared["click"]
wait_for = shared["wait_for"]
app_pids = shared["app_pids"]
foreground = shared["FOREGROUND"]
narrow_window = shared["NARROW"]


def center(node):
    x, y, width, height = node["bounds"]
    if width < 8 or height < 8:
        raise AssertionError(f"Unusable native target bounds: {node}")
    return round(x + min(40, width / 2)), round(y + height / 2)


def item(nodes, role, name):
    found = [node for node in nodes if node["role"] == role and node["name"] == name]
    if len(found) != 1:
        raise AssertionError(f"Expected one {role} {name!r}, found {len(found)}")
    return found[0]


def click_item(nodes, role, name):
    x, y = center(item(nodes, role, name))
    click(f"c:{x},{y}")


def key(script):
    osa('tell application "System Events" to tell process "FileCat"\n'
        'set frontmost to true\n' + script + '\nend tell')


def ax_activate(role, name, focus=False):
    script = '''tell application "System Events"
    tell process "FileCat"
        set allElements to entire contents of window 1
        repeat with e in allElements
            try
                if (role of e as text) is ROLE and (name of e as text) is NAME then
                    if SHOULD_FOCUS then set focused of e to true
                    click e
                    return "activated"
                end if
            end try
        end repeat
        error "The named control could not be activated"
    end tell
end tell'''.replace("ROLE", json.dumps(role, ensure_ascii=False)).replace(
        "NAME", json.dumps(name, ensure_ascii=False)).replace(
        "SHOULD_FOCUS", "true" if focus else "false")
    if osa(script) != "activated":
        raise AssertionError(f"Could not activate the {name} native AX control")


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
target.write_text("editor_b.txt\\neditor_a.txt\\n", encoding="utf-8")
(root / "editor-done").write_text(target.name, encoding="utf-8")
''', encoding="utf-8")
    settings = {"SchemaVersion": 1, "Editor": {
        "Name": "Controlled native rename editor", "Executable": sys.executable,
        "Arguments": [str(editor), "{file}"]}}
    (fixture / "data" / "settings.json").write_text(json.dumps(settings), encoding="utf-8")


def run(bundle, report_path, narrow):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    if bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file():
        raise AssertionError("Use an isolated signed FileCat bundle under FileCatValidation")
    if report_path.parent.resolve() != validation:
        raise AssertionError("Write the native report under FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes this fixture ambiguous")
    fixture = validation / "MacRenameEditor" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    configure_editor(fixture)
    helper_source = Path(__file__).with_name("mac-ax-rename-snapshot.swift")
    helper = fixture / "ax-rename-snapshot"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    built = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(helper_source), "-o", str(helper)],
                           capture_output=True, text=True, timeout=40)
    if built.returncode:
        raise AssertionError("Direct AX helper build failed: " + built.stderr[-1200:])
    pid = None
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                        "--data", str(data), "--left", str(source), "--right", str(target)],
                       check=True, capture_output=True, timeout=15)
        pid = wait_for(lambda: next(iter(app_pids()), None), "the isolated Mac app")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 check=True, capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The process is not the exact isolated app")
        osa(foreground)

        def nodes():
            completed = subprocess.run([str(helper), str(executable), str(pid)],
                                       capture_output=True, text=True, timeout=15)
            if completed.returncode:
                raise AssertionError(completed.stderr.strip() or "Direct AX snapshot failed")
            snapshot = json.loads(completed.stdout)
            if snapshot["visited"] >= 5000 or snapshot["windows"] < 1:
                raise AssertionError("The Mac AX tree is unavailable or unbounded")
            (fixture / "last-snapshot.json").write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
            return snapshot["nodes"]

        def rows():
            found = nodes()
            return found if all(any(n["role"] == "AXRow" and n["name"] == name for n in found)
                                for name in expected) else None

        initial = wait_for(rows, "both exact source rows")
        if narrow:
            size = tuple(int(value) for value in osa(narrow_window).split("|"))
            if not (700 <= size[0] <= 800 and 550 <= size[1] <= 650):
                raise AssertionError(f"The narrow window did not settle: {size}")
            initial = wait_for(rows, "reflowed source rows")
        else:
            size = None
        click_item(initial, "AXRow", "a.txt")
        bx, by = center(item(initial, "AXRow", "b.txt"))
        click("kd:shift", f"c:{bx},{by}", "ku:shift")

        def marked():
            found = nodes()
            marks = {n["name"] for n in found if n["role"] == "AXRow" and n["selected"]}
            return found if marks == {"a.txt", "b.txt"} else None

        selected = wait_for(marked, "both selected source identities")
        filters = sorted((n for n in selected if n["role"] == "AXTextField" and n["name"] == "Filter"),
                         key=lambda node: node["bounds"][0])
        if len(filters) != 2:
            raise AssertionError("Both panel Filter fields are required")
        x, y = center(filters[0])
        click(f"c:{x},{y}")
        key('key code 0 using {command down}\nkeystroke "b*"\nkey code 36')

        def filtered():
            found = nodes()
            names = {n["name"] for n in found if n["role"] == "AXRow" and n["name"] in expected}
            return found if names == {"b.txt"} else None

        wait_for(filtered, "the filtered marked source")
        key("key code 46 using {control down}")  # Ctrl+M: Bulk rename.

        hidden_name = "Include 1 marked item hidden by the filter"
        def review():
            found = nodes()
            return found if any(n["role"] == "AXCheckBox" and n["name"] == hidden_name
                                for n in found) else None

        try:
            opened = wait_for(review, "the bulk rename scope review")
        except AssertionError as exc:
            image = capture(validation / "FileCatCapture.app", validation)
            raise AssertionError(f"{exc}; native capture: {image}") from exc
        if item(opened, "AXCheckBox", hidden_name)["value"] != "0":
            raise AssertionError("Hidden marks were included before the user's choice")
        ax_activate("AXCheckBox", hidden_name)
        def mask_field():
            found = nodes()
            return found if any(n["role"] == "AXTextField" and n["name"] == "Name mask"
                                for n in found) else None

        focused = wait_for(mask_field, "the Name mask")
        item(focused, "AXTextField", "Name mask")
        ax_activate("AXTextField", "Name mask", focus=True)
        key('key code 0 using {command down}\nkeystroke "reviewed_[C]"')

        def proposed():
            found = nodes()
            summary = "2 of 2 names change · includes 1 hidden marked items"
            detail = next((n["value"] for n in found if n["role"] == "AXTextField" and
                           n["name"] == "Full old and new paths of selected rename"), "")
            included = any(n["role"] == "AXCheckBox" and n["name"] == hidden_name and
                           n["value"] == "1" for n in found)
            if (included and summary in {n["name"] for n in found} and
                    detail == f"From: {source / 'b.txt'}\nTo: {source / 'reviewed_1.txt'}"):
                return found
            return None

        preview = wait_for(proposed, "both reviewed rename mappings")
        item(preview, "AXButton", "Next rename mapping")
        first_status = "1 of 2 shown"
        if not any(n["role"] == "AXStaticText" and n["name"] == first_status for n in preview):
            raise AssertionError("The first mapping position is not exposed")
        ax_activate("AXButton", "Next rename mapping")
        second_status = "2 of 2 shown"
        def second_mapping():
            found = nodes()
            detail = next((n["value"] for n in found if n["role"] == "AXTextField" and
                           n["name"] == "Full old and new paths of selected rename"), "")
            if (detail == f"From: {source / 'a.txt'}\nTo: {source / 'reviewed_2.txt'}" and
                            any(n["role"] == "AXStaticText" and n["name"] == second_status for n in found)):
                return found
            return None
        wait_for(second_mapping, "second full-path mapping through the native Next action")
        ax_activate("AXButton", "Previous rename mapping")
        wait_for(proposed, "first full-path mapping through the native Previous action")
        item(preview, "AXButton", "Edit names in editor…")
        visual = capture(validation / "FileCatCapture.app", validation)
        ax_activate("AXButton", "Edit names in editor…")

        done = fixture / "editor-done"
        wait_for(lambda: done if done.is_file() else None, "the configured editor to save exact names")
        def load_names_decision():
            found = nodes()
            return found if any(n["role"] == "AXButton" and n["name"] == "Load names"
                                for n in found) else None

        confirmation = wait_for(load_names_decision, "the nested Load names decision")
        item(confirmation, "AXButton", "Load names")
        ax_activate("AXButton", "Load names")

        def edited():
            found = nodes()
            summary = "2 of 2 names change · includes 1 hidden marked items"
            detail = next((n["value"] for n in found if n["role"] == "AXTextField" and
                           n["name"] == "Full old and new paths of selected rename"), "")
            if (any(n["name"].startswith(summary + " · names come from the editor") for n in found) and
                    detail == f"From: {source / 'b.txt'}\nTo: {source / 'editor_b.txt'}"):
                return found
            return None

        final_preview = wait_for(edited, "both editor-supplied mappings")
        if list((data / "local" / "temp").glob("rename-*.txt")):
            raise AssertionError("The private edited-name list was not removed")
        card = item(final_preview, "AXGroup", "Bulk rename dialog")
        cx, cy, cw, ch = card["bounds"]
        rename_buttons = [n for n in final_preview if n["role"] == "AXButton" and n["name"] == "Rename"
                          and cx <= center(n)[0] < cx + cw and cy <= center(n)[1] < cy + ch]
        if len(rename_buttons) != 1:
            raise AssertionError(f"Expected one approval action inside the review, found {len(rename_buttons)}")
        rename = rename_buttons[0]
        x, y = center(rename)
        click(f"c:{x},{y}")

        def file_result():
            if not (source / "editor_a.txt").exists() or not (source / "editor_b.txt").exists():
                return False
            return ((source / "editor_a.txt").read_bytes() == expected["a.txt"]
                    and (source / "editor_b.txt").read_bytes() == expected["b.txt"]
                    and not (source / "a.txt").exists() and not (source / "b.txt").exists()
                    and {entry.name for entry in source.iterdir()} == {"editor_a.txt", "editor_b.txt"}
                    and not list(target.iterdir()))

        wait_for(file_result, "the exact two-file native rename result")
        return {"passed": True, "platform": "macOS", "narrow": narrow, "windowSize": size,
                "appSha256": hashlib.sha256((bundle / "Contents/MacOS/FileCat.dll").read_bytes()).hexdigest(),
                "initialSha256": {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()},
                "sourceNames": sorted(p.name for p in source.iterdir()), "targetEmpty": True,
                "reviewedNames": ["reviewed_1.txt", "reviewed_2.txt"],
                "mappingNavigation": [first_status, second_status, first_status],
                "editedNames": ["editor_b.txt", "editor_a.txt"],
                "editorActionCapture": visual, "privateListRemoved": True,
                "fixture": str(fixture)}
    finally:
        if pid is not None:
            command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                     capture_output=True, text=True, timeout=5).stdout.strip()
            if command.startswith(str(executable) + " --new-instance --data " + str(data)):
                os.kill(pid, signal.SIGTERM)


def main():
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != "--narrow"):
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2])
    try:
        result = run(Path(sys.argv[1]), report_path, len(sys.argv) == 4)
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
