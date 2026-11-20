#!/usr/bin/env python3
"""Produce bounded independent input fixtures using Python tarfile/gzip, with golden semantic metadata."""
import gzip
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import tarfile

root = Path(sys.argv[1]).resolve(strict=True)
assert root.is_dir()
for dialect, format_id in [('ustar', tarfile.USTAR_FORMAT), ('pax', tarfile.PAX_FORMAT)]:
    data = io.BytesIO()
    expected = []
    with tarfile.open(fileobj=data, mode='w', format=format_id) as writer:
        for name, payload in [('one', b'delete me'), ('duplicate', b'first external'), ('duplicate', b'second external'), ('folder/', None)]:
            info = tarfile.TarInfo(name)
            info.mode, info.uid, info.gid = (0o750 if payload is None else 0o640), 12, 34
            info.uname, info.gname, info.mtime = 'external-user', 'external-group', 1700000000
            info.type = tarfile.DIRTYPE if payload is None else tarfile.REGTYPE
            info.size = len(payload or b'')
            if dialect == 'pax':
                info.pax_headers = {'mtime': '1700000000.123456789', 'atime': '1700000001.111111111', 'ctime': '1700000002.222222222'}
            writer.addfile(info, None if payload is None else io.BytesIO(payload))
            metadata = bytearray(struct.pack('<Hiii', 2 if dialect == 'pax' else 1, info.mode, info.uid, info.gid))
            for value in [info.uname, info.gname]:
                encoded = value.encode('utf-8')
                metadata += struct.pack('<i', len(encoded)) + encoded
            if dialect == 'pax':
                metadata += struct.pack('<qi', 1700000000, 123456789)
                metadata += b'\x01' + struct.pack('<qi', 1700000001, 111111111)
                metadata += b'\x01' + struct.pack('<qi', 1700000002, 222222222)
            else:
                metadata += struct.pack('<q', 621355968000000000 + 1700000000 * 10000000) + b'\x00\x00'
            expected.append({'Name': name, 'Sha256': hashlib.sha256(payload or b'').hexdigest().upper(), 'Metadata': metadata.hex().upper()})
    output = io.BytesIO()
    with gzip.GzipFile(fileobj=output, mode='wb', filename='', mtime=0, compresslevel=6) as encoder:
        encoder.write(data.getvalue())
    encoded = output.getvalue()
    (root / (dialect + '.tar.gz')).write_bytes(encoded)
    (root / (dialect + '.json')).write_text(json.dumps(expected), encoding='utf-8')
    print(dialect, len(encoded), hashlib.sha256(encoded).hexdigest().upper())
