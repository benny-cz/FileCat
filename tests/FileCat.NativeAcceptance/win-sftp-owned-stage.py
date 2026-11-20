"""Serve an owned, empty directory over SFTP for the EFS live-upload tests."""

import argparse
import base64
import errno
from functools import wraps
import hashlib
import os
from pathlib import Path, PureWindowsPath
import re
from socketserver import BaseRequestHandler, ThreadingTCPServer
from threading import Lock

import paramiko


def as_sftp_status(operation):
    @wraps(operation)
    def invoke(*args):
        try:
            result = operation(*args)
            return paramiko.SFTP_OK if result is None else result
        except OSError as error:
            return paramiko.SFTPServer.convert_errno(error.errno)
    return invoke


class OwnedHandle(paramiko.SFTPHandle):
    @as_sftp_status
    def stat(self):
        return paramiko.SFTPAttributes.from_stat(os.fstat(self.stream.fileno()))


class OwnedDirectory(paramiko.SFTPServerInterface):
    def __init__(self, server, root, rename_lock):
        super().__init__(server)
        self.root = root
        self.rename_lock = rename_lock

    def local(self, path, *, allow_root=False):
        parts = [part for part in path.split("/") if part not in ("", ".")]
        if (not allow_root and not parts) or any(
            not re.fullmatch(r"[a-zA-Z0-9._-]+", part)
            or part.endswith(".")
            or PureWindowsPath(part).is_reserved()
            for part in parts
        ):
            raise PermissionError(errno.EACCES, "Unsupported owned fixture path")
        candidate = self.root.joinpath(*parts)
        if candidate.resolve() != candidate:
            raise PermissionError(errno.EACCES, "Links are unavailable in the owned fixture")
        return candidate

    def canonicalize(self, path):
        relative = self.local(path, allow_root=True).relative_to(self.root)
        return "/" + "/".join(relative.parts)

    @as_sftp_status
    def list_folder(self, path):
        directory = self.local(path, allow_root=True)
        return [
            paramiko.SFTPAttributes.from_stat(self.local(path.rstrip("/") + "/" + item.name).stat(), item.name)
            for item in directory.iterdir()
        ]

    @as_sftp_status
    def stat(self, path):
        return paramiko.SFTPAttributes.from_stat(self.local(path, allow_root=True).stat())

    lstat = stat

    @as_sftp_status
    def open(self, path, flags, attr):
        handle = OwnedHandle(flags)
        access = flags & (os.O_WRONLY | os.O_RDWR)
        mode = {os.O_RDONLY: "rb", os.O_WRONLY: "wb", os.O_RDWR: "r+b"}[access]
        descriptor = os.open(self.local(path), flags | getattr(os, "O_BINARY", 0), 0o600)
        try:
            handle.stream = os.fdopen(descriptor, mode)
        except BaseException:
            os.close(descriptor)
            raise
        handle.readfile = handle.stream if access != os.O_WRONLY else None
        handle.writefile = handle.stream if access != os.O_RDONLY else None
        return handle

    @as_sftp_status
    def mkdir(self, path, attr):
        self.local(path).mkdir(mode=0o700)

    @as_sftp_status
    def rmdir(self, path):
        self.local(path).rmdir()

    @as_sftp_status
    def remove(self, path):
        self.local(path).unlink()

    @as_sftp_status
    def rename(self, oldpath, newpath):
        old, new = self.local(oldpath), self.local(newpath)
        with self.rename_lock:
            if new.exists():
                raise FileExistsError(errno.EEXIST, "The owned fixture target exists")
            old.rename(new)

    @as_sftp_status
    def chattr(self, path, attr):
        if attr.st_atime is None or attr.st_mtime is None:
            return paramiko.SFTP_OP_UNSUPPORTED
        os.utime(self.local(path), (attr.st_atime, attr.st_mtime))


class PasswordSession(paramiko.ServerInterface):
    def get_allowed_auths(self, username):
        return "password"

    def check_auth_password(self, username, password):
        return (paramiko.AUTH_SUCCESSFUL if (username, password) == ("tester", "secret-pass")
                else paramiko.AUTH_FAILED)

    def check_channel_request(self, kind, chanid):
        return (paramiko.OPEN_SUCCEEDED if kind == "session"
                else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED)


class SftpConnection(BaseRequestHandler):
    def handle(self):
        with paramiko.Transport(self.request) as transport:
            transport.add_server_key(self.server.key)
            transport.set_subsystem_handler("sftp", paramiko.SFTPServer, OwnedDirectory,
                                            self.server.root, self.server.rename_lock)
            transport.start_server(server=PasswordSession())
            channel = transport.accept(timeout=10)
            if channel is not None:
                transport.join()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("ready", type=Path)
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    ready = args.ready.resolve()
    if root != ready.parent / "server" or any(root.iterdir()) or ready.exists():
        raise ValueError("Use a fresh ready file and its empty sibling server directory")
    with ThreadingTCPServer(("127.0.0.1", 0), SftpConnection) as server:
        server.daemon_threads = True
        server.root = root
        server.rename_lock = Lock()
        server.key = paramiko.RSAKey.generate(2048)
        digest = hashlib.sha256(server.key.asbytes()).digest()
        fingerprint = "SHA256:" + base64.b64encode(digest).decode("ascii").rstrip("=")
        print("SFTP server listening on loopback", flush=True)
        staged = ready.with_suffix(".tmp")
        with staged.open("x", encoding="ascii") as receipt:
            receipt.write(f"{server.server_address[1]}\n{fingerprint}\n")
        staged.rename(ready)
        server.serve_forever()


if __name__ == "__main__":
    main()
