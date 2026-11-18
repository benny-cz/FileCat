"""Inspect the owned FileCat resource dialog through the Ubuntu accessibility bus."""

from collections import deque
import json
import sys
import time

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def read_dialog(process_id):
    desktop = Atspi.get_desktop(0)
    applications = (desktop.get_child_at_index(index) for index in range(min(desktop.get_child_count(), 128)))
    application = next((item for item in applications if item and item.get_process_id() == process_id), None)
    result = {"applicationExposed": application is not None, "dialogTitleExposed": False,
              "closeButtonFocused": False, "scratchAmountExposed": False}
    if application is None:
        return result

    pending = deque([application])
    for _ in range(4096):
        if not pending:
            break
        item = pending.popleft()
        name = item.get_name() or ""
        if name == "Disk space resource limits":
            result["dialogTitleExposed"] = True
        if name == "Close limits" and item.get_role() == Atspi.Role.PUSH_BUTTON:
            state = item.get_state_set()
            result["closeButtonFocused"] |= state.contains(Atspi.StateType.FOCUSED) and state.contains(Atspi.StateType.ENABLED)
        if " for this index and its derived data." in name:
            result["scratchAmountExposed"] = True
        for index in range(min(item.get_child_count(), 128)):
            child = item.get_child_at_index(index)
            if child is not None:
                pending.append(child)
    return result


if __name__ == "__main__":
    Atspi.init()
    end = time.monotonic() + 4
    while True:
        receipt = read_dialog(int(sys.argv[1]))
        if all(receipt.values()) or time.monotonic() >= end:
            print(json.dumps(receipt))
            sys.exit(0)
        time.sleep(0.1)

