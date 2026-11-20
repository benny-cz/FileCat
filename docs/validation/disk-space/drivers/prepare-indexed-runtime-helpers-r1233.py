import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'
revision = 'r1233'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


build = (t / 'indexed-runtime-helpers-controls-build-r1232.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in build and all(build.count(x) == 2 for x in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
core = r / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'; render = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
assert sha(core / 'FileCat.Core.dll') == sha(render / 'FileCat.Core.dll')
paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'FileCat.slnx'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths) if (r / p).is_file()]
proof = dict(revision=revision, head=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), sources=sources, platforms=[])
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(t / f'batch-{revision}-{platform}')
    for source, target in ((core, stage / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'),
                           (r / 'tests/FileCat.WorkerProbe/bin/Debug/net10.0', stage / 'tests/FileCat.WorkerProbe/bin/Debug/net10.0'),
                           (r / 'tests/FileCat.CoreCrashHarness/bin/Debug/net10.0', stage / 'tests/FileCat.CoreCrashHarness/bin/Debug/net10.0'),
                           (render, stage / 'render')):
        target.mkdir(parents=True)
        for p in source.iterdir():
            if p.is_file() and not p.name.startswith('FileCat.App.Tests.'):
                shutil.copy2(p, target / p.name)
    shutil.copy2(r / 'FileCat.slnx', stage / 'FileCat.slnx')
    for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
        shutil.copytree(render / 'runtimes' / rid, stage / 'render/runtimes' / rid)
    unix = (d / 'epoch-app-unix-template-r1194.sh').read_text()
    unix = unix.replace('for phase in app-rescan render settings;do', 'for phase in epoch app-rescan core render;do')
    unix = unix.replace('assembly=FileCat.Rendering.Tests.dll', 'assembly=FileCat.Rendering.Tests.dll;bin="$base/payload/render"')
    unix = unix.replace('  app-rescan)', "  epoch) bin=\"$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0\";assembly=FileCat.Core.Tests.dll;set -- -method '*IndexedRefreshNavigation*';;\n  core) bin=\"$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0\";assembly=FileCat.Core.Tests.dll;set -- -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -class '*PathAndStateTests' -class '*ZipListingRevisionTests';;\n  app-rescan)")
    unix = unix.replace("-method '*NativeRescanPreparation*'", "-method '*NativeRuntimeRescanFactory*' -method '*NativeRescanView*' -method '*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs'")
    windows = (d / 'epoch-app-windows-template-r1195.ps1').read_text()
    windows = windows.replace("foreach($phase in 'epoch','native-epoch','app-rescan','core','native','render','settings')", "foreach($phase in 'epoch','app-rescan','core','render')")
    windows = windows.replace("'epoch'{@('-method','*RescanEpoch*')}", "'epoch'{@('-method','*IndexedRefreshNavigation*')}")
    windows = windows.replace("'-method','*NativeRescanPreparation*'", "'-method','*NativeRuntimeRescanFactory*','-method','*NativeRescanView*','-method','*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs'")
    windows = windows.replace('[int]$a.total-ne7-or[int]$a.passed-ne7', '[int]$a.total-ne14-or[int]$a.passed-ne14')
    windows = windows.replace('[int]$a.total-ne23-or[int]$a.passed-ne21-or[int]$a.skipped-ne2', '[int]$a.total-ne15-or[int]$a.passed-ne15-or[int]$a.skipped-ne0')
    (stage / 'validate.py').write_text("""import sys,json,xml.etree.ElementTree as E
phase,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1'))for k in ('total','passed','failed','errors','skipped'))
assert c[0]>0 and c[2:4]==(0,0),c
if phase=='epoch':assert c==(15,15,0,0,0),c
if phase=='app-rescan':assert c==(14,14,0,0,0),c
if phase=='render':assert c==(801,801,0,0,0),c
print(json.dumps(dict(phase=phase,counts=c)))
""", encoding='utf-8', newline='\n')
    inputs = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz');subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    runner = fresh(t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    runner.write_text((windows if platform == 'windows' else unix).replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=inputs, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / 'source-r1233-indexed-runtime-helpers.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources:archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'indexed-runtime-helpers-proof-r1233.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']:p['archiveBytes']for p in proof['platforms']}))
