"""Probe an atomic exchange between a private stage directory and a public archive directory."""

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
root = Path(tempfile.mkdtemp(prefix="unix-crossdir-exchange-v1-", dir=task))
private = root / "private"
public = root / "public"
private.mkdir(mode=0o700)
public.mkdir(mode=0o755)
libc = ctypes.CDLL(None, use_errno=True)
system = platform.system()
if system == "Darwin":
    exchange = libc.renameatx_np
elif system == "Linux":
    exchange = libc.renameat2
else:
    raise RuntimeError("Cross-directory exchange probe requires Linux or macOS")
exchange.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
exchange.restype = ctypes.c_int
private_fd = os.open(private, os.O_RDONLY | os.O_DIRECTORY)
public_fd = os.open(public, os.O_RDONLY | os.O_DIRECTORY)


def write(directory_fd, name, payload):
    fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644, dir_fd=directory_fd)
    try:
        os.write(fd, payload)
        os.fsync(fd)
    finally:
        os.close(fd)


def identity(directory_fd, name):
    stat = os.stat(name, dir_fd=directory_fd, follow_symlinks=False)
    return stat.st_dev, stat.st_ino, stat.st_mode


def call(source, target):
    result = exchange(private_fd, source.encode(), public_fd, target.encode(), 2)
    return 0 if result == 0 else ctypes.get_errno()


try:
    assert os.fstat(private_fd).st_dev == os.fstat(public_fd).st_dev
    assert os.stat(private).st_mode & 0o777 == 0o700
    write(private_fd, "stage", b"verified stage bytes")
    write(public_fd, "archive", b"original archive bytes")
    stage = identity(private_fd, "stage")
    original = identity(public_fd, "archive")
    assert stage != original
    assert call("stage", "archive") == 0
    assert identity(public_fd, "archive") == stage
    assert identity(private_fd, "stage") == original
    assert (public / "archive").read_bytes() == b"verified stage bytes"
    assert (private / "stage").read_bytes() == b"original archive bytes"
    os.fsync(private_fd)
    os.fsync(public_fd)
    assert call("stage", "archive") == 0
    assert identity(private_fd, "stage") == stage
    assert identity(public_fd, "archive") == original
    os.fsync(private_fd)
    os.fsync(public_fd)
    missing = call("stage", "missing")
    assert missing == errno.ENOENT
    assert identity(private_fd, "stage") == stage
    assert identity(public_fd, "archive") == original
    print(json.dumps({
        "system": system,
        "kernel": platform.release(),
        "exchange": "renameatx_np" if system == "Darwin" else "renameat2",
        "flag": 2,
        "different_pinned_directories": "PASS",
        "private_mode": "0700",
        "same_device": True,
        "stage_identity": stage[:2],
        "original_identity": original[:2],
        "bytes_and_identity_after_both_directions": "PASS",
        "both_directory_syncs": "PASS",
        "missing_name_errno": missing,
    }))
finally:
    os.close(private_fd)
    os.close(public_fd)
    for path in (private / "stage", public / "archive"):
        try:
            path.unlink()
        except FileNotFoundError:
            pass
    private.rmdir()
    public.rmdir()
    root.rmdir()
