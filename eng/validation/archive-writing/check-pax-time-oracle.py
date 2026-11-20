#!/usr/bin/env python3
"""Independent stdlib reader of owned test outputs; never extracts archive members."""
import decimal
import hashlib
import json
from pathlib import Path
import platform
import sys
import tarfile

root = Path(sys.argv[1]).resolve(strict=True)
assert root.is_dir()
checked = []
for expected_path in sorted(root.glob('*.json')):
    expected = json.loads(expected_path.read_text(encoding='utf-8'))
    archive = expected_path.with_suffix('.tar')
    assert not archive.is_symlink()
    with tarfile.open(archive, mode='r:') as source:
        members = source.getmembers()
        assert len(members) == len(expected)
        for member, wanted in zip(members, expected):
            assert member.isfile() and member.name == wanted['Name']
            for key, field in [('mtime', 'Modified'), ('atime', 'Accessed'), ('ctime', 'Changed')]:
                value = wanted[field]
                if value is None:
                    assert key not in member.pax_headers
                else:
                    # tarfile.mtime projects to binary float; inspect its independently parsed PAX dictionary.
                    assert decimal.Decimal(member.pax_headers[key]) == decimal.Decimal(value)
            digest = hashlib.sha256()
            with source.extractfile(member) as payload:
                while chunk := payload.read(65536):
                    digest.update(chunk)
            assert digest.hexdigest().upper() == wanted['Sha256']
    checked.append({'archive': archive.name, 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest().upper(), 'members': len(expected)})
assert len(checked) == 8, f'Expected eight create/replace/rename fixtures, got {len(checked)}'
print(json.dumps({'python': platform.python_version(), 'platform': platform.platform(), 'checked': checked}, sort_keys=True))
