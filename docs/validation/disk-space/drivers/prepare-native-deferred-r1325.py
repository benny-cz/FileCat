"""Repeat the native probe with the final captured-cohort summary correction."""
import hashlib,json,pathlib,shutil,subprocess,tarfile
r=pathlib.Path.cwd();t=r/'.tmp';rev='r1325';managed=r/'tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0'
def sha(p):
    with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
def fresh(p):
    assert not p.exists(),str(p)
    return p
for name in ('indexed-deferred-cohort-build-r1323.txt','indexed-deferred-native-build-r1323.txt'):
    build=(t/name).read_text(encoding='utf-8-sig')
    assert all(build.count(s)==1 for s in ('Build succeeded.','    0 Warning(s)','    0 Error(s)'))
assert sha(managed/'FileCat.dll')==sha(r/'tests/FileCat.Rendering.Tests/bin/Debug/net10.0/FileCat.dll')
previous=json.loads((t/'native-deferred-proof-r1322.json').read_text())
unix=(t/'batch-r1322-mac/run.py').read_text();compile(unix,'run.py','exec')
proof=[]
for platform in ('windows','mac','linux'):
    old=next(x for x in previous['platforms']if x['platform']==platform)
    stage=fresh(t/('batch-'+rev+'-'+platform));stage.mkdir()
    files=[]
    for entry in old['files']:
        name=entry['path']
        before=t/('batch-r1322-'+platform)/('patch/'if platform!='windows'and entry['patch']else'')/name
        if not before.exists():
            before=t/('batch-r1313-'+platform)/name
            if name=='run.py'and platform=='linux':before=t/'batch-r1318-linux/run.py'
        assert sha(before)==entry['sha256'],name
        current=managed/name if name in ('FileCat.dll','FileCat.pdb','DiskSpaceHarness.dll','DiskSpaceHarness.pdb')else before
        patch=sha(current)!=entry['sha256']
        if patch:assert name in('FileCat.dll','FileCat.pdb'),name
        files.append(dict(path=name,baselineSha256=entry['sha256'],sha256=sha(current),patch=patch))
        if platform=='windows'or patch:
            target=stage/('patch'if platform!='windows'else'')/name
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(current,target)
    if platform!='windows':
        home='/Users/benny'if platform=='mac'else'/home/benny'
        (stage/'plan.json').write_text(json.dumps(dict(baseline=home+'/FileCatValidation/dsa-batch-20261107-r1322/app',files=files),indent=2))
        (stage/'run.py').write_text(unix)
    archive=fresh(t/('dsa-batch-'+platform+'-'+rev+'.tgz'))
    with tarfile.open(archive,'w:gz')as out:
        for p in sorted(stage.rglob('*')):
            if p.is_file():out.add(p,arcname=p.relative_to(stage).as_posix())
    suffix='.ps1'if platform=='windows'else'.sh'
    runner=(t/('batch-'+platform+'-r1322'+suffix)).read_text().replace(old['sha256'],sha(archive))
    runner_path=fresh(t/('batch-'+platform+'-'+rev+suffix));runner_path.write_text(runner,encoding='utf-8',newline='\n')
    proof.append(dict(platform=platform,bytes=archive.stat().st_size,sha256=sha(archive),runnerSha256=sha(runner_path),reusedGuestCache=platform!='windows',files=files))
paths=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','src','tests','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','FileCat.slnx','global.json'],text=True).splitlines()
snapshot=fresh(t/('source-'+rev+'-native-deferred.tgz'))
with tarfile.open(snapshot,'w:gz')as out:
    for n in paths:out.add(r/n,arcname=n,recursive=False)
fresh(t/('native-deferred-proof-'+rev+'.json')).write_text(json.dumps(dict(revision=rev,baseline='r1322',baselineProofSha256=sha(t/'native-deferred-proof-r1322.json'),sources=[dict(path=n,sha256=sha(r/n))for n in paths],sourceSha256=sha(snapshot),platforms=proof),indent=2))
print(json.dumps([dict(platform=x['platform'],bytes=x['bytes'],sha256=x['sha256'])for x in proof],indent=2))
