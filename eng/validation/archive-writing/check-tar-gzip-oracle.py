#!/usr/bin/env python3
"""Independent stdlib/zlib reader of bounded owned test fixtures; never extracts members to disk."""
import hashlib
import io
import json
from pathlib import Path
import platform
import struct
import sys
import tarfile
import decimal
import zlib

root = Path(sys.argv[1]).resolve(strict=True)
assert root.is_dir()
checked = []
for expected_path in sorted(root.glob('*.json')):
    expected = json.loads(expected_path.read_text(encoding='utf-8'))
    archive = expected_path.with_suffix('.tar.gz')
    assert not archive.is_symlink() and archive.stat().st_size <= 16 << 20
    encoded = archive.read_bytes()
    assert encoded[:10].hex().upper() == expected['Header']
    assert encoded[:8] == bytes([31, 139, 8, 0, 0, 0, 0, 0])
    decoder = zlib.decompressobj(31)
    decoded = decoder.decompress(encoded, (16 << 20) + 1)
    assert len(decoded) <= 16 << 20 and decoder.eof and not decoder.unused_data and not decoder.unconsumed_tail
    assert struct.unpack('<II', encoded[-8:]) == (zlib.crc32(decoded), len(decoded) & 0xffffffff)
    with tarfile.open(fileobj=io.BytesIO(decoded), mode='r:') as source:
        members = source.getmembers()
        assert len(members) == len(expected['Members'])
        for member, wanted in zip(members, expected['Members']):
            assert member.name == wanted['Name'].rstrip('/') and member.size == wanted['Length']
            assert member.isfile() if wanted['Type'] == 0 else member.isdir()
            metadata = bytes.fromhex(wanted['Metadata'])
            schema, mode, uid, gid = struct.unpack_from('<Hiii', metadata)
            assert schema in (1, 2) and (member.mode, member.uid, member.gid) == (mode, uid, gid)
            offset = 14
            for actual in [member.uname, member.gname]:
                length = struct.unpack_from('<i', metadata, offset)[0]
                offset += 4
                assert metadata[offset:offset + length].decode('utf-8') == actual
                offset += length
            def timestamp():
                global offset
                seconds = struct.unpack_from('<q', metadata, offset)[0]
                offset += 8
                if schema == 1:
                    return decimal.Decimal(seconds - 621355968000000000) / 10000000
                nanos = struct.unpack_from('<i', metadata, offset)[0]
                offset += 4
                return decimal.Decimal(seconds) + decimal.Decimal(nanos) / 1000000000
            modified = timestamp()
            assert decimal.Decimal(member.pax_headers.get('mtime', str(member.mtime))) == modified
            for key in ['atime', 'ctime']:
                present = metadata[offset] != 0
                offset += 1
                if present:
                    assert decimal.Decimal(member.pax_headers[key]) == timestamp()
                else:
                    assert key not in member.pax_headers
            assert offset == len(metadata)
            digest = hashlib.sha256()
            if member.isfile():
                with source.extractfile(member) as payload:
                    while chunk := payload.read(65536):
                        digest.update(chunk)
            assert digest.hexdigest().upper() == wanted['Sha256']
    checked.append({'archive': archive.name, 'sha256': hashlib.sha256(encoded).hexdigest().upper(), 'members': len(members)})
expected_count = int(sys.argv[2]) if len(sys.argv) > 2 else 11
assert len(checked) == expected_count, f'Expected {expected_count} fixtures, got {len(checked)}'
print(json.dumps({'python': platform.python_version(), 'zlib': zlib.ZLIB_RUNTIME_VERSION, 'platform': platform.platform(), 'checked': checked}, sort_keys=True))
