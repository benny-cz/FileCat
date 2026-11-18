"""Deliver the two requested test keys to the owned FileCat window in the Ubuntu VM."""

import subprocess


def send_requested_key(run_directory, process_id, environment, active_test_processes, sent):
    request = run_directory / "resource-key.request"
    response = run_directory / "resource-key.response"
    if not request.is_file() or response.exists():
        return

    key = request.read_text()
    if tuple(sent + [key]) not in (("Return",), ("Return", "Escape")):
        raise ValueError("The resource keyboard probe requested an unexpected key.")

    def x11(*arguments):
        if active_test_processes() != {process_id}:
            raise RuntimeError("Another tester appeared before native keyboard input.")
        return subprocess.check_output(["xdotool", *arguments], env=environment, text=True, timeout=8).strip()

    windows = x11("search", "--onlyvisible", "--all", "--pid", str(process_id), "--name", "^Disk space .*FileCat$").splitlines()
    if len(windows) != 1 or x11("getwindowpid", windows[0]) != str(process_id):
        raise RuntimeError("The FileCat test must own exactly one visible disk space window.")
    x11("key", "--window", windows[0], key)

    pending = run_directory / "resource-key.response.pending"
    pending.write_text(key)
    pending.replace(response)
    sent.append(key)
