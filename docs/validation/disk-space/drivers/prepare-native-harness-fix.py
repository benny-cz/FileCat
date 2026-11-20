"""Reuse verified native product inputs; replace only the standalone test harness."""
import hashlib
import json
import pathlib
import re
import shutil
import sys
import tarfile
import time

revision, prior = sys.argv[1:3]
driver_only = sys.argv[3:] == ['--driver-only']
assert len(sys.argv) == 3 or driver_only
assert all(re.fullmatch('r[0-9]+', s) for s in (revision, prior))
root = pathlib.Path.cwd()
temp = root / '.tmp'
managed = root / 'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
started = time.monotonic()
records = []

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

for platform in ('mac', 'linux', 'windows'):
    baseline = temp / f'desktop-{prior}-{platform}'
    stage = temp / f'desktop-{revision}-{platform}'
    assert not stage.exists()
    shutil.copytree(baseline, stage)
    changed = []
    for source in managed.iterdir():
        if source.is_file() and source.suffix in ('.dll', '.pdb', '.json'):
            target = stage / source.name
            assert target.is_file()
            if sha(source) != sha(target):
                assert source.name in ('DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb'), 'Product input changed: ' + source.name
                changed.append(source.name)
                shutil.copy2(source, target)
    assert set(changed) == (set() if driver_only else {'DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb'})
    archive = temp / f'desktop-{revision}-{platform}.tgz'
    assert not archive.exists()
    patch = temp / f'native-patch-{revision}-{platform}'
    patch.mkdir()
    home = '/Users/benny' if platform == 'mac' else '/home/benny'
    guest = f'C:\\FileCatDsa20261104R2\\native-{prior}\\app' if platform == 'windows' else home + f'/FileCatValidation/dsa-native-{prior}/app'
    plan = {'baseline': guest, 'files': []}
    for path in sorted(stage.rglob('*')):
        if not path.is_file():
            continue
        name = path.relative_to(stage).as_posix()
        old = baseline / name
        is_patch = name in changed
        plan['files'].append({'path': name, 'baselineSha256': sha(old), 'sha256': sha(path), 'patch': is_patch})
        if is_patch:
            target = patch / 'patch' / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
    (patch / 'plan.json').write_text(json.dumps(plan, indent=2) + '\n')
    with tarfile.open(archive, 'w:gz') as out:
        for path in sorted(patch.iterdir()):
            out.add(path, arcname=path.name)
    records.append({'platform': platform, 'archive': str(archive), 'sha256': sha(archive), 'bytes': archive.stat().st_size, 'guestReuse': True, 'changed': changed})
proof = {'revision': revision, 'baseline': prior, 'elapsedSeconds': time.monotonic() - started, 'platforms': records}
(temp / f'pack-native-{revision}.json').write_text(json.dumps(proof, indent=2) + '\n')
print(json.dumps(proof, indent=2))
