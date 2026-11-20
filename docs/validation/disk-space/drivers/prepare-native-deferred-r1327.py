"""Windows-only retry after a foreign worker interrupted native startup; no binary changes."""
import hashlib,json,pathlib,tarfile
r=pathlib.Path.cwd();t=r/'.tmp';rev='r1327'
def sha(p):
    with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
old=json.loads((t/'native-deferred-proof-r1326.json').read_text())
assert all(sha(r/x['path'])==x['sha256']for x in old['sources'])
win=next(x for x in old['platforms']if x['platform']=='windows')
stage=t/('batch-'+rev+'-windows');assert not stage.exists();stage.mkdir()
files=[dict(path=x['path'],baselineSha256=x['sha256'],sha256=x['sha256'],patch=False)for x in win['files']]
plan=dict(baseline=r'C:\FileCatDsa20261104R2\batch-r1326\app',files=files)
(stage/'plan.json').write_text(json.dumps(plan,indent=2))
archive=t/('dsa-batch-windows-'+rev+'.tgz');assert not archive.exists()
with tarfile.open(archive,'w:gz')as out:out.add(stage/'plan.json',arcname='plan.json')
runner=(t/'batch-windows-r1326.ps1').read_text().replace(win['sha256'],sha(archive))
# The Windows source fixture itself needs space in addition to the verified application clone.
runner=runner.replace(" $names=@{}", " $cloneBytes=[long]0;foreach($item in $plan.files){$cloneBytes+=(Get-Item -LiteralPath (Join-Path $plan.baseline $item.path)).Length}\n $drive=Get-CimInstance Win32_LogicalDisk -Filter \"DeviceID='C:'\";if($drive.FreeSpace-lt($cloneBytes+512MB)){throw 'Insufficient native clone and generated fixture space'}\n $names=@{}")
runner_path=t/('batch-windows-'+rev+'.ps1');assert not runner_path.exists();runner_path.write_text(runner,encoding='utf-8',newline='\n')
proof=t/('native-deferred-proof-'+rev+'.json');assert not proof.exists()
proof.write_text(json.dumps(dict(revision=rev,baseline='r1326',baselineProofSha256=sha(t/'native-deferred-proof-r1326.json'),unchangedSourcePacketSha256=old['sourceSha256'],verifiedFiles=len(files),binaryChanges=0,archiveSha256=sha(archive),runnerSha256=sha(runner_path),bytes=archive.stat().st_size),indent=2))
print(json.dumps(dict(files=len(files),bytes=archive.stat().st_size,binaryChanges=0)))
