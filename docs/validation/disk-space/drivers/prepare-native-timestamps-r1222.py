"""Freeze the rescan navigation/UI product; reuse the prior completed integration scope."""
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd()
t = r / '.tmp'
d = r / 'docs/validation/disk-space/drivers'
revision = 'r1222'


def sha(p):
    with p.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


def files(source, target):
    target.mkdir(parents=True)
    for p in source.iterdir():
        if p.is_file():
            shutil.copy2(p, target / p.name)


build = (t / 'native-timestamps-private-final-build-r1220.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in build
assert all(build.count(x) == 4 for x in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
# This checkpoint includes explicitly snapshotted uncommitted implementation files.
tracked = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', 'src', 'tests', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'FileCat.slnx'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(tracked) if (r / p).is_file()]
changed = subprocess.check_output(['git', 'diff', '--name-only', '68a435dc..0a2e3a53', '--', 'tests'], text=True).splitlines()
affected = {}
for project in ('FileCat.Core.Tests', 'FileCat.App.Tests', 'FileCat.Platform.Windows.Tests'):
    names = set()
    for p in changed:
        if p.startswith('tests/' + project + '/') and p.endswith('.cs'):
            names.update(re.findall(r'public\s+(?:sealed\s+|partial\s+|abstract\s+)*class\s+(\w+Tests)\b', (r / p).read_text(encoding='utf-8-sig')))
    assert names, project
    affected[project] = sorted(names)


def shell_filters(project):
    return ' '.join("-class '*" + name + "'" for name in affected[project])


def ps_filters(project):
    return ','.join("'-class','*" + name + "'" for name in affected[project])


focus = ['*RescanEpoch*', '*SortedCapturePayload*', '*MaximumAcceptedCapturedValueFieldsFitTheExistingSortKeyLimit', '*PermutedNativeIdentitiesProduceOrderedCanonicalObjectsWithExactAliasesAndBoundedReads', '*SealedIdentityUniquenessIsDerivedRatherThanAcceptedFromTheSuppliedHeader', '*InternalTraversalUsesCapturedRowsWhilePublicChildrenRetainExactNameOrder', '*UniqueIdentityTraversalPreservesFilteredAndUnfilteredAccountingWhenNativeKeysReverseObjectIds', '*NativeDeepSiblingEnumerationHasBoundedDescriptorChurn', '*NativeDirectoryReplacementDuringEnumerationCannotPublishEitherOccupant', '*NativeRelativeChildrenObserveLinksAndFifosWithoutFollowingOrReadingThem', 'FileCat.Core.Tests.AnalysisRefreshNavigationTests.*', '*CapturedNativeTimestamps*', '*InconsistentNativeTimestampPrecision*', '*StatxBirthMask*', '*NativeUnixCreationAndChangeTimes*']
unix = (d / 'epoch-app-unix-template-r1194.sh').read_text()
unix = unix.replace('for phase in app-rescan render settings;do', 'for phase in epoch app-rescan render settings;do')
unix = unix.replace('assembly=FileCat.Rendering.Tests.dll', 'assembly=FileCat.Rendering.Tests.dll; bin="$base/payload/render"')
unix = unix.replace('  app-rescan)', '  epoch) bin="$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0";assembly=FileCat.Core.Tests.dll;set -- ' + ' '.join("-method '" + x + "'" for x in focus) + ';;\n  core) bin="$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0";assembly=FileCat.Core.Tests.dll;set -- -class \'FileCat.Core.Tests.Analysis*\' -class \'*CommandTests\' -class \'*DeviceIoSchedulerTests\' -class \'*PathAndStateTests\' -class \'*ZipListingRevisionTests\' ' + shell_filters('FileCat.Core.Tests') + ';;\n  app-rescan)')
unix = unix.replace("-class '*SettingsPreservationTests';;", "-class '*SettingsPreservationTests' " + shell_filters('FileCat.App.Tests') + ';;')
windows = (d / 'epoch-app-windows-template-r1195.ps1').read_text()
windows = windows.replace("'epoch'{@('-method','*RescanEpoch*')}", "'epoch'{@(" + ','.join("'-method','" + x + "'" for x in focus) + ')}')
windows = windows.replace("'-class','*ZipListingRevisionTests')", "'-class','*ZipListingRevisionTests'," + ps_filters('FileCat.Core.Tests') + ')')
windows = windows.replace("'-class','*SettingsPreservationTests')", "'-class','*SettingsPreservationTests'," + ps_filters('FileCat.App.Tests') + ')')
windows = windows.replace("'-class','*BoundExtractionTests')", "'-class','*BoundExtractionTests'," + ps_filters('FileCat.Platform.Windows.Tests') + ')')
windows = windows.replace('[int]$a.total-ne7-or[int]$a.passed-ne7', '[int]$a.total-ne10-or[int]$a.passed-ne10')
windows = windows.replace('[int]$a.total-ne23-or[int]$a.passed-ne21-or[int]$a.skipped-ne2', '[int]$a.total-ne40-or[int]$a.passed-ne34-or[int]$a.skipped-ne6')
unix = unix.replace("-method '*NativeRescanPreparation*'", "-method '*NativeRescanView*'")
windows = windows.replace("foreach($phase in 'epoch','native-epoch','app-rescan','core','native','render','settings')", "foreach($phase in 'epoch','app-rescan','render','settings')")
windows = windows.replace("'-method','*NativeRescanPreparation*'", "'-method','*NativeRescanView*'")
windows = windows.replace('[int]$a.total-ne10-or[int]$a.passed-ne10', '[int]$a.total-ne9-or[int]$a.passed-ne9')
windows = windows.replace('[int]$a.total-ne40-or[int]$a.passed-ne34-or[int]$a.skipped-ne6', '[int]$a.total-ne15-or[int]$a.passed-ne15-or[int]$a.skipped-ne0')
unix_scale = '''export FILECAT_TEST_MAC_RUSAGE_CONTROL="$base/payload/render/native-counter-layout.json"
for shape in wide deep;do
 idle;printf 'RUNNING phase=1000-%s\\n' "$shape" > "$base/state.txt"
 case="$base/case-1000-$shape";mkdir "$case"
 export FILECAT_TEST_NATIVE_SCALE_COUNT=1000 FILECAT_TEST_NATIVE_SCALE_SHAPE="$shape" FILECAT_TEST_NATIVE_SCALE_ROOT="$case"
 unset FILECAT_TEST_NATIVE_SCALE_SOURCE FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE
 set +e
 (cd "$base/payload/render";"$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml "$case/tests.xml") > "$case/test.log" 2>&1
 result=$?;set -e
 if [ "$result" != 0 ] || ! python3 "$base/payload/render/validate-native-optimization.py" scale "$case";then printf 'FINISHED exit=1 phase=1000-%s\\n' "$shape" > "$base/state.txt";tail -n 30 "$case/test.log";exit 1;fi
done
'''
unix = unix.replace("printf 'FINISHED exit=0\\n'", unix_scale + "printf 'FINISHED exit=0\\n'")
windows_scale = ''' foreach($shape in 'wide','deep'){
  Idle;('RUNNING phase=1000-'+$shape)|Set-Content $state
  $case=Join-Path $base ('case-1000-'+$shape);New-Item -ItemType Directory -Path $case|Out-Null
  $env:FILECAT_TEST_NATIVE_SCALE_COUNT='1000';$env:FILECAT_TEST_NATIVE_SCALE_SHAPE=$shape;$env:FILECAT_TEST_NATIVE_SCALE_ROOT=$case
  Remove-Item Env:FILECAT_TEST_NATIVE_SCALE_SOURCE,Env:FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE -ErrorAction SilentlyContinue
  Set-Location -LiteralPath (Join-Path $payload 'render');$ErrorActionPreference='Continue'
  & 'C:\\Program Files\\dotnet\\dotnet.exe' FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml (Join-Path $case 'tests.xml') *> (Join-Path $case 'test.log')
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
  [xml]$document=[IO.File]::ReadAllText((Join-Path $case 'tests.xml'));$a=$document.assemblies.assembly
  $receipt=Get-Content -LiteralPath (Join-Path $case 'receipt.json') -Raw|ConvertFrom-Json
  if($result-ne0-or[int]$a.total-ne1-or[int]$a.passed-ne1-or!$receipt.completed-or!$receipt.cleanup-or$receipt.remainingSharedQuotaBytes-ne0-or$receipt.sourceCloseFailures-ne0){throw 'Native acquisition control failed'}
 }
'''
windows = windows.replace(" 'FINISHED exit=0'|Set-Content", windows_scale + " 'FINISHED exit=0'|Set-Content")
# App-only fixes after the four-project combined build are separately checked.
fix_build = (t / 'rescan-windows-locator-final-build-r1213.txt').read_text(encoding='utf-8-sig')
assert 'Build FAILED.' not in fix_build
assert all(fix_build.count(x) == 2 for x in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
metadata_names = ('FileDetailsColumnTests', 'AnalysisCurrentDemandTests', 'OwnerColumnAnalysisTests', 'MetadataOutcomeUiTests')
extra_names = ('OwnerColumn*', 'AnalysisCurrentDemandTests', 'AnalysisLeaveTests', 'MetadataPreparationTests', 'MetadataOutcomeUiTests')
unix = unix.replace('for phase in epoch app-rescan render settings;do', 'for phase in epoch app-rescan metadata render settings;do')
unix = unix.replace('  app-rescan)', "  metadata) assembly=FileCat.App.Tests.dll;set -- " + ' '.join("-class '*" + name + "'" for name in metadata_names) + ";;\n  app-rescan)")
unix = unix.replace("-class '*StorageOperationsUiTests';;", "-class '*StorageOperationsUiTests' " + ' '.join("-class '*" + name + "'" for name in extra_names) + ';;')
windows = windows.replace("foreach($phase in 'epoch','app-rescan','render','settings')", "foreach($phase in 'epoch','app-rescan','metadata','render','settings')")
windows = windows.replace("   'app-rescan'", "   'metadata'{@(" + ','.join("'-class','*" + name + "'" for name in metadata_names) + ")}\n   'app-rescan'")
windows = windows.replace("'app-rescan','render','settings'))", "'app-rescan','metadata','render','settings'))")
windows = windows.replace("if($phase-eq'settings')", "if($phase-in@('metadata','settings'))")
windows = windows.replace("'-class','*StorageOperationsUiTests')", "'-class','*StorageOperationsUiTests'," + ','.join("'-class','*" + name + "'" for name in extra_names) + ')')
# New native timestamp contracts require current Core/native and UI regression suites.
unix = unix.replace('for phase in epoch app-rescan metadata render settings;do', 'for phase in epoch app-rescan metadata core render settings;do')
windows = windows.replace("foreach($phase in 'epoch','app-rescan','metadata','render','settings')", "foreach($phase in 'epoch','native-epoch','app-rescan','metadata','core','native','render','settings')")
unix = unix.replace("-method '*NativeRescanView*'", "-method '*NativeRescanView*' -method '*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs'")
windows = windows.replace("'-method','*NativeRescanView*'", "'-method','*NativeRescanView*','-method','*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs'")
windows = windows.replace("'native-epoch'{@('-method','*RescanEpoch*')}", "'native-epoch'{@('-method','*RescanEpoch*','-method','*NativeWindowsCreationAndChangeTimes*')}")
windows = windows.replace('[int]$a.total-ne9-or[int]$a.passed-ne9', '[int]$a.total-ne11-or[int]$a.passed-ne11')
windows = windows.replace('[int]$a.total-ne15-or[int]$a.passed-ne15-or[int]$a.skipped-ne0', '[int]$a.total-ne71-or[int]$a.passed-ne63-or[int]$a.skipped-ne8')
windows = windows.replace('[int]$a.total-ne2-or[int]$a.passed-ne2', '[int]$a.total-ne4-or[int]$a.passed-ne4')

core = r / 'tests/FileCat.Core.Tests/bin/Debug/net10.0'
render = r / 'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'
app = r / 'tests/FileCat.App.Tests/bin/Debug/net10.0'
native = r / 'tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0'
core_sha = sha(core / 'FileCat.Core.dll')
assert all(sha(p / 'FileCat.Core.dll') == core_sha for p in (render, app, native))
proof = dict(revision=revision, head=head, sources=sources, affectedMainTestClasses=affected, coreSha256=core_sha, platforms=[])
validator = """import sys,json,xml.etree.ElementTree as E
mode,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1')) for k in ('total','passed','failed','errors','skipped'))
assert c[0]>0 and c[2:4]==(0,0),c
if mode=='epoch':assert c==(71,71,0,0,0),c
if mode=='app-rescan':assert c==(11,11,0,0,0),c
if mode=='render':assert c==(798,798,0,0,0),c
print(json.dumps(dict(mode=mode,counts=c)))
"""
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(t / f'batch-{revision}-{platform}')
    for project in ('FileCat.Core.Tests', 'FileCat.WorkerProbe', 'FileCat.CoreCrashHarness'):
        files(r / f'tests/{project}/bin/Debug/net10.0', stage / f'tests/{project}/bin/Debug/net10.0')
    shutil.copy2(r / 'FileCat.slnx', stage / 'FileCat.slnx')
    files(render, stage / 'render')
    shutil.copy2(t / 'mac-footprint-counter-control-r1171b.json', stage / 'render/native-counter-layout.json')
    shutil.copy2(d / 'validate-native-optimization-r1184.py', stage / 'render/validate-native-optimization.py')
    for p in app.glob('FileCat.App.Tests.*'):
        if p.is_file():
            shutil.copy2(p, stage / 'render' / p.name)
    for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
        shutil.copytree(render / 'runtimes' / rid, stage / 'render/runtimes' / rid)
    if platform == 'windows':
        files(native, stage / 'native')
    (stage / 'validate.py').write_text(validator, encoding='utf-8', newline='\n')
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz')
    subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    ext = 'ps1' if platform == 'windows' else 'sh'
    runner = fresh(t / f'batch-{platform}-{revision}.{ext}')
    runner.write_text((windows if platform == 'windows' else unix).replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / 'source-r1222-rescan-ui.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources:
        archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'rescan-ui-proof-r1222.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']: p['archiveBytes'] for p in proof['platforms']}))
