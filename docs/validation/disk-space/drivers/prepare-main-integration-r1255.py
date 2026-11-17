"""Freeze the current main merge for one queued guest batch; never run tests on the host."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'; revision = 'r1255'


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def fresh(p):
    assert not p.exists(), str(p)
    return p


for phase in ('core', 'native', 'render', 'app'):
    build = (t / f'main-integration-build-r1254-{phase}.txt').read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in build and all(build.count(v) == 1 for v in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
assert not subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'tests'], text=True).strip()
head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
subprocess.run(['git', 'merge-base', '--is-ancestor', '40480c8e', head], check=True)
paths = subprocess.check_output(['git', 'ls-files', 'src', 'tests', 'FileCat.slnx', 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'], text=True).splitlines()
sources = [dict(path=p, sha256=sha(r / p)) for p in sorted(paths) if (r / p).is_file()]
projects = ['FileCat.Core.Tests', 'FileCat.Rendering.Tests', 'FileCat.App.Tests', 'FileCat.Platform.Windows.Tests', 'FileCat.WorkerProbe', 'FileCat.CoreCrashHarness']
core_sha = sha(r / 'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.dll')
assert all(sha(r / f'tests/{p}/bin/Debug/net10.0/FileCat.Core.dll') == core_sha for p in projects[:4])
focus = ['*NativeRuntimeRescanFactory*', '*NativeRescanView*', '*NativeRescanPreparationKeepsPrivateNamesExcludedAcrossBothEpochs',
         '*NativeOwnedRuntimeView*', '*NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab', '*NonrespondingPreparationFreezesBindingAndNeverStartsAScanAfterReturning']
unix = '''set -eu
cd "$(dirname "$0")";base=$(pwd)
idle(){ if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=%s\n' "$1" > "$base/state.txt";ps -axo pid=,ppid=,args= > "$base/busy-$1.txt";exit 75;fi; }
idle verify
if [ "$(uname -s)" = Darwin ];then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1);else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);fi
[ "$actual" = '@HASH@' ];umask 077;mkdir payload;tar -xzf bundle.tgz -C payload
for phase in app-rescan core render settings;do
 idle "$phase";printf 'RUNNING phase=%s\n' "$phase" > "$base/state.txt"
 project=FileCat.Rendering.Tests;set -- -class '*AnalysisRenderingTests'
 case "$phase" in
 app-rescan)set -- @FOCUS@;;
 core)project=FileCat.Core.Tests;set -- -class '*';;
 settings)project=FileCat.App.Tests;set -- -class '*';;
 esac
 bin="$base/payload/tests/$project/bin/Debug/net10.0"
 set +e
 (cd "$bin";"$HOME/.dotnet/dotnet" "$project.dll" "$@" -noLogo -noColor -maxThreads 2 -xml "$base/$phase.xml") > "$base/$phase.log" 2>&1
 result=$?;set -e
 if [ "$result" != 0 ] || ! python3 "$base/payload/validate.py" "$phase" "$base/$phase.xml";then printf 'FINISHED exit=1 phase=%s\n' "$phase" > "$base/state.txt";tail -n 30 "$base/$phase.log";exit 1;fi
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
'''.replace('@FOCUS@', ' '.join("-method '" + method + "'" for method in focus))
windows = r'''$ErrorActionPreference='Stop';$base=$PSScriptRoot;$state=Join-Path $base 'state.txt'
function Idle([string]$phase){if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){('BUSY phase='+$phase)|Set-Content $state;exit 75}}
try{
 if((Get-Process -Id $PID).SessionId-eq0-or[Environment]::UserName-ne'filecatdsa_r2'){throw 'Use ordinary interactive R2'}
 Idle verify
 if((Get-FileHash -LiteralPath (Join-Path $base 'bundle.tgz') -Algorithm SHA256).Hash.ToLowerInvariant()-ne'@HASH@'){throw 'Bundle changed'}
 $payload=Join-Path $base 'payload';if(Test-Path -LiteralPath $payload){throw 'Fresh payload required'}
 New-Item -ItemType Directory -Path $payload|Out-Null;& tar.exe -xzf (Join-Path $base 'bundle.tgz') -C $payload;if($LASTEXITCODE){throw 'Extraction failed'}
 foreach($phase in 'app-rescan','core','native','render','settings'){
  Idle $phase;('RUNNING phase='+$phase)|Set-Content $state
  $project=switch($phase){'core'{'FileCat.Core.Tests'}'native'{'FileCat.Platform.Windows.Tests'}'settings'{'FileCat.App.Tests'}default{'FileCat.Rendering.Tests'}}
  $filters=if($phase-eq'app-rescan'){@FOCUS@}elseif($phase-eq'render'){@('-class','*AnalysisRenderingTests')}else{@('-class','*')}
  $bin=Join-Path $payload ('tests\'+$project+'\bin\Debug\net10.0');Set-Location -LiteralPath $bin;$ErrorActionPreference='Continue'
  & 'C:\Program Files\dotnet\dotnet.exe' ($project+'.dll') @filters -noLogo -noColor -maxThreads 2 -xml (Join-Path $base ($phase+'.xml')) *> (Join-Path $base ($phase+'.log'))
  $result=$LASTEXITCODE;$ErrorActionPreference='Stop';[xml]$xml=[IO.File]::ReadAllText((Join-Path $base ($phase+'.xml')));$a=$xml.assemblies.assembly
  if($result-ne0-or[int]$a.total-le0-or[int]$a.failed-ne0-or[int]$a.errors-ne0){throw 'Actual test phase failed'}
  if($phase-eq'app-rescan'-and([int]$a.total-ne29-or[int]$a.passed-ne29-or[int]$a.skipped-ne0)){throw 'Focused discovery mismatch'}
  if($phase-eq'render'-and([int]$a.total-ne814-or[int]$a.passed-ne813-or[int]$a.skipped-ne1)){throw 'Rendering discovery mismatch'}
 }
 'FINISHED exit=0'|Set-Content $state;exit 0
}catch{($_|Out-String)|Set-Content (Join-Path $base 'failure.txt');'FINISHED exit=1'|Set-Content $state;exit 1}
'''.replace('@FOCUS@', '@(' + ','.join("'-method','" + method + "'" for method in focus) + ')')
validator = '''import json,sys,xml.etree.ElementTree as E
phase,path=sys.argv[1:];a=E.parse(path).getroot().find('assembly');c=tuple(int(a.get(k,'-1'))for k in ('total','passed','failed','errors','skipped'))
assert c[0]>0 and c[2:4]==(0,0),c
if phase=='app-rescan':assert c==(29,29,0,0,0),c
if phase=='render':assert c==(814,814,0,0,0),c
print(json.dumps(dict(phase=phase,counts=c)))
'''
proof = dict(revision=revision, head=head, mergedMain='40480c8e', sources=sources, coreSha256=core_sha,
             purpose='Focused lifecycle then full Core, Windows native, analyzer rendering and App assemblies after the main merge.', platforms=[])
for platform in ('windows', 'mac', 'linux'):
    stage = fresh(t / f'batch-{revision}-{platform}')
    for project in projects:
        if project == 'FileCat.Platform.Windows.Tests' and platform != 'windows': continue
        source = r / f'tests/{project}/bin/Debug/net10.0'; target = stage / f'tests/{project}/bin/Debug/net10.0'; target.mkdir(parents=True)
        for p in source.iterdir():
            if p.is_file(): shutil.copy2(p, target / p.name)
            elif p.name == 'TestData': shutil.copytree(p, target / p.name)
        for rid in {'windows': ('win', 'win-x64'), 'mac': ('osx', 'osx-arm64'), 'linux': ('linux-x64',)}[platform]:
            if (source / 'runtimes' / rid).exists(): shutil.copytree(source / 'runtimes' / rid, target / 'runtimes' / rid)
    shutil.copy2(r / 'FileCat.slnx', stage / 'FileCat.slnx')
    (stage / 'validate.py').write_text(validator, encoding='utf-8', newline='\n')
    rows = [dict(path=p.relative_to(stage).as_posix(), sha256=sha(p)) for p in sorted(stage.rglob('*')) if p.is_file()]
    archive = fresh(t / f'dsa-batch-{platform}-{revision}.tgz');subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
    runner = fresh(t / f'batch-{platform}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    runner.write_text((windows if platform == 'windows' else unix).replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
    proof['platforms'].append(dict(platform=platform, inputs=rows, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner)))
snapshot = fresh(t / 'source-r1255-main-integration.tgz')
with tarfile.open(snapshot, 'w:gz') as archive:
    for row in sources: archive.add(r / row['path'], arcname=row['path'], recursive=False)
proof['sourceSnapshotSha256'] = sha(snapshot)
fresh(t / 'main-integration-proof-r1255.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({p['platform']:p['archiveBytes']for p in proof['platforms']}))
