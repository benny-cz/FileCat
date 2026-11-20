#!/usr/bin/env python3
"""Verify that local-only details commands refuse an actual FTP result without provider traffic.

FILECAT_ATSPI_ISOLATED_DISPLAY=1 dbus-run-session -- xvfb-run -a /usr/bin/python3 \
    linux-atspi-remote-file-details.py APP REPORT.json
Use an owned build with AT-SPI and pyftpdlib installed. Both paths must be below ~/FileCatValidation.
"""

import ftplib
import hashlib
import io
from itertools import islice
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile


NAME = "same-name.txt"
REASON = "archive, remote and virtual display paths are not local objects"
LOCAL = b"local file with the same presentation name"
REMOTE = b"owned remote file content"
SERVER = """
from pathlib import Path
import sys
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler
from pyftpdlib.servers import FTPServer

root, ready, trace = map(Path, sys.argv[1:])
authorizer = DummyAuthorizer()
authorizer.add_user("tester", "secret-pass", str(root), perm="elr")

class Handler(FTPHandler):
    def pre_process_command(self, line, cmd, arg):
        if cmd.isascii() and cmd.isalpha() and len(cmd) <= 8:
            with trace.open("a", encoding="ascii") as output:
                output.write(cmd + "\\n")
        return super().pre_process_command(line, cmd, arg)

Handler.authorizer = authorizer
server = FTPServer(("127.0.0.1", 0), Handler)
ready.write_text(str(server.socket.getsockname()[1]), encoding="ascii")
server.serve_forever()
"""


def run(app_path, report, result):
    helpers = runpy.run_path(str(Path(__file__).with_name("linux-atspi-ftp-large-listing.py")))
    Atspi, tree, wait_for, matching, action, stop = (
        helpers[name] for name in ("Atspi", "tree", "wait_for", "matching", "action", "stop")
    )
    fixture = Path(tempfile.mkdtemp(prefix="remote-file-details-", dir=report.parent))
    source, remote, state = (fixture / name for name in ("source", "remote", "state"))
    for folder in source, remote, state:
        folder.mkdir()
    (source / NAME).write_bytes(LOCAL)
    (remote / NAME).write_bytes(REMOTE)
    ready, trace = fixture / "ready", fixture / "ftp-commands.txt"
    result["fixture"] = str(fixture)
    result["appDllSha256"] = hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest()
    server = app = None

    def xdotool(*args):
        return subprocess.run(["/usr/bin/xdotool", *args], text=True, capture_output=True, timeout=5)

    def windows():
        return xdotool("search", "--onlyvisible", "--pid", str(app.pid)).stdout.splitlines()

    def main_window():
        return next((window for window in windows()
                     if " — FileCat — " in xdotool("getwindowname", window).stdout), None)

    def key(chord):
        if xdotool("getwindowfocus").stdout.strip() not in windows():
            window = wait_for(main_window, f"the owned FileCat window before {chord}", seconds=8)
            xdotool("windowfocus", "--sync", window).check_returncode()
        xdotool("key", "--clearmodifiers", chord).check_returncode()

    def find(role, name):
        return next((node for node in tree(application)
                     if node.get_role_name() == role and node.get_name() == name
                     and node.get_state_set().contains(Atspi.StateType.SHOWING)), None)

    def named(role, name):
        return wait_for(lambda: find(role, name) or (find("push button", name) if role == "button" else None),
                        f"the accessible {name}")

    def commands():
        return trace.read_text(encoding="ascii").splitlines()

    def exposure(node):
        return " ".join(dict.fromkeys(text for text in (node.get_name(), node.get_description(), node.get_help_text()) if text))

    def search_refused(title):
        key("ctrl+shift+p")
        if not named("entry", "Search commands").get_editable_text().set_text_contents(title):
            raise AssertionError(f"Could not search for {title}.")
        row = wait_for(lambda: next((node for node in tree(application)
                       if node.get_role_name() == "list item" and title.casefold() in exposure(node).casefold()
                       and REASON in exposure(node)), None), f"the disabled {title} result and reason")
        help_text = exposure(row)
        key("Escape")
        return help_text

    def menu_refusals(titles):
        key("alt+f")
        action(named("menu item", "Inspect"), "the Inspect submenu")
        states = {}
        for title in titles:
            item = wait_for(lambda: next((node for node in tree(application)
                            if node.get_role_name() == "menu item" and (node.get_name() or "").startswith(title)
                            and node.get_state_set().contains(Atspi.StateType.SHOWING)), None), f"the Inspect {title} item")
            help_text = exposure(item)
            if item.get_state_set().contains(Atspi.StateType.ENABLED) or REASON not in help_text:
                raise AssertionError(f"Inspect did not disable {title} with the remote-provider reason.")
            states[title] = help_text
        return states

    application = None
    try:
        with (fixture / "ftp.log").open("wb") as log:
            server = subprocess.Popen([sys.executable, "-c", SERVER, str(remote), str(ready), str(trace)],
                                      stdout=log, stderr=subprocess.STDOUT)
        port = wait_for(lambda: ready.read_text(encoding="ascii") if ready.is_file() and server.poll() is None else None,
                        "the owned loopback FTP port")
        if not port.isdecimal() or not 1 <= int(port) <= 65535:
            raise AssertionError("Invalid FTP fixture port.")
        with (fixture / "app.log").open("wb") as log:
            app = subprocess.Popen([str(app_path), "--new-instance", "--data", str(state),
                                    "--left", str(source), "--right", str(remote)],
                                   cwd=fixture, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        application = wait_for(lambda: next((node for node in
                              (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                              if node is not None and node.get_process_id() == app.pid), None), "the owned FileCat process")
        wait_for(main_window, "the owned desktop window")
        locations = wait_for(lambda: matching(application, "entry", "Location") or None, "the panel locations")
        address = f"ftp://tester@127.0.0.1:{port}/"
        if len(locations) != 2 or not locations[-1].grab_focus() or not locations[-1].get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned FTP location.")
        key("Return")
        action(named("button", "Connect unencrypted"), "the owned plaintext connection")
        if not named("entry", "Password").get_editable_text().set_text_contents("secret-pass"):
            raise AssertionError("Could not enter the loopback fixture's credential.")
        action(named("button", "Connect"), "the owned FTP connection")
        panel = wait_for(lambda: next((node for node in tree(application)
                         if node.get_role_name() == "list" and address.rstrip("/") in (node.get_name() or "")), None),
                         "the FTP panel")
        row = wait_for(lambda: next((node for node in tree(panel) if node.get_role_name() == "list item"
                       and (node.get_name() or "").split(",")[0] == NAME), None), "the same-name remote file")
        if not row.grab_focus():
            raise AssertionError("Could not focus the remote file.")
        wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED), "remote row focus")
        baseline = commands()
        if not {"USER", "PASS"} <= set(baseline) or not {"LIST", "MLSD", "NLST"} & set(baseline):
            raise AssertionError("The FTP trace did not see the real connection and listing.")

        titles = ("File-system details", "Extended attributes")
        result["commandSearch"] = {title: search_refused(title) for title in titles}
        key("ctrl+alt+Return")
        notice = wait_for(lambda: next((node.get_name() for node in tree(application)
                          if node.get_role_name() == "label" and REASON in (node.get_name() or "")), None),
                          "the accessible shortcut refusal")
        result["shortcutNotice"] = notice
        key("alt+shift+Return")
        if any((node.get_name() or "").startswith("File-system details") for node in tree(application)
               if node.get_role_name() in ("frame", "window", "dialog")):
            raise AssertionError("A local details window opened on the remote row.")
        if not row.get_state_set().contains(Atspi.StateType.FOCUSED):
            raise AssertionError("Inspection changed the focused remote row.")
        result["inspectMenu"] = menu_refusals(titles)
        if commands() != baseline:
            raise AssertionError(f"Inspection sent FTP commands: {commands()[len(baseline):]}.")
        result["inspectionCommands"] = []
        downloaded = io.BytesIO()
        with ftplib.FTP(timeout=5) as client:
            client.connect("127.0.0.1", int(port))
            client.login("tester", "secret-pass")
            client.retrbinary(f"RETR {NAME}", downloaded.write)
            client.quit()
        if downloaded.getvalue() != REMOTE or "RETR" not in commands()[len(baseline):]:
            raise AssertionError("The FTP trace did not observe the independent retrieval control.")
        result["positiveControlCommands"] = commands()[len(baseline):]
        if (source / NAME).read_bytes() != LOCAL or (remote / NAME).read_bytes() != REMOTE:
            raise AssertionError("The isolated original files changed.")
        result["passed"] = True
    except AssertionError:
        if application is not None:
            controls = ([node.get_role_name(), node.get_name()] for node in tree(application)
                        if node.get_state_set().contains(Atspi.StateType.SHOWING))
            result["visibleControls"] = list(islice(controls, 80))
        raise
    finally:
        stop(app)
        stop(server)


def main():
    if len(sys.argv) != 3 or os.environ.get("FILECAT_ATSPI_ISOLATED_DISPLAY") != "1":
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app = Path(sys.argv[1]).resolve(strict=True)
    report = Path(sys.argv[2]).resolve()
    if app.name != "FileCat" or validation not in app.parents or validation not in report.parents or report.exists():
        raise SystemExit("Use a dedicated validation build and fresh receipt.")
    result = {"passed": False}
    try:
        run(app, report, result)
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
