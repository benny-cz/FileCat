"""Continue only unfinished phases using frozen r1166 inputs, never current bin/."""
import datetime
import hashlib
import json
import pathlib
import shutil
import tarfile
import xml.etree.ElementTree as ET

ROOT = pathlib.Path.cwd()
TMP = ROOT / '.tmp'
DRIVERS = ROOT / 'docs/validation/disk-space/drivers'
BASELINE = 'r1166'
REVISION = 'r1166b'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(path):
    if path.exists():
        raise RuntimeError('Fresh destination required: ' + str(path))
    return path


proof = dict(revision=REVISION, baseline=BASELINE, scope='RunnerOnly',
             observedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
             producerSha256=digest(pathlib.Path(__file__)), platforms=[])
source = TMP / f'source-candidate-{BASELINE}.json'
shutil.copy2(source, fresh(TMP / f'source-candidate-{REVISION}.json'))
proof['sourceManifestSha256'] = digest(source)
proof['sourceSnapshot'] = '.tmp/source-r1166-main-sync.tgz'
proof['sourceSnapshotSha256'] = digest(TMP / 'source-r1166-main-sync.tgz')
original_pack = json.loads((TMP / f'pack-timing-{BASELINE}.json').read_text(encoding='utf-8-sig'))
for platform, tag, completed, pending in [
    ('windows', 'windows', {'smoke': 17, 'preview': 5, 'core': 1980},
     ['metacore', 'native', 'ownernative', 'render', 'ownerrender', 'appsettings', 'ownerapp']),
    ('mac', 'mac', {'smoke': 17},
     ['preview', 'core', 'metacore', 'render', 'ownerrender', 'appsettings', 'ownerapp']),
    ('linux', 'ubuntu', {'smoke': 17},
     ['preview', 'core', 'metacore', 'render', 'ownerrender', 'appsettings', 'ownerapp']),
]:
    results = TMP / f'{tag}-batch-{BASELINE}-results'
    expected_state = 'BUSY phase=' + pending[0]
    if (results / 'state.txt').read_text(encoding='utf-8-sig').strip() != expected_state:
        raise RuntimeError('Original terminal state changed')
    if {p.stem.removesuffix('-' + BASELINE) for p in results.glob('*.xml')} != set(completed):
        raise RuntimeError('Unexpected prior phase set')
    passed = {}
    for phase, total in completed.items():
        path = results / f'{phase}-{BASELINE}.xml'
        a = ET.parse(path).getroot().find('assembly')
        if int(a.get('total')) != total or int(a.get('failed')) or int(a.get('errors')):
            raise RuntimeError('Cannot reuse an incomplete or failed phase')
        if int(a.get('passed')) + int(a.get('skipped')) != total:
            raise RuntimeError('Unexpected unexecuted cases')
        passed[phase] = dict(sha256=digest(path), total=total, passed=int(a.get('passed')), skipped=int(a.get('skipped')))
    original = TMP / f'dsa-batch-{platform}-{BASELINE}.tgz'
    expected = next(a for a in original_pack['archives'] if a['platform'] == platform)
    if digest(original) != expected['sha256']:
        raise RuntimeError('Original frozen archive changed')
    old = TMP / f'batch-{BASELINE}-{platform}'
    files = []
    # Verify the whole frozen tree against the archive that actually ran.
    with tarfile.open(original) as archive:
        members = [m for m in archive.getmembers() if m.isfile()]
        names = set()
        for member in members:
            name = pathlib.PurePosixPath(member.name)
            if name.is_absolute() or '..' in name.parts or ':' in str(name) or str(name) in names:
                raise RuntimeError('Invalid frozen archive path')
            names.add(str(name))
            sha = digest(old / name)
            if hashlib.file_digest(archive.extractfile(member), 'sha256').hexdigest() != sha:
                raise RuntimeError('Frozen tree differs: ' + str(name))
            files.append(dict(path=str(name), baselineSha256=sha, sha256=sha, patch=False))
    if {p.relative_to(old).as_posix() for p in old.rglob('*') if p.is_file()} != names:
        raise RuntimeError('Frozen tree has additional files')
    candidate = fresh(TMP / f'dsa-batch-{platform}-{REVISION}.tgz')
    core = 'tests/FileCat.Core.Tests/bin/Debug/net10.0'
    template = DRIVERS / ('main-sync-windows-template-r1166.ps1' if platform == 'windows' else 'main-sync-unix-template-r1166.sh')
    runner = template.read_text(encoding='utf-8-sig')
    if platform == 'windows':
        shutil.copytree(old, fresh(TMP / f'batch-{REVISION}-{platform}'))
        shutil.copy2(original, candidate)
        before = "foreach($phase in 'smoke','preview','core','metacore','native','ownernative','render','ownerrender','appsettings','ownerapp')"
        after = "foreach($phase in " + ','.join(repr(p) for p in pending) + ')'
        if runner.count(before) != 1:
            raise RuntimeError('Phase boundary changed')
        runner = runner.replace(before, after)
    else:
        delta = fresh(TMP / f'frozen-continuation-{platform}-{REVISION}')
        delta.mkdir()
        baseline = ('/Users/benny' if platform == 'mac' else '/home/benny') + f'/FileCatValidation/dsa-batch-20261107-{BASELINE}/payload'
        (delta / 'plan.json').write_text(json.dumps(dict(baseline=baseline, files=files, allowed=[], scope='RunnerOnly'), indent=2))
        shutil.copy2(DRIVERS / 'run-batch-cache.py', delta / 'reuse.py')
        with tarfile.open(candidate, 'w:gz') as archive:
            for path in sorted(delta.iterdir()):
                archive.add(path, arcname=path.name)
        before = 'mkdir payload\ntar -xzf bundle.tgz -C payload'
        if runner.count(before) != 1:
            raise RuntimeError('Extraction boundary changed')
        runner = runner.replace(before, f"mkdir patch-input\ntar -xzf bundle.tgz -C patch-input\npython3 patch-input/reuse.py '{baseline}'")
        before = 'for phase in smoke preview core metacore render ownerrender appsettings ownerapp; do'
        if runner.count(before) != 1:
            raise RuntimeError('Phase boundary changed')
        runner = runner.replace(before, 'for phase in ' + ' '.join(pending) + '; do')
    sha = digest(candidate)
    runner = runner.replace('@REV@', REVISION).replace('@HASH@', sha).replace('@CORE@', core)
    if '@REV@' in runner or '@HASH@' in runner or '@CORE@' in runner:
        raise RuntimeError('Unexpanded runner')
    extension = 'ps1' if platform == 'windows' else 'sh'
    output = fresh(TMP / f'batch-{platform}-{REVISION}.{extension}')
    output.write_text(runner, encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, priorState=expected_state, reusedPhases=passed,
                                   pendingPhases=pending, verifiedInputs=len(files), changed=[],
                                   archiveSha256=sha, archiveBytes=candidate.stat().st_size,
                                   runnerSha256=digest(output), templateSha256=digest(template)))
fresh(TMP / f'frozen-continuation-proof-{REVISION}.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: dict(bytes=p['archiveBytes'], pending=p['pendingPhases'], changed=p['changed']) for p in proof['platforms']}))
