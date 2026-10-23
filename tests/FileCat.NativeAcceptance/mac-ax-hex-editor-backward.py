#!/usr/bin/env python3
"""Exercise native Mac hex-editor Previous/F3 search on a test-owned file.

Usage: python3 mac-ax-hex-editor-backward.py SOURCE REPORT.json
SOURCE is the isolated FileCatValidation panel containing a.txt and b.txt.
"""

import hashlib
import json
from pathlib import Path
import runpy
import subprocess
import sys
import time


base = runpy.run_path(str(Path(__file__).with_name("mac-ax-viewer-backward.py")))
CONTENT = base["CONTENT"]
FIRST_OFFSET = base["FIRST_OFFSET"]
SECOND_OFFSET = base["SECOND_OFFSET"]
find_click = base["find_click"]
osa = base["osa"]
FIXTURE_NAME = "native-editor-backward-search.bin"


def editor_open():
    return osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then return "yes"
        end repeat
    end tell
end tell
return "no"
''') == "yes"


def editor_click(role, name):
    return osa(f'''
on clickNamed(itemRef, soughtRole, soughtName, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is soughtRole and (name of itemRef as text) is soughtName then
                click itemRef
                return true
            end if
            repeat with childRef in (UI elements of itemRef)
                if my clickNamed(childRef, soughtRole, soughtName, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end clickNamed
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "{FIXTURE_NAME} — FileCat Hex Editor" then
                if my clickNamed(w, {json.dumps(role)}, {json.dumps(name)}, 0) then return "clicked"
            end if
        end repeat
        error "Missing editor {role}: {name}"
    end tell
end tell
''')


def open_editor():
    osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then error "Editor already open"
            if (name of w as text) contains "FileCat Viewer" then error "Close the viewer first"
        end repeat
        key code 35 using {control down, shift down}
        delay 0.25
        set commandField to value of attribute "AXFocusedUIElement"
        if (role of commandField as text) is not "AXTextField" or (name of commandField as text) is not "Search commands" then
            error "Command shortcut did not focus the Search commands field"
        end if
        keystroke "a" using {command down}
        key code 51
        keystroke "edit file bytes in hex"
        if (value of commandField as text) is not "edit file bytes in hex" then
            error "Command-search query differed"
        end if
        key code 36
        return "submitted"
    end tell
end tell
''')
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if editor_open():
            return
        time.sleep(0.2)
    raise AssertionError("The native hex editor did not open")


def ensure_source_panel():
    title = osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "FileCat" then return name of w as text
        end repeat
        error "No named FileCat window"
    end tell
end tell
''')
    if title.startswith("source — FileCat"):
        return
    try:
        find_click("AXRow", "source")
    except AssertionError as exc:
        raise AssertionError(f"Cannot reopen isolated source from {title!r}: {exc}") from exc
    osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 36
    end tell
end tell
''')
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        if osa('''tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) starts with "source — FileCat" then return "yes"
    end repeat
    return "no"
end tell''') == "yes":
            return
        time.sleep(0.2)
    raise AssertionError("The isolated source panel did not reopen")


def enter_query(query, visible_controls=False, first=False):
    if visible_controls:
        if first:
            editor_click("AXButton", "Show or hide hex editor Find bar")
        editor_click("AXTextField", "Find in file")
        osa(f'''
tell application "System Events" to tell process "FileCat"
    set frontmost to true
    set findField to value of attribute "AXFocusedUIElement"
    if (role of findField as text) is not "AXTextField" or (name of findField as text) is not "Find in file" then
        error "Visible Find click did not focus the query; actual " & (role of findField as text) & " / " & (name of findField as text)
    end if
    keystroke "a" using {{command down}}
    key code 51
    keystroke {json.dumps(query)}
    if (value of findField as text) is not {json.dumps(query)} then
        error "Visible Find query input differed: " & (value of findField as text)
    end if
    key code 36
    return "searched"
end tell
''')
        return
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 3 using {{control down}}
        set findField to missing value
        repeat 30 times
            delay 0.1
            set findField to value of attribute "AXFocusedUIElement"
            if (role of findField as text) is "AXTextField" and (name of findField as text) is "Find in file" then exit repeat
        end repeat
        if (role of findField as text) is not "AXTextField" or (name of findField as text) is not "Find in file" then
            error "Editor Ctrl+F did not focus Find in file; actual " & (role of findField as text) & " / " & (name of findField as text) & "; front window " & (name of window 1 as text)
        end if
        keystroke "a" using {{command down}}
        key code 51
        keystroke {json.dumps(query)}
        if (value of findField as text) is not {json.dumps(query)} then error "Editor query input differed"
        key code 36
        return "searched"
    end tell
end tell
''')


def editor_status(fragment):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        result = osa('''
on findStatus(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXStaticText" then
                set labelText to name of itemRef as text
                if labelText contains "Found at 0x" or labelText contains "was not found before the cursor" then return labelText
            end if
            repeat with childRef in (UI elements of itemRef)
                set resultText to my findStatus(childRef, depth + 1)
                if resultText is not "" then return resultText
            end repeat
        end try
    end tell
    return ""
end findStatus
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then
                return my findStatus(w, 0)
            end if
        end repeat
    end tell
end tell
return ""
''')
        if fragment in result:
            return result
        time.sleep(0.2)
    raise AssertionError(f"Editor never displayed {fragment!r}; last status {result!r}")


def editor_match_count(fragment):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        result = osa('''
on findCount(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXStaticText" then
                set labelText to name of itemRef as text
                if labelText contains "matches visited" or labelText contains "match visited" then return labelText
            end if
            repeat with childRef in (UI elements of itemRef)
                set resultText to my findCount(childRef, depth + 1)
                if resultText is not "" then return resultText
            end repeat
        end try
    end tell
    return ""
end findCount
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then
                return my findCount(w, 0)
            end if
        end repeat
    end tell
end tell
return ""
''')
        if fragment in result:
            return result
        time.sleep(0.2)
    raise AssertionError(f"Editor never displayed match status {fragment!r}; last {result!r}")


def editor_save_state():
    return osa('''
on findState(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXStaticText" then
                set labelText to name of itemRef as text
                if labelText contains "No unsaved changes" or labelText contains "modified (unsaved)" then return labelText
            end if
            repeat with childRef in (UI elements of itemRef)
                set resultText to my findState(childRef, depth + 1)
                if resultText is not "" then return resultText
            end repeat
        end try
    end tell
    return ""
end findState
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "native-editor-backward-search.bin — FileCat Hex Editor" then
                return my findState(w, 0)
            end if
        end repeat
    end tell
end tell
return ""
''')


def editor_size(width, height):
    actual = osa(f'''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "{FIXTURE_NAME} — FileCat Hex Editor" then
            set size of w to {{{width}, {height}}}
            set position of w to {{80, 60}}
            perform action "AXRaise" of w
            set s to size of w
            return (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
    end repeat
end tell
error "Owned Hex editor absent during resize"
''')
    size = [int(part) for part in actual.split("|")]
    if len(size) != 2 or not (620 <= size[0] <= 1200 and 360 <= size[1] <= 850):
        raise AssertionError(f"Mac Hex editor returned an implausible native frame: {actual!r}")
    return size


def editor_frame():
    raw = osa(f'''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "{FIXTURE_NAME} — FileCat Hex Editor" then
            set p to position of w
            set s to size of w
            return (item 1 of p as text) & "|" & (item 2 of p as text) & "|" & ¬
                (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
    end repeat
end tell
error "Owned Hex editor absent during frame inspection"
''')
    frame = [int(value) for value in raw.split("|")]
    if len(frame) != 4 or frame[2] < 620 or frame[3] < 360:
        raise AssertionError(f"Invalid native Hex editor frame: {raw!r}")
    return frame


def pointer_resize(target_width, target_height, screenshot=False):
    before = editor_frame()
    screenshot_path = None
    if screenshot:
        capture_dir = Path.home() / "FileCatValidation"
        capture_app = capture_dir / "FileCatCapture.app"
        captures = set(capture_dir.glob("native-screen-*.png"))
        shot = subprocess.run(["/usr/bin/open", "-a", str(capture_app)],
                              capture_output=True, text=True, timeout=30)
        if shot.returncode:
            raise AssertionError(shot.stderr.strip() or "Approved Mac capture failed")
        for _ in range(50):
            added = sorted(set(capture_dir.glob("native-screen-*.png")) - captures,
                           key=lambda path: path.stat().st_mtime_ns)
            if added:
                screenshot_path = str(added[-1])
                break
            time.sleep(0.2)
        if screenshot_path is None:
            raise AssertionError("Approved Mac capture produced no image")
    x, y, width, height = before
    start = (x + width - 1, y + height - 1)
    target = (start[0] - max(0, width - target_width),
              start[1] - max(0, height - target_height))
    done = subprocess.run(["/opt/homebrew/bin/cliclick", "-r", "-e", "4", "-w", "100",
                           f"dd:{start[0]},{start[1]}", f"dm:{target[0]},{target[1]}",
                           f"du:{target[0]},{target[1]}"], capture_output=True,
                          text=True, timeout=15)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "Native Hex editor pointer drag failed")
    time.sleep(0.5)
    after = editor_frame()
    return {"before": before, "after": after, "screenshot": screenshot_path, "dragStart": start,
            "dragEnd": target, "shrunk": after[2] < width - 40 and after[3] < height - 40}


def editor_find_layout():
    raw = osa('''
on rectText(itemRef)
    tell application "System Events"
        set p to position of itemRef
        set s to size of itemRef
        return (item 1 of p as text) & "," & (item 2 of p as text) & "," & ¬
            (item 1 of s as text) & "," & (item 2 of s as text)
    end tell
end rectText
on findRect(itemRef, soughtRole, soughtName, depth)
    if depth > 25 then return ""
    tell application "System Events"
        try
            if (role of itemRef as text) is soughtRole and (name of itemRef as text) is soughtName then
                return my rectText(itemRef)
            end if
            repeat with childRef in UI elements of itemRef
                set candidate to my findRect(childRef, soughtRole, soughtName, depth + 1)
                if candidate is not "" then return candidate
            end repeat
        end try
    end tell
    return ""
end findRect
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "native-editor-backward-search.bin — FileCat Hex Editor" then
            set frameRect to my rectText(w)
            set queryRect to my findRect(w, "AXTextField", "Find in file", 0)
            set nextRect to my findRect(w, "AXButton", "Find next match", 0)
            set previousRect to my findRect(w, "AXButton", "Find previous match", 0)
            set countRect to my findRect(w, "AXStaticText", "2 distinct matches visited · total not counted", 0)
            return frameRect & "|" & queryRect & "|" & nextRect & "|" & previousRect & "|" & countRect
        end if
    end repeat
end tell
error "Owned Hex editor absent during layout inspection"
''')
    parts = raw.split("|")
    if len(parts) != 5:
        raise AssertionError(f"Incomplete native Hex editor layout: {raw!r}")
    names = ("frame", "query", "next", "previous", "status")
    layout = {}
    for name, part in zip(names, parts):
        values = [int(value) for value in part.split(",")]
        if len(values) != 4 or values[2] < 8 or values[3] < 8:
            raise AssertionError(f"Unusable native {name} bounds: {part!r}")
        layout[name] = values
    fx, fy, fw, fh = layout["frame"]
    for name in names[1:]:
        x, y, width, height = layout[name]
        if not (fx <= x and fy <= y and x + width <= fx + fw and y + height <= fy + fh):
            raise AssertionError(f"Native {name} control is outside the Hex editor window")
    return layout


def refocus_find_with_ctrl_f():
    try:
        return {"input": "System Events", "focused": osa('''
tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 3 using {control down}
    set focusedElement to missing value
    repeat 30 times
        delay 0.1
        set focusedElement to value of attribute "AXFocusedUIElement"
        if (role of focusedElement as text) is "AXTextField" and (name of focusedElement as text) is "Find in file" then
            return "Find in file"
        end if
    end repeat
    error "Ctrl+F after search navigation did not refocus Find; actual " & ¬
        (role of focusedElement as text) & " / " & (name of focusedElement as text)
end tell
''')}
    except AssertionError as synthetic_failure:
        helper = Path(__file__).with_name("mac-send-key-find")
        native = subprocess.run([str(helper), "find"],
                                capture_output=True, text=True, timeout=30)
        if native.returncode:
            raise AssertionError(f"Native Ctrl+F failed after System Events: "
                                 f"{native.stderr.strip()}; {synthetic_failure}")
        focused = osa('''
tell application "System Events" to tell process "FileCat"
    set focusedElement to missing value
    repeat 30 times
        delay 0.1
        set focusedElement to value of attribute "AXFocusedUIElement"
        if (role of focusedElement as text) is "AXTextField" and (name of focusedElement as text) is "Find in file" then
            return "Find in file"
        end if
    end repeat
    error "Native Ctrl+F did not refocus Find; actual " & ¬
        (role of focusedElement as text) & " / " & (name of focusedElement as text)
end tell
''')
        return {"input": "CGEvent after System Events failed",
                "syntheticFailure": str(synthetic_failure), "focused": focused}


def editor_set_query(query):
    actual = osa(f'''
on setQuery(itemRef, newValue, depth)
    if depth > 25 then return ""
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXTextField" and (name of itemRef as text) is "Find in file" then
                set value of itemRef to newValue
                return value of itemRef as text
            end if
            repeat with childRef in UI elements of itemRef
                set resultText to my setQuery(childRef, newValue, depth + 1)
                if resultText is not "" then return resultText
            end repeat
        end try
    end tell
    return ""
end setQuery
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "{FIXTURE_NAME} — FileCat Hex Editor" then
            return my setQuery(w, {json.dumps(query)}, 0)
        end if
    end repeat
end tell
return ""
''')
    if actual != query:
        raise AssertionError(f"Native Hex query did not change to {query!r}: {actual!r}")


def send_f3(backward):
    modifier = " using {shift down}" if backward else ""
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 99{modifier}
    end tell
end tell
''')


def close_editor():
    if not editor_open():
        return
    osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then
                repeat with e in UI elements of w
                    try
                        if (description of e as text) is "close button" then
                            click e
                            return "closed"
                        end if
                    end try
                end repeat
            end if
        end repeat
        error "Editor close button absent"
    end tell
end tell
''')
    time.sleep(0.25)
    if editor_open():
        # The runner owns this exact disposable fixture; never save an accidental
        # unsaved edit made by accessibility input during a failed acceptance run.
        editor_click("AXButton", "Discard edits")
    deadline = time.monotonic() + 5
    while editor_open() and time.monotonic() < deadline:
        time.sleep(0.2)
    if editor_open():
        raise AssertionError("The owned hex editor remained open after close")


def run(source, narrow=False, pointer_probe=False, keyboard_find=False):
    source = source.resolve(strict=True)
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated two-file source fixture is not exact")
    if editor_open():
        raise AssertionError("Close the existing editor before this test")
    fixture = source / FIXTURE_NAME
    if fixture.exists():
        raise AssertionError("The test-owned editor search fixture already exists")
    with fixture.open("xb") as output:
        output.write(CONTENT)
    digest = hashlib.sha256(CONTENT).digest()
    try:
        ensure_source_panel()
        find_click("AXButton", "Reread")
        deadline = time.monotonic() + 12
        while True:
            try:
                find_click("AXRow", FIXTURE_NAME)
                break
            except AssertionError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.25)
        open_editor()
        window_size = editor_size(720, 500) if narrow else None
        if pointer_probe:
            pointer = pointer_resize(620, 380, screenshot=True)
            close_editor()
            source_intact = hashlib.sha256(fixture.read_bytes()).digest() == digest
            return {"passed": pointer["shrunk"] and source_intact and not editor_open(),
                    "platform": "macOS", "mode": "Hex editor pointer resize",
                    "requestedWindowSize": [720, 500], "axWindowSize": window_size,
                    "pointer": pointer, "sourceBytesUnchanged": source_intact,
                    "editorClosed": not editor_open()}
        native_drags = []
        for _ in range(3 if narrow else 0):
            if window_size[0] <= 724 and window_size[1] <= 504:
                break
            drag = pointer_resize(720, 500)
            native_drags.append(drag)
            next_size = drag["after"][2:]
            if next_size == window_size:
                break
            window_size = next_size
        if narrow and (window_size[0] > 724 or window_size[1] > 504):
            raise AssertionError(f"Mac Hex editor could not fit 720x500: {window_size!r}")
        if native_drags:
            time.sleep(0.3)
        enter_query("NEEDLE", visible_controls=narrow and not keyboard_find,
                    first=narrow and not keyboard_find)
        first = editor_status(f"Found at 0x{FIRST_OFFSET:X}")
        first_count = editor_match_count("1 distinct match visited · total not counted")
        editor_click("AXButton", "Find next match")
        second = editor_status(f"Found at 0x{SECOND_OFFSET:X}")
        second_count = editor_match_count("2 distinct matches visited · total not counted")
        refocused_find = refocus_find_with_ctrl_f() if keyboard_find else None
        layout = editor_find_layout() if narrow else None
        editor_click("AXButton", "Find previous match")
        previous = editor_status(f"Found at 0x{FIRST_OFFSET:X}")
        send_f3(backward=False)
        editor_status(f"Found at 0x{SECOND_OFFSET:X}")
        send_f3(backward=True)
        editor_status(f"Found at 0x{FIRST_OFFSET:X}")
        send_f3(backward=True)
        before_first = editor_status("was not found before the cursor")
        if narrow:
            editor_set_query("second")
            editor_click("AXButton", "Find next match")
        else:
            enter_query("second")
        changed = editor_status(f"Found at 0x{SECOND_OFFSET + 7:X}")
        changed_count = editor_match_count("1 distinct match visited · total not counted")
        save_state = editor_save_state()
        if "No unsaved changes" not in save_state:
            raise AssertionError(f"Search left unexpected editor edits: {save_state!r}")
        close_editor()
        if editor_open() or hashlib.sha256(fixture.read_bytes()).digest() != digest:
            raise AssertionError("Editor remained open or changed the source bytes")
        return {"passed": True, "platform": "macOS", "mode": "Hex editor",
                "firstHit": FIRST_OFFSET, "secondHit": SECOND_OFFSET,
                "firstStatus": first, "firstCount": first_count,
                "secondStatus": second, "secondCount": second_count,
                "previousStatus": previous, "noEarlierStatus": before_first,
                "changedQueryStatus": changed, "changedQueryCount": changed_count,
                "saveState": save_state,
                "editorClosed": True,
                "sourceBytesUnchanged": True,
                "requestedWindowSize": [720, 500] if narrow else None,
                "windowSize": window_size, "resizeClamped": window_size != [720, 500] if narrow else None,
                "nativeDrags": native_drags,
                "findOpening": "Ctrl+F" if keyboard_find else "visible Find" if narrow else "Ctrl+F",
                "refocusedFind": refocused_find,
                "findLayout": layout}
    finally:
        close_editor()
        if fixture.exists() and hashlib.sha256(fixture.read_bytes()).digest() == digest:
            fixture.unlink()
            find_click("AXButton", "Reread")


def main():
    if len(sys.argv) not in (3, 4) or len(sys.argv) == 4 and sys.argv[3] not in ("--narrow", "--narrow-ctrl", "--pointer-probe"):
        raise SystemExit(__doc__)
    try:
        mode = sys.argv[3] if len(sys.argv) == 4 else ""
        result = run(Path(sys.argv[1]), mode in ("--narrow", "--narrow-ctrl", "--pointer-probe"),
                     mode == "--pointer-probe", mode == "--narrow-ctrl")
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
