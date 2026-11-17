"""Freeze a rendering-fixture-only update over the qualified r1170 product bytes."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

root = pathlib.Path.cwd()
temp = root / '.tmp'
drivers = root / 'docs/validation/disk-space/drivers'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(path):
    if path.exists():
        raise RuntimeError('Fresh destination required: ' + str(path))
    return path


build = (temp / 'native-acquisition-build-r1175.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in build and all(x in build for x in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
baseline_source = json.loads((temp / 'source-candidate-r1170.json').read_text(encoding='utf-8-sig'))
prior_instrumentation = {'tests/FileCat.CoreCrashHarness/CsvReportScale.cs',
                         'tests/FileCat.CoreCrashHarness/Program.cs',
                         'tests/FileCat.CoreCrashHarness/AnalysisPhysicalMemory.cs'}
allowed = {'tests/FileCat.Rendering.Tests/FileCat.Rendering.Tests.csproj'} | prior_instrumentation
for row in baseline_source:
    if row['path'] not in allowed:
        assert sha(root / row['path']) == row['sha256'], row['path']
for path in prior_instrumentation:
    committed = subprocess.check_output(['git', 'show', '378fab8:' + path])
    assert (root / path).read_bytes().replace(b'\r\n', b'\n') == committed.replace(b'\r\n', b'\n'), path
files = ['tests/FileCat.Rendering.Tests/AnalysisAcquisitionScaleTests.cs',
         'tests/FileCat.Rendering.Tests/FileCat.Rendering.Tests.csproj',
         'tests/FileCat.CoreCrashHarness/AnalysisPhysicalMemory.cs']
files += ['docs/validation/disk-space/drivers/' + name for name in
          ('prepare-native-acquisition-r1175.py', 'native-acquisition-unix-template-r1175.sh',
           'native-acquisition-windows-template-r1175.ps1', 'validate-native-acquisition-r1175.py')]
source = [dict(path=p, sha256=sha(root / p)) for p in files]
qualified = json.loads((root / 'docs/validation/disk-space/source-manifest-r1170-main-sync-input-capacity.json').read_text())
proof = dict(revision='r1175', productRebuilt=False, productQualification='source-manifest-r1170-main-sync-input-capacity.json', sources=source, platforms=[])
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(temp / f'batch-r1175-{platform}')
    old = temp / f'batch-r1170-{platform}'
    shutil.copytree(old / 'render', stage / 'render')
    expected = {r['path']: r['sha256'] for r in qualified['inputs']['ubuntu' if platform == 'linux' else platform]}
    for path in (stage / 'render').rglob('*'):
        if path.is_file():
            relative = path.relative_to(stage).as_posix()
            assert sha(path) == expected[relative], relative
    for name in ('FileCat.Rendering.Tests.dll', 'FileCat.Rendering.Tests.pdb'):
        shutil.copy2(root / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0' / name, stage / 'render' / name)
    # Verify the compile-time product references, too. The only different FileCat assembly is this fixture.
    for path in (stage / 'render').glob('FileCat*.dll'):
        if path.name not in ('FileCat.Rendering.Tests.dll', 'FileCat.App.Tests.dll', 'FileCat.CoreCrashHarness.dll'):
            assert sha(path) == sha(root / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0' / path.name), path.name
    shutil.copy2(temp / 'mac-footprint-counter-control-r1171b.json', stage / 'native-counter-layout.json')
    shutil.copy2(drivers / 'validate-native-acquisition-r1175.py', stage / 'validate.py')
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p), bytes=p.stat().st_size)
            for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(temp / f'dsa-batch-{platform}-r1175.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    ext = 'ps1' if platform == 'windows' else 'sh'
    template = drivers / f'native-acquisition-{"windows" if platform == "windows" else "unix"}-template-r1175.{ext}'
    runner = fresh(temp / f'batch-{platform}-r1175.{ext}')
    runner.write_text(template.read_text(encoding='utf-8-sig').replace('@HASH@', sha(archive)), newline='\n', encoding='utf-8')
    proof['platforms'].append(dict(platform=platform, inputs=rows, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
with tarfile.open(fresh(temp / 'source-r1175-native-acquisition.tgz'), 'w:gz') as archive:
    for row in source:
        archive.add(root / row['path'], arcname=row['path'], recursive=False)
fresh(temp / 'native-acquisition-proof-r1175.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: dict(bytes=p['archiveBytes'], files=len(p['inputs'])) for p in proof['platforms']}))
