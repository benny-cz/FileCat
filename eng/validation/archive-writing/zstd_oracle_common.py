"""Test-only native libzstd oracle with fixed input/output limits; never a shipped dependency or codec fallback."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform

MAX_BYTES = 16 << 20


def frame_header(data):
    assert 9 <= len(data) <= MAX_BYTES and data[:4] == bytes.fromhex('28b52ffd')
    flags = data[4]
    assert flags & 0x1b == 0  # no dictionary field, unused or reserved bits
    single = bool(flags & 32)
    size_flag = flags >> 6
    width = (1 if single else 0) if size_flag == 0 else 1 << size_flag
    offset = 5 if single else 6
    assert offset + width <= len(data)
    size = int.from_bytes(data[offset:offset + width], 'little') if width else None
    if width == 2:
        size += 256
    if size is not None:
        assert size <= MAX_BYTES
    if single:
        window = size
    else:
        basis = 1 << (10 + (data[5] >> 3))
        window = basis + basis // 8 * (data[5] & 7)
    assert window <= 64 << 20
    return {'checksum': bool(flags & 4), 'contentSize': size, 'window': window}


class NativeZstd:
    def __init__(self):
        name = os.environ.get('FILECAT_ARCHIVE_ZSTD_ORACLE_LIBRARY')
        assert name, 'Supply an explicit pinned native libzstd path.'
        path = Path(name).resolve(strict=True)
        assert Path(name).is_absolute() and path.is_file() and path.stat().st_size <= 32 << 20
        digest = hashlib.sha256(path.read_bytes()).hexdigest().upper()
        inventory = json.loads(Path(__file__).with_name('zstd-oracle-inventory.json').read_text(encoding='utf-8'))
        expected = [item for item in inventory['libraries'] if item['os'] == platform.system() and item['sha256'] == digest]
        assert inventory['schema'] == 1 and len(expected) == 1, 'The native oracle is not in the reviewed test inventory.'
        self.library = str(path)
        self.lib = ctypes.CDLL(self.library)
        definitions = {
            'ZSTD_isError': ([ctypes.c_size_t], ctypes.c_uint),
            'ZSTD_getErrorName': ([ctypes.c_size_t], ctypes.c_char_p),
            'ZSTD_versionString': ([], ctypes.c_char_p),
            'ZSTD_compressBound': ([ctypes.c_size_t], ctypes.c_size_t),
            'ZSTD_createCCtx': ([], ctypes.c_void_p),
            'ZSTD_freeCCtx': ([ctypes.c_void_p], ctypes.c_size_t),
            'ZSTD_CCtx_setParameter': ([ctypes.c_void_p, ctypes.c_int, ctypes.c_int], ctypes.c_size_t),
            'ZSTD_compress2': ([ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t], ctypes.c_size_t),
            'ZSTD_findFrameCompressedSize': ([ctypes.c_void_p, ctypes.c_size_t], ctypes.c_size_t),
            'ZSTD_decompress': ([ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t], ctypes.c_size_t),
        }
        for function, (arguments, result) in definitions.items():
            method = getattr(self.lib, function)
            method.argtypes = arguments
            method.restype = result
        self.version = self.lib.ZSTD_versionString().decode('ascii')
        assert self.version == expected[0]['version']

    def check(self, result):
        if self.lib.ZSTD_isError(result):
            raise AssertionError(self.lib.ZSTD_getErrorName(result).decode('ascii'))
        return result

    def compress(self, data, checksum):
        assert len(data) <= MAX_BYTES
        source = ctypes.create_string_buffer(data)
        bound = self.check(self.lib.ZSTD_compressBound(len(data)))
        assert bound <= MAX_BYTES + (MAX_BYTES >> 7) + 65536
        output = ctypes.create_string_buffer(bound)
        context = self.lib.ZSTD_createCCtx()
        assert context
        try:
            self.check(self.lib.ZSTD_CCtx_setParameter(context, 100, 3))  # compressionLevel
            self.check(self.lib.ZSTD_CCtx_setParameter(context, 201, int(checksum)))
            length = self.check(self.lib.ZSTD_compress2(context, output, bound, source, len(data)))
            return output.raw[:length]
        finally:
            self.check(self.lib.ZSTD_freeCCtx(context))

    def decompress(self, data):
        info = frame_header(data)
        source = ctypes.create_string_buffer(data)
        assert self.check(self.lib.ZSTD_findFrameCompressedSize(source, len(data))) == len(data)
        # A single-pass decode has fixed caller-owned output, not a header-sized window allocation.
        output = ctypes.create_string_buffer(MAX_BYTES + 1)
        length = self.check(self.lib.ZSTD_decompress(output, MAX_BYTES + 1, source, len(data)))
        assert length <= MAX_BYTES and (info['contentSize'] is None or info['contentSize'] == length)
        return output.raw[:length]

    def facts(self):
        result = {'version': self.version, 'library': self.library}
        path = Path(self.library)
        if path.is_absolute() and path.is_file():
            result['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest().upper()
        return result
