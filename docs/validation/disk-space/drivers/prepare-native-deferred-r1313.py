"""Prepare one native-window batch with a 50K-path scan and three themed review frames."""
import hashlib,json,pathlib,shutil,subprocess,tarfile
r=pathlib.Path.cwd();t=r/'.tmp';d=r/'docs/validation/disk-space/drivers';rev='r1313'
def sha(p):
    with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
def fresh(p):
    assert not p.exists(),str(p)
    return p
build=(t/'indexed-deferred-native-build-r1312.txt').read_text(encoding='utf-8-sig')
assert all(build.count(s)==1 for s in ('Build succeeded.','    0 Warning(s)','    0 Error(s)'))
managed=r/'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
primary=r.parent/'disk-space-analysis'
assert sha(managed/'FileCat.dll')==sha(t/'focused-delta-mac-r1310/patch/FileCat.dll')
unix=(d/'native-replacement-ui.py').read_text()
unix=unix.replace('dsa-native-r','dsa-batch-20261107-r').replace('app.tgz','bundle.tgz')
unix=unix.replace("['pgrep', '-f', r'^[^ ]*dotnet +.*FileCat']", "['pgrep','-x','dotnet']")
unix=unix.replace("arguments = ['./FileCat', '--replacement-ui-probe']\n    if sys.platform == 'darwin':\n        arguments.append('--replacement-compositor-capture')","arguments = ['./FileCat', '--indexed-deferred-probe']")
unix=unix.replace("probe = data.get('replacementUiProbe')","probe = data.get('indexedDeferredProbe')")
unix=unix.replace("assert len(probe['cases']) == 4 and probe['sessions'] == 8","assert probe['completedMatches']==50000 and probe['reviewedMarks']==50000 and len(probe['frames'])==3 and probe['sessions']==2")
unix=unix.replace("run.glob('replacement-*.png')","run.glob('deferred-*.png')")
unix=unix.replace("result['finished'] = data","result['finished'] = data")
unix=unix.replace("        for name in ('result.json', 'desktop.log', 'owned.pid'):", "        for name in ('result.json', 'desktop.log', 'owned.pid'):")
# Preserve the controller's own bounded cleanup/report collection, including failures.
compile(unix,'run.py','exec')
windows=(d/'native-replacement-ui.ps1').read_text()
windows=windows.replace('native-r','batch-r').replace('app.tgz','bundle.tgz').replace("'--replacement-ui-probe'","'--indexed-deferred-probe'")
windows=windows.replace('$probe=$data.replacementUiProbe','$probe=$data.indexedDeferredProbe')
windows=windows.replace("if(@($probe.cases).Count-ne4-or$probe.sessions-ne8){throw 'Native replacement case count mismatch'}","if($probe.completedMatches-ne50000-or$probe.reviewedMarks-ne50000-or@($probe.frames).Count-ne3-or$probe.sessions-ne2){throw 'Native deferred case count mismatch'}")
windows=windows.replace("'replacement-*.png'","'deferred-*.png'")
proof=[]
for platform in ('windows','mac','linux'):
    stage=fresh(t/('batch-'+rev+'-'+platform));stage.mkdir()
    for p in managed.iterdir():
        if p.is_file()and p.suffix in('.dll','.pdb','.json'):shutil.copy2(p,stage/p.name)
    for rid in {'windows':('win','win-x64'),'mac':('osx','osx-arm64'),'linux':('linux-x64',)}[platform]:
        if(managed/'runtimes'/rid).exists():shutil.copytree(managed/'runtimes'/rid,stage/'runtimes'/rid)
    launcher='FileCat.exe'if platform=='windows'else'FileCat'
    original=primary/('.tmp/desktop-r95-'+platform)/launcher
    shutil.copy2(original,stage/launcher)
    if platform!='windows':(stage/'run.py').write_text(unix)
    files=[dict(path=p.relative_to(stage).as_posix(),bytes=p.stat().st_size,sha256=sha(p))for p in sorted(stage.rglob('*'))if p.is_file()]
    archive=fresh(t/('dsa-batch-'+platform+'-'+rev+'.tgz'))
    with tarfile.open(archive,'w:gz')as out:
        for p in sorted(stage.rglob('*')):
            if p.is_file():out.add(p,arcname=p.relative_to(stage).as_posix())
    digest=sha(archive)
    if platform=='windows':
        runner="& {\n"+windows+"\n} -ExpectedHash '"+digest+"'\n"
        # Script param() stays first in its script block; the ordinary scheduled-task
        # launcher owns the desktop/session, rather than vmrun's ambiguous interactive mode.
        runner_path=fresh(t/('batch-windows-'+rev+'.ps1'))
    else:
        runner='''set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1; then printf 'BUSY phase=verify\\n' > state.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '@HASH@' ]; then printf 'FAILED phase=integrity\\n' > state.txt; exit 74; fi
umask 077
mkdir controller
tar -xzf bundle.tgz -C controller run.py
printf 'RUNNING phase=native\\n' > state.txt
set +e
python3 controller/run.py "$PWD" '@HASH@'
code=$?
printf 'FINISHED exit=%s\\n' "$code" > state.txt
exit "$code"
'''.replace('@HASH@',digest)
        runner_path=fresh(t/('batch-'+platform+'-'+rev+'.sh'))
    runner_path.write_text(runner,encoding='utf-8',newline='\n')
    proof.append(dict(platform=platform,bytes=archive.stat().st_size,sha256=digest,runnerSha256=sha(runner_path),launcherSource=str(original),launcherSha256=sha(original),files=files))
paths=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','src','tests','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','FileCat.slnx','global.json'],text=True).splitlines()
snapshot=fresh(t/('source-'+rev+'-native-deferred.tgz'))
with tarfile.open(snapshot,'w:gz')as out:
    for n in paths:out.add(r/n,arcname=n,recursive=False)
fresh(t/('native-deferred-proof-'+rev+'.json')).write_text(json.dumps(dict(revision=rev,sources=[dict(path=n,sha256=sha(r/n))for n in paths],sourceSha256=sha(snapshot),platforms=proof),indent=2))
print(json.dumps([dict(platform=x['platform'],bytes=x['bytes'],sha256=x['sha256'])for x in proof],indent=2))
