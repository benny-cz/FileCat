import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(path):
    assert not path.exists(), str(path)
    return path


prior = json.loads((temp / 'native-optimization-proof-r1184.json').read_text())
build = (temp / 'native-locality-fixture-fix-build-r1185.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in build and build.count('Build succeeded.') == 1
assert '    0 Warning(s)' in build and '    0 Error(s)' in build
sources = [dict(path=row['path'], sha256=sha(root / row['path'])) for row in prior['sources']]
changes = [row['path'] for row, old in zip(sources, prior['sources']) if row != old]
assert changes == ['tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.Locality.cs'], changes
assert sha(root / 'src/FileCat.Core/bin/Debug/net10.0/FileCat.Core.dll') == prior['productCoreSha256']
proof = dict(revision='r1185', productCoreSha256=prior['productCoreSha256'], productRebuilt=False,
             sourceChanges=changes, sources=sources, platforms=[])
for platform in ('windows', 'mac', 'linux'):
    source = temp / f'batch-r1184-{platform}'
    stage = fresh(temp / f'batch-r1185-{platform}')
    shutil.copytree(source, stage)
    for name in ('FileCat.Core.Tests.dll', 'FileCat.Core.Tests.pdb'):
        shutil.copy2(root / 'tests/FileCat.Core.Tests/bin/Debug/net10.0' / name,
                     stage / 'tests/FileCat.Core.Tests/bin/Debug/net10.0' / name)
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p), bytes=p.stat().st_size)
            for p in sorted(stage.rglob('*')) if p.is_file()]
    old = {row['path']: row for row in next(p for p in prior['platforms'] if p['platform'] == platform)['inputs']}
    changed = [row['path'] for row in rows if row != old[row['path']]]
    assert changed == [f'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.{ext}' for ext in ('dll', 'pdb')], changed
    archive = fresh(temp / f'dsa-batch-{platform}-r1185.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    ext = 'ps1' if platform == 'windows' else 'sh'
    previous = next(p for p in prior['platforms'] if p['platform'] == platform)
    runner = fresh(temp / f'batch-{platform}-r1185.{ext}')
    runner.write_text((temp / f'batch-{platform}-r1184.{ext}').read_text().replace(previous['archiveSha256'], sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, changedInputs=changed,
                                  archiveSha256=sha(archive), archiveBytes=archive.stat().st_size,
                                  runnerSha256=sha(runner)))
snapshot = fresh(temp / 'source-r1185-locality-fixture-fix.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources:
        archive.add(root / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(temp / 'native-locality-fixture-fix-proof-r1185.json').write_text(json.dumps(proof, indent=2))
print(json.dumps(dict(productRebuilt=False, sourceChanges=changes,
                      archives={p['platform']: p['archiveBytes'] for p in proof['platforms']})))
