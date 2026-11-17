"""Reuse verified r1305 dependencies; overlay the review retry/tooltip fix and run the complete deferred-selection subset after the wrapping-only product correction."""
import hashlib, json, pathlib, shutil, subprocess, tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'
revision = 'r1324'; baseline_revision = 'r1305'; assembly = 'FileCat.Rendering.Tests'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
def fresh(p):
    assert not p.exists(), str(p)
    return p
build = (t/'indexed-deferred-cohort-build-r1323.txt').read_text(encoding='utf-8-sig')
assert all(build.count(s) == 1 for s in ('Build succeeded.', '    0 Warning(s)', '    0 Error(s)'))
old = json.loads((t/'indexed-deferred-proof-r1305.json').read_text())
source_paths = [row['path'] for row in old['sources']]
changed_sources = [row['path'] for row in old['sources'] if sha(r/row['path']) != row['sha256']]
assert changed_sources == ['src/FileCat.App/Services/AnalysisDeferredIndexedMatches.cs','src/FileCat.App/Views/DiskSpaceWindow.DeferredSelection.cs','src/FileCat.App/Views/DiskSpaceWindow.IndexedDeferredSelection.cs','tests/FileCat.NativeAcceptance/DiskSpaceHarness/Program.cs','tests/FileCat.Rendering.Tests/AnalysisRenderingTests.DeferredSelection.cs','tests/FileCat.Rendering.Tests/AnalysisRenderingTests.IndexedDeferred.cs'], changed_sources
source_paths = subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','src','tests','FileCat.slnx','global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props'],text=True).splitlines()
snapshot = fresh(t/('source-'+revision+'-indexed-deferred.tgz'))
with tarfile.open(snapshot,'w:gz') as archive:
    for name in source_paths: archive.add(r/name,arcname=name,recursive=False)
unix = (d/'run-focused-test-delta.py').read_text()
unix = unix.replace("['pgrep','-f','^[^ ]*dotnet +.*FileCat']", "['pgrep','-x','dotnet']")
unix = unix.replace(" for item in plan['files']:\n  relative=", " required=sum((baseline/pathlib.PurePosixPath(i['path'])).stat().st_size for i in plan['files'])+256*1024*1024\n if shutil.disk_usage(output).free<required:raise RuntimeError('Insufficient clone space')\n for item in plan['files']:\n  relative=",1)
unix = unix.replace("(output/'cache-proof.txt').write_text('VERIFIED cachedFiles='+str(len(plan['files']))+' changedFiles='+str(sum(bool(row['patch'])for row in plan['files']))+'\\n')", "(output/'cache-proof.json').write_text(json.dumps(plan,indent=2))")
unix = unix.replace("('render-'+revision+'.xml')", "'render.xml'").replace("('render-'+revision+'.log')", "'render.log'")
unix = unix.replace(" state('FINISHED exit='+str(code));sys.exit(code)", " import xml.etree.ElementTree as E\n a=E.parse(output/'render.xml').getroot().find('assembly')\n counts=tuple(int(a.get(k,'-1'))for k in ('total','passed','failed','errors','skipped'))\n if code==0 and counts!=(40,40,0,0,0):raise RuntimeError('Discovery mismatch: '+str(counts))\n state('FINISHED exit='+str(code));sys.exit(code)")
unix=unix.replace("not in(plan['assembly'],plan['assembly'][:-4]+'.pdb')","not in(plan['assembly'],plan['assembly'][:-4]+'.pdb','FileCat.dll','FileCat.pdb')")
compile(unix,'run.py','exec')
windows = (d/'run-focused-test-delta.ps1').read_text()
windows = windows.replace(" foreach($item in $plan.files){", " $cloneBytes=[long]0;foreach($item in $plan.files){$cloneBytes+=(Get-Item -LiteralPath (Join-Path $plan.baseline $item.path)).Length}\n $drive=Get-CimInstance Win32_LogicalDisk -Filter \"DeviceID='C:'\";if($drive.FreeSpace-lt($cloneBytes+256MB)){throw 'Insufficient clone space'}\n foreach($item in $plan.files){",1)
windows = windows.replace(" ('VERIFIED cachedFiles='+$plan.files.Count+' changedFiles='+@($plan.files|Where-Object{$_.patch}).Count)|Set-Content (Join-Path $base 'cache-proof.txt')", " $plan|ConvertTo-Json -Depth 8|Set-Content (Join-Path $base 'cache-proof.json')")
windows = windows.replace('render-@REV@.xml','render.xml').replace('render-@REV@.log','render.log')
windows = windows.replace("$ErrorActionPreference='Stop';('FINISHED exit='+$result)", "$ErrorActionPreference='Stop';if($result-eq0){[xml]$xml=Get-Content -Raw -LiteralPath (Join-Path $base 'render.xml');$a=$xml.assemblies.assembly;if([int]$a.total-ne40-or[int]$a.passed-ne40-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne0){throw 'Rendering discovery mismatch'}};('FINISHED exit='+$result)")
windows=windows.replace("$item.path-notin $plan.assembly,($plan.assembly-replace'[.]dll$','.pdb')","$item.path-notin $plan.assembly,($plan.assembly-replace'[.]dll$','.pdb'),'FileCat.dll','FileCat.pdb'")
proof=[]; current=r/('tests/'+assembly+'/bin/Debug/net10.0')
for platform in ('windows','mac','linux'):
    stage=fresh(t/('focused-delta-'+platform+'-'+revision));stage.mkdir()
    baseline=t/('batch-'+baseline_revision+'-'+platform)/'render'; files=[]; ignored=[]
    for p in sorted(baseline.rglob('*')):
        if not p.is_file():continue
        name=p.relative_to(baseline).as_posix(); new=current/name
        before=sha(p); after=sha(new); patch=before!=after
        if patch and name==assembly+'.exe':
            ignored.append(dict(path=name,baselineSha256=before,currentSha256=after,reason='Unused test apphost; DLL is invoked through dotnet and baseline apphost is retained'))
            after=before;patch=False
        if patch:
            assert name in (assembly+'.dll',assembly+'.pdb','FileCat.dll','FileCat.pdb'), name
            target=stage/'patch'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(new,target)
        files.append(dict(path=name,baselineSha256=before,sha256=after,patch=patch))
    assert sorted(x['path'] for x in files if x['patch'])==sorted([assembly+'.dll',assembly+'.pdb','FileCat.dll','FileCat.pdb'])
    remote=('C:\\FileCatDsa20261104R2\\batch-'+baseline_revision+'\\payload\\render')if platform=='windows' else('/Users/benny'if platform=='mac'else'/home/benny')+'/FileCatValidation/dsa-batch-20261107-'+baseline_revision+'/payload/render'
    plan=dict(revision=revision,baseline=remote,assembly=assembly+'.dll',methods=['*DeferredSelection*','*NativeDeferredIndexed*'],files=files)
    (stage/'plan.json').write_text(json.dumps(plan,indent=2))
    if platform!='windows':(stage/'run.py').write_text(unix)
    archive=fresh(t/('dsa-batch-'+platform+'-'+revision+'.tgz'))
    with tarfile.open(archive,'w:gz')as out:
        for p in sorted(stage.rglob('*')):
            if p.is_file():out.add(p,arcname=p.relative_to(stage).as_posix())
    if platform=='windows':runner=windows.replace('@BASELINE@',remote).replace('@REV@',revision).replace('@HASH@',sha(archive));extension='.ps1'
    else:
        runner='''set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1; then printf 'BUSY phase=verify\\n' > state.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '@HASH@' ]; then printf 'FAILED phase=integrity\\n' > state.txt; exit 74; fi
umask 077
mkdir driver
tar -xzf bundle.tgz -C driver
python3 driver/run.py
'''.replace('@HASH@',sha(archive));extension='.sh'
    runner_path=fresh(t/('batch-'+platform+'-'+revision+extension));runner_path.write_text(runner,encoding='utf-8',newline='\n')
    proof.append(dict(platform=platform,files=len(files),changed=[x['path']for x in files if x['patch']],ignoredUnusedApphost=ignored,bytes=archive.stat().st_size,sha256=sha(archive),runnerSha256=sha(runner_path)))
(t/('cache-input-'+revision)).mkdir()
packet=dict(revision=revision,baseline=baseline_revision,baselineProofSha256=sha(t/'indexed-deferred-proof-r1305.json'),snapshotSha256=sha(snapshot),changedSources=changed_sources,sources=[dict(path=n,sha256=sha(r/n))for n in source_paths],platforms=proof)
fresh(t/('indexed-deferred-proof-'+revision+'.json')).write_text(json.dumps(packet,indent=2))
print(json.dumps(proof,indent=2))
