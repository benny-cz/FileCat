import ctypes
import errno
import json
import os
import platform
import tempfile
from pathlib import Path


task = Path.home() / "FileCatValidation" / "archive-writing-42829297"
if not task.is_dir():
    raise RuntimeError("Task-owned test root is absent")
directory = Path(tempfile.mkdtemp(prefix="unix-exchange-v1-", dir=task))
libc = ctypes.CDLL(None, use_errno=True)
system = platform.system()
if system == "Darwin":
    exchange = libc.renameatx_np
elif system == "Linux":
    exchange = libc.renameat2
else:
    raise RuntimeError("Unix exchange probe requires Linux or macOS")
exchange.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
exchange.restype = ctypes.c_int
directory_fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)


def call(source, destination):
    result = exchange(directory_fd, source.encode(), directory_fd, destination.encode(), 2)
    return 0 if result == 0 else ctypes.get_errno()


def identity(name):
    stat = os.stat(name, dir_fd=directory_fd, follow_symlinks=False)
    return stat.st_dev, stat.st_ino, stat.st_mode


def write(name, payload):
    fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600, dir_fd=directory_fd)
    try:
        os.write(fd, payload)
        os.fsync(fd)
    finally:
        os.close(fd)


try:
    write("original", b"original bytes")
    write("stage", b"verified stage bytes")
    before_original, before_stage = identity("original"), identity("stage")
    assert call("stage", "original") == 0
    assert identity("original") == before_stage and identity("stage") == before_original
    assert (directory / "original").read_bytes() == b"verified stage bytes"
    assert (directory / "stage").read_bytes() == b"original bytes"
    assert call("stage", "original") == 0
    assert identity("original") == before_original and identity("stage") == before_stage

    missing_errno = call("stage", "missing")
    assert missing_errno == errno.ENOENT
    assert identity("stage") == before_stage and identity("original") == before_original

    write("poison", b"poison bytes")
    os.symlink("poison", "alias", dir_fd=directory_fd)
    before_alias = identity("alias")
    assert call("stage", "alias") == 0
    assert identity("alias") == before_stage and identity("stage") == before_alias
    assert os.readlink("stage", dir_fd=directory_fd) == "poison"
    assert (directory / "poison").read_bytes() == b"poison bytes"
    assert call("stage", "alias") == 0
    assert identity("stage") == before_stage and identity("alias") == before_alias

    print(json.dumps({"system": system, "kernel": platform.release(), "directory": str(directory),
                      "exchange": "renameatx_np" if system == "Darwin" else "renameat2",
                      "flag": 2, "same_directory_exchange": "PASS", "missing_name_errno": missing_errno,
                      "symlink_component_swap": "PASS", "original_identity": before_original[:2],
                      "stage_identity": before_stage[:2]}))
finally:
    os.close(directory_fd)
    for name in ("original", "stage", "poison", "alias"):
        try:
            (directory / name).unlink()
        except FileNotFoundError:
            pass
    directory.rmdir()
