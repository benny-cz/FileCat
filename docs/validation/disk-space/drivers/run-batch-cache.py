"""Copy a verified immutable baseline; runner-only reruns refuse every binary patch."""
import hashlib
import json
import pathlib
import shutil
import sys


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


base = pathlib.Path.cwd().resolve()
driver = base / 'patch-input'
plan = json.loads((driver / 'plan.json').read_text(encoding='utf-8-sig'))
baseline = pathlib.Path(sys.argv[1]).resolve(strict=True)
if str(baseline) != plan['baseline']:
    raise RuntimeError('Unexpected cached baseline')
allowed = set(plan['allowed'])
scope = plan.get('scope', 'TestOnly')
if scope == 'RunnerOnly':
    if allowed:
        raise RuntimeError('Runner-only reuse cannot patch any input')
elif scope == 'AppUi':
    if allowed != {'render/FileCat.dll', 'render/FileCat.pdb', 'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}:
        raise RuntimeError('Only the declared App and rendering-test DLL/PDB pairs may be patched')
elif scope in ('TestOnly', 'OldProductControl'):
    core = {'core/FileCat.Core.Tests.dll', 'core/FileCat.Core.Tests.pdb'}
    checkout_core = {'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.dll', 'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.pdb'}
    render = {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
    if allowed not in (core, checkout_core, render) and not (scope == 'TestOnly' and allowed in (core | render, checkout_core | render)):
        raise RuntimeError('Only complete declared Core/render test DLL/PDB pairs may be patched')
else:
    raise RuntimeError('Unknown cached input scope')
rows = []
seen = set()
for item in plan['files']:
    path = pathlib.PurePosixPath(item['path'])
    if path.is_absolute() or '..' in path.parts or ':' in str(path) or str(path) in seen:
        raise RuntimeError('Invalid cached path')
    seen.add(str(path))
    source = (baseline / path).resolve(strict=True)
    if not source.is_relative_to(baseline) or digest(source) != item['baselineSha256']:
        raise RuntimeError('Cached input changed: ' + str(path))
    if item['patch'] and str(path) not in allowed:
        raise RuntimeError('Production patch refused')
    source = (driver / 'patch' / path).resolve(strict=True) if item['patch'] else source
    if item['patch'] and not source.is_relative_to(driver):
        raise RuntimeError('Patch escaped its archive')
    if digest(source) != item['sha256']:
        raise RuntimeError('Candidate input changed')
    rows.append((item, path, source))
payload = base / 'payload'
payload.mkdir()  # Refuse an existing destination; never overwrite an earlier run.
for item, path, source in rows:
    target = payload / path
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    if digest(target) != item['sha256']:
        raise RuntimeError('Copied input mismatch')
(base / 'cache-proof.txt').write_text('VERIFIED cachedFiles=' + str(len(rows)) + ' changedFiles=' + str(sum(bool(row[0]['patch']) for row in rows)) + '\n')
