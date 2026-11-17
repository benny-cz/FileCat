"""Continue native review validation: harness-only Mac patch, fresh VM payloads."""
import hashlib,json,pathlib,shutil,tarfile,subprocess
r=pathlib.Path.cwd();t=r/'.tmp';rev='r1318';baseline='r1313';managed=r/'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
def sha(p):
    with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
def fresh(p):
    assert not p.exists(),str(p)
    return p
build=(t/'indexed-deferred-native-build-r1317.txt').read_text(encoding='utf-8-sig')
assert all(build.count(s)==1 for s in ('Build succeeded.','    0 Warning(s)','    0 Error(s)'))
original=json.loads((t/'native-deferred-proof-r1313.json').read_text())
for row in original['sources']:
    if sha(r/row['path'])!=row['sha256']:
        assert row['path']in('tests/FileCat.NativeAcceptance/DiskSpaceHarness/Program.cs','tests/FileCat.NativeAcceptance/DiskSpaceHarness/IndexedDeferred.cs')
unix=(t/'batch-r1313-mac/run.py').read_text()
unix=unix.replace("arguments = ['./FileCat', '--indexed-deferred-probe']", "arguments = ['./FileCat', '--indexed-deferred-probe']\n    if sys.platform=='darwin':arguments.append('--replacement-compositor-capture')")
unix=unix.replace("replacement-(memory|indexed)-(keep|replace)-(ClassicDark|Classic|HighContrast)", "deferred-review-(ClassicDark|Classic|HighContrast)")
unix=unix.replace("    names = set()", "    required=sum((baseline/e['path']).stat().st_size for e in plan['files'])+256*1024*1024\n    assert shutil.disk_usage(base).free>=required, 'Insufficient clone space'\n    names = set()")
compile(unix,'run.py','exec')
proof=[]
for platform in ('windows','mac','linux'):
    old=next(x for x in original['platforms']if x['platform']==platform)
    source=t/('batch-'+baseline+'-'+platform);stage=fresh(t/('batch-'+rev+'-'+platform));stage.mkdir()
    for entry in old['files']:
        assert sha(source/entry['path'])==entry['sha256']
        p=managed/entry['path']
        if p.exists() and p.suffix in('.dll','.pdb','.json'):
            if sha(p)!=entry['sha256']:assert entry['path']in('DiskSpaceHarness.dll','DiskSpaceHarness.pdb'),entry['path']
    if platform=='mac':
        files=[]
        for entry in old['files']:
            patch=entry['path']in('DiskSpaceHarness.dll','DiskSpaceHarness.pdb')
            current=managed/entry['path']if patch else source/entry['path']
            files.append(dict(path=entry['path'],baselineSha256=entry['sha256'],sha256=sha(current),patch=patch))
            if patch:
                target=stage/'patch'/entry['path'];target.parent.mkdir(exist_ok=True);shutil.copy2(current,target)
        (stage/'plan.json').write_text(json.dumps(dict(baseline='/Users/benny/FileCatValidation/dsa-batch-20261107-r1313/app',files=files),indent=2))
    else:
        for entry in old['files']:
            current=managed/entry['path']if entry['path']in('DiskSpaceHarness.dll','DiskSpaceHarness.pdb')else source/entry['path']
            target=stage/entry['path'];target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(current,target)
    if platform!='windows':(stage/'run.py').write_text(unix)
    archive=fresh(t/('dsa-batch-'+platform+'-'+rev+'.tgz'))
    with tarfile.open(archive,'w:gz')as out:
        for p in sorted(stage.rglob('*')):
            if p.is_file():out.add(p,arcname=p.relative_to(stage).as_posix())
    suffix='.ps1'if platform=='windows'else'.sh'
    runner=(t/('batch-'+platform+'-'+baseline+suffix)).read_text().replace(old['sha256'],sha(archive))
    runner_path=fresh(t/('batch-'+platform+'-'+rev+suffix));runner_path.write_text(runner,encoding='utf-8',newline='\n')
    proof.append(dict(platform=platform,bytes=archive.stat().st_size,sha256=sha(archive),runnerSha256=sha(runner_path),reusedGuestCache=platform=='mac',files=[dict(path=p.relative_to(stage).as_posix(),sha256=sha(p))for p in sorted(stage.rglob('*'))if p.is_file()]))
paths=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','src','tests','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','FileCat.slnx','global.json'],text=True).splitlines()
snapshot=fresh(t/('source-'+rev+'-native-deferred.tgz'))
with tarfile.open(snapshot,'w:gz')as out:
    for n in paths:out.add(r/n,arcname=n,recursive=False)
fresh(t/('native-deferred-proof-'+rev+'.json')).write_text(json.dumps(dict(revision=rev,baseline=baseline,baselineProofSha256=sha(t/'native-deferred-proof-r1313.json'),sources=[dict(path=n,sha256=sha(r/n))for n in paths],sourceSha256=sha(snapshot),platforms=proof),indent=2))
print(json.dumps([dict(platform=x['platform'],bytes=x['bytes'],sha256=x['sha256'])for x in proof],indent=2))
