"""Prepare the native-window branch refresh and possible-staleness probe."""
import argparse,hashlib,json,pathlib,shutil,subprocess,tarfile
r=pathlib.Path.cwd();t=r/'.tmp';d=r/'docs/validation/disk-space/drivers'
parser=argparse.ArgumentParser()
mode=parser.add_mutually_exclusive_group()
mode.add_argument('--scope-fix',action='store_true')
mode.add_argument('--recipe-preview',action='store_true')
mode.add_argument('--saved-recipe',action='store_true')
options=parser.parse_args()
scope_fix=options.scope_fix
saved=options.saved_recipe
recipes=options.recipe_preview or saved
rev='r1355'if saved else'r1351'if recipes else'r1346'if scope_fix else'r1341'
def sha(p):
    with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
def fresh(p):
    assert not p.exists(),str(p)
    return p
build_name='saved-recipe-native-build-r1355.txt'if saved else'metadata-recipes-native-build-r1351.txt'if recipes else'branch-refresh-native-final-build-r1344.txt'if scope_fix else'branch-refresh-native-build-r1340.txt'
build=(t/build_name).read_text(encoding='utf-8-sig')
assert all(build.count(s)==1 for s in ('Build succeeded.','    0 Warning(s)','    0 Error(s)'))
managed=r/'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
primary=r
render_revision='r1350'if recipes else'r1345'if scope_fix else'r1339'
render=t/'batch-r1354-mac/render'if saved else t/f'focused-delta-mac-{render_revision}/patch'
assert sha(managed/'FileCat.dll')==sha(render/'FileCat.dll')
if recipes:assert sha(managed/'FileCat.Core.dll')==sha(render/'FileCat.Core.dll')
unix=(d/'native-replacement-ui.py').read_text()
unix=unix.replace('dsa-native-r','dsa-batch-20261107-r').replace('app.tgz','bundle.tgz')
unix=unix.replace("['pgrep', '-f', r'^[^ ]*dotnet +.*FileCat']", "['pgrep','-x','dotnet']")
unix=unix.replace("arguments = ['./FileCat', '--replacement-ui-probe']\n    if sys.platform == 'darwin':\n        arguments.append('--replacement-compositor-capture')","arguments = ['./FileCat', '--branch-refresh-probe']")
unix=unix.replace("probe = data.get('replacementUiProbe')","probe = data.get('branchRefreshProbe')")
unix=unix.replace("assert len(probe['cases']) == 4 and probe['sessions'] == 8","assert probe['branchRoot']=='Photos' and probe['sessions']==3 and probe['originalMarksAndBasketKept'] and probe['newFilesPreserved']")
unix=unix.replace("run.glob('replacement-*.png')","run.glob('branch-*.png')")
if recipes:
    unix=unix.replace("and probe['newFilesPreserved']", "and probe['newFilesPreserved'] and probe['recipePreviewPassed']")
if saved:
    unix=unix.replace("arguments = ['./FileCat', '--branch-refresh-probe']", "arguments = ['./FileCat', '--branch-refresh-probe', '--saved-recipe-probe']\n    if sys.platform == 'darwin':\n        arguments.append('--replacement-compositor-capture')")
    unix=unix.replace("assert re.fullmatch(r'replacement-(memory|indexed)-(keep|replace)-(ClassicDark|Classic|HighContrast)[.]png', frame)",
                      "assert frame == 'branch-saved-recipe.png'")
    unix=unix.replace("and probe['recipePreviewPassed']", "and probe['recipePreviewPassed'] and probe['savedRecipePassed'] and (sys.platform != 'darwin' or len(captured) == 1)")
if scope_fix:
    unix=unix.replace("('DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb')", "('DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb', 'FileCat.dll', 'FileCat.pdb')")
# Preserve the controller's own bounded cleanup/report collection, including failures.
compile(unix,'run.py','exec')
windows=(d/'native-replacement-ui.ps1').read_text()
windows=windows.replace('native-r','batch-r').replace('app.tgz','bundle.tgz').replace("'--replacement-ui-probe'","'--branch-refresh-probe'")
windows=windows.replace('$probe=$data.replacementUiProbe','$probe=$data.branchRefreshProbe')
windows=windows.replace("if(@($probe.cases).Count-ne4-or$probe.sessions-ne8){throw 'Native replacement case count mismatch'}","if($probe.branchRoot-ne'Photos'-or$probe.sessions-ne3-or!$probe.originalMarksAndBasketKept-or!$probe.newFilesPreserved){throw 'Native branch probe evidence mismatch'}")
windows=windows.replace("'replacement-*.png'","'branch-*.png'")
if recipes:
    windows=windows.replace("-or!$probe.newFilesPreserved)", "-or!$probe.newFilesPreserved-or!$probe.recipePreviewPassed)")
if saved:
    windows=windows.replace("-ArgumentList '--branch-refresh-probe'", "-ArgumentList @('--branch-refresh-probe','--saved-recipe-probe')")
    windows=windows.replace("-or!$probe.recipePreviewPassed)", "-or!$probe.recipePreviewPassed-or!$probe.savedRecipePassed)")
proof=[]
old_mac=None
if scope_fix:
    old=json.loads((t/'native-branch-proof-r1341.json').read_text())
    old_mac=next(item for item in old['platforms']if item['platform']=='mac')
for platform in ('windows','mac','linux'):
    stage=fresh(t/('batch-'+rev+'-'+platform));stage.mkdir()
    launcher='FileCat.exe'if platform=='windows'else'FileCat'
    original=primary/('.tmp/desktop-r95-'+platform)/launcher
    if platform=='mac'and old_mac:
        cached=t/'batch-r1341-mac';plan=[]
        for item in old_mac['files']:
            name=item['path'];previous=cached/name;digest=sha(previous);assert digest==item['sha256']
            present=managed/name if (managed/name).is_file() else previous
            current=sha(present);patch=digest!=current
            if patch:
                assert name in ('FileCat.dll','FileCat.pdb','DiskSpaceHarness.dll','DiskSpaceHarness.pdb'),name
                target=stage/'patch'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(present,target)
            plan.append(dict(path=name,baselineSha256=digest,sha256=current,patch=patch))
        (stage/'plan.json').write_text(json.dumps(dict(baseline='/Users/benny/FileCatValidation/dsa-batch-20261107-r1341/app',files=plan),indent=2))
    else:
        for p in managed.iterdir():
            if p.is_file()and p.suffix in('.dll','.pdb','.json'):shutil.copy2(p,stage/p.name)
        for rid in {'windows':('win','win-x64'),'mac':('osx','osx-arm64'),'linux':('linux-x64',)}[platform]:
            if(managed/'runtimes'/rid).exists():shutil.copytree(managed/'runtimes'/rid,stage/'runtimes'/rid)
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
snapshot=fresh(t/('source-'+rev+'-native-branch.tgz'))
with tarfile.open(snapshot,'w:gz')as out:
    for n in paths:out.add(r/n,arcname=n,recursive=False)
fresh(t/('native-branch-proof-'+rev+'.json')).write_text(json.dumps(dict(revision=rev,sources=[dict(path=n,sha256=sha(r/n))for n in paths],sourceSha256=sha(snapshot),platforms=proof),indent=2))
print(json.dumps([dict(platform=x['platform'],bytes=x['bytes'],sha256=x['sha256'])for x in proof],indent=2))
