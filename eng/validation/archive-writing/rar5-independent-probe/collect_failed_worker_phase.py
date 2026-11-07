"""Freeze the unsuccessful first RAR worker batch without rebuilding or repeating its tests."""
from pathlib import Path
import collections,hashlib,json,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as E
task=Path(sys.argv[1]);root=task/'rar5-worker-source-v1';out=task/'rar5-worker-v1';archive=task/'rar5-worker-initial-evidence-v1.tar.gz'
if archive.exists():raise SystemExit('Initial evidence already frozen; do not overwrite it.')
if sys.platform=='win32':
 guard=subprocess.run(['powershell.exe','-NoProfile','-Command',"if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){exit 1}"],capture_output=True)
else:guard=subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],capture_output=True)
assert guard.returncode==0,'Another tester is active; defer initial evidence collection.'
tests=E.parse(out/'results/archive-worker-secrets.trx').findall('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult')
counts=dict(collections.Counter(test.get('outcome') for test in tests));assert counts.get('Failed',0)>0
assert all('.Rar5WorkerPipelineTests.' in test.get('testName') for test in tests if test.get('outcome')=='Failed')
binaries=out/'binaries';binaries.mkdir()
for folder in [root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'src/FileCat.ArchiveWorker/bin/Release/net10.0']:
 for path in folder.iterdir():
  if path.is_file() and path.name.startswith('FileCat.') and path.suffix in ('.dll','.pdb'):shutil.copyfile(path,binaries/path.name)
packet=task/'rar5-worker-source-v1.tar.gz';shutil.copyfile(packet,out/packet.name)
for name in ['rar5-worker-batch-v1.log','rar5-worker-error-v1.txt']:
 if (task/name).exists():shutil.copyfile(task/name,out/name)
(out/'initial-failure.json').write_text(json.dumps({'counts':counts,'production_backend_qualified':False,
 'source_packet_sha256':hashlib.sha256(packet.read_bytes()).hexdigest(),'scope':'Initial failed application run; no independent controls or compiler-source qualification completed.'},indent=2))
rows=[]
for path in sorted(out.rglob('*')):
 assert not path.is_symlink()
 if path.is_file():rows.append({'path':path.relative_to(out).as_posix(),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
(out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
with tarfile.open(archive,'x:gz',dereference=False) as tar:
 for path in sorted(out.rglob('*')):tar.add(path,arcname=out.name+'/'+path.relative_to(out).as_posix(),recursive=False)
facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows),'counts':counts}
(task/'rar5-worker-initial-evidence-v1.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)
