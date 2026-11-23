"""Windows metadata reads with no reparse traversal or persistent source writes.

Directory enumeration and explicit content reads suppress automatic access-time
updates on their own handles using SetFileTime(-1), as documented by Windows.
No stored timestamp is assigned. All handles share read/write/delete access so
inventory does not lock out active producers. A scan is a live observation.
"""
from contextlib import contextmanager
import ctypes as c
from ctypes import wintypes as w
import hashlib
import os
from pathlib import Path
import struct


class Basic(c.Structure):
    _fields_ = [("creation", c.c_longlong), ("access", c.c_longlong),
                ("write", c.c_longlong), ("change", c.c_longlong),
                ("attributes", w.DWORD)]


class Standard(c.Structure):
    _fields_ = [("allocation", c.c_longlong), ("size", c.c_longlong),
                ("links", w.DWORD), ("delete_pending", c.c_ubyte),
                ("directory", c.c_ubyte)]


class FileId(c.Structure):
    _fields_ = [("volume", c.c_ulonglong), ("identifier", c.c_ubyte * 16)]


class WindowsMetadataIO:
    def __init__(self):
        if os.name != "nt":
            raise RuntimeError("This inventory requires Windows; no unsafe fallback is used.")
        self.kernel = c.WinDLL("kernel32", use_last_error=True)
        self.create = self._api("CreateFileW", [w.LPCWSTR, w.DWORD, w.DWORD,
                                c.c_void_p, w.DWORD, w.DWORD, w.HANDLE], w.HANDLE)
        self.close = self._api("CloseHandle", [w.HANDLE])
        self.info = self._api("GetFileInformationByHandleEx",
                              [w.HANDLE, c.c_int, c.c_void_p, w.DWORD])
        self.set_time = self._api("SetFileTime", [w.HANDLE, c.c_void_p,
                                                c.c_void_p, c.c_void_p])
        self.read = self._api("ReadFile", [w.HANDLE, c.c_void_p, w.DWORD,
                                          c.POINTER(w.DWORD), c.c_void_p])
        self.free = self._api("GetDiskFreeSpaceExW", [w.LPCWSTR,
                              c.POINTER(c.c_ulonglong), c.POINTER(c.c_ulonglong),
                              c.POINTER(c.c_ulonglong)])
        self.directory_changes = []
        self.content_reads = 0
        self.content_bytes_read = 0
        self.enumeration_allocation_comparisons = 0

    def _api(self, name, arguments, result=w.BOOL):
        fn = getattr(self.kernel, name)
        fn.argtypes, fn.restype = arguments, result
        return fn

    @staticmethod
    def _check(ok):
        if not ok:
            raise c.WinError(c.get_last_error())

    @staticmethod
    def _long(path):
        value = str(path)
        if value.startswith("\\\\?\\"):
            return value
        if value.startswith("\\\\"):
            return "\\\\?\\UNC\\" + value[2:]
        return "\\\\?\\" + value

    def _basic(self, handle):
        value = Basic()
        self._check(self.info(handle, 0, c.byref(value), c.sizeof(value)))
        return {name: int(getattr(value, name)) for name, _ in value._fields_}

    def _stat(self, handle):
        basic, standard, identity = self._basic(handle), Standard(), FileId()
        self._check(self.info(handle, 1, c.byref(standard), c.sizeof(standard)))
        self._check(self.info(handle, 18, c.byref(identity), c.sizeof(identity)))
        return dict(basic=basic, size=standard.size, allocation=standard.allocation,
                    links=standard.links, directory=bool(standard.directory),
                    delete_pending=bool(standard.delete_pending),
                    volume_serial=str(identity.volume),
                    file_id=bytes(identity.identifier).hex())

    @contextmanager
    def _handle(self, path, content=False, enumeration=False):
        # Name-normalization APIs can open parent directories internally,
        # outside our atime guard. Inspect ancestors using metadata-only,
        # OPEN_REPARSE_POINT handles instead. The walk also rejects every
        # reparse entry; a live inventory is not an atomic namespace snapshot.
        for parent in Path(path).absolute().parents:
            ancestor = self.create(self._long(parent), 0x80, 7, None, 3, 0x02200000, None)
            if ancestor == c.c_void_p(-1).value:
                raise c.WinError(c.get_last_error())
            try:
                if self._basic(ancestor)["attributes"] & 0x400:
                    raise ValueError("Reparse ancestry is forbidden: " + str(parent))
            finally:
                self.close(ancestor)
        # Metadata-only opens need no WRITE_ATTRIBUTES. Only handles that read
        # data (including directory listings) need the documented atime guard.
        access = 0x80
        if content:
            access |= 0x80000000 | 0x100
        if enumeration:
            access |= 1 | 0x100
        handle = self.create(self._long(path), access, 7, None, 3, 0x02200000, None)
        if handle == c.c_void_p(-1).value:
            raise c.WinError(c.get_last_error())
        try:
            before = self._basic(handle)
            if before["attributes"] & 0x400:
                raise ValueError("Reparse traversal is forbidden: " + str(path))
            if content or enumeration:
                never = c.c_ulonglong(0xffffffffffffffff)
                self._check(self.set_time(handle, None, c.byref(never), None))
                if self._basic(handle) != before:
                    raise RuntimeError("Access-time guard altered stored metadata: " + str(path))
            yield handle
        finally:
            self.close(handle)

    def stat(self, path):
        with self._handle(path) as handle:
            return self._stat(handle)

    def entries(self, directory):
        """Return exact names, identifiers and allocation reported by NTFS."""
        with self._handle(directory, enumeration=True) as handle:
            before = self._basic(handle)
            identity = self._stat(handle)
            if not identity["directory"]:
                raise ValueError("Inventory root is not a directory")
            buffer = c.create_string_buffer(64 * 1024)
            first = True
            while True:
                ok = self.info(handle, 20 if first else 19, buffer, len(buffer))
                first = False
                if not ok:
                    error = c.get_last_error()
                    if error == 18:  # ERROR_NO_MORE_FILES
                        break
                    raise c.WinError(error)
                offset = 0
                while True:
                    nxt, _ = struct.unpack_from("<II", buffer, offset)
                    created, accessed, written, changed, size, allocation = \
                        struct.unpack_from("<qqqqqq", buffer, offset + 8)
                    attributes, name_length, _, reparse_tag = \
                        struct.unpack_from("<IIII", buffer, offset + 56)
                    file_id = buffer.raw[offset + 72:offset + 88].hex()
                    end = offset + 88 + name_length
                    if end > len(buffer) or name_length % 2:
                        raise RuntimeError("Invalid native directory record")
                    name = buffer.raw[offset + 88:end].decode("utf-16le", "surrogatepass")
                    if name not in (".", ".."):
                        yield dict(name=name, size=size, allocation=allocation,
                                   volume_serial=identity["volume_serial"], file_id=file_id,
                                   directory=bool(attributes & 0x10), reparse=bool(attributes & 0x400),
                                   reparse_tag=reparse_tag,
                                   basic=dict(creation=created, access=accessed, write=written,
                                              change=changed, attributes=attributes))
                    if not nxt:
                        break
                    if nxt < 88 or offset + nxt >= len(buffer):
                        raise RuntimeError("Invalid native directory offset")
                    offset += nxt
            after = self._basic(handle)
            if before != after:
                self.directory_changes.append(dict(path=str(directory), before=before, after=after))

    def read_control(self, path, maximum=64 * 1024 * 1024):
        with self._handle(path, content=True) as handle:
            before = self._stat(handle)
            if before["directory"] or before["size"] > maximum:
                raise ValueError("Control file exceeds its read budget: " + str(path))
            pieces, total = [], 0
            buffer, read_count = c.create_string_buffer(1024 * 1024), w.DWORD()
            while True:
                self._check(self.read(handle, buffer, len(buffer), c.byref(read_count), None))
                if not read_count.value:
                    break
                total += read_count.value
                if total > maximum:
                    raise ValueError("Control file grew beyond its read budget")
                pieces.append(buffer.raw[:read_count.value])
            after = self._stat(handle)
            if before != after or total != before["size"]:
                raise RuntimeError("Control file changed during read: " + str(path))
            self.content_reads += 1
            self.content_bytes_read += total
            return b"".join(pieces)

    def digest(self, path):
        with self._handle(path, content=True) as handle:
            before, digest, total = self._stat(handle), hashlib.sha256(), 0
            if before["directory"]:
                raise ValueError("Cannot hash a directory")
            buffer, read_count = c.create_string_buffer(4 * 1024 * 1024), w.DWORD()
            while True:
                self._check(self.read(handle, buffer, len(buffer), c.byref(read_count), None))
                if not read_count.value:
                    break
                digest.update(buffer.raw[:read_count.value])
                total += read_count.value
            after = self._stat(handle)
            if before != after or total != before["size"]:
                raise RuntimeError("File changed during hash: " + str(path))
            self.content_reads += 1
            self.content_bytes_read += total
            return digest.hexdigest()

    def disk_space(self, drive):
        available, total, free = c.c_ulonglong(), c.c_ulonglong(), c.c_ulonglong()
        self._check(self.free(str(drive), c.byref(available), c.byref(total), c.byref(free)))
        return dict(available_bytes=available.value, total_bytes=total.value,
                    total_free_bytes=free.value)
