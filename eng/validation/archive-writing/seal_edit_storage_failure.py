"""Close the original failed storage cohort without changing/rebuilding its source or re-running a test."""
from pathlib import Path
import hashlib,json,shutil,subprocess,sys,tarfile
def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
task=Path(sys.argv[1]).resolve();out=task/'awe-storage-v1';root=task/'archive-workflow-source-v1'
assert not(out/'files.json').exists()
subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
delta=json.loads((out/'delta.json').read_text());assert delta['candidate_commit']=='03ec22014ae2e5d76288a3774e7e4287253126e3'
ledger={row['path']:row for row in json.loads((out/'source-files.json').read_text())['files']}
for row in ledger.values():assert sha(root/row['path'])==row['sha256'],row['path']
audit=json.loads((out/'compiler-sources/facts.json').read_text());assert audit['compilerSourceChecksumsMatched']
for row in audit['binaries']:
    assert row['pdbBoundToAssembly']
    assert sha(out/'binaries'/(row['assembly']+'.dll'))==row['dllSha256'].lower()
    assert sha(out/'binaries'/(row['assembly']+'.pdb'))==row['pdbSha256'].lower()
uncompiled=[]
for row in delta['files']:
    if not row['path'].endswith('.cs'):continue
    if row['path'].startswith('tests/FileCat.Platform.Windows.Tests/'):
        uncompiled.append(dict(path=row['path'],sha256=row['sha256'],reason='Windows-only test project was not compiled on this Unix lane.'));continue
    found=[item for item in audit['sources'] if item['document'].replace('\\','/').endswith('/'+row['path'])]
    assert found and all(item['sha256'].lower()==row['sha256'] for item in found),row['path']
(out/'uncompiled-platform-sources.json').write_text(json.dumps(uncompiled,indent=2))
census={'owned_filecat_or_test_processes':0,'production_backend_qualified':False,'storage_integrated':False}
if sys.platform=='darwin':
    census['preserved_wake_helper']=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True).strip();assert census['preserved_wake_helper']
subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
(out/'final-census.json').write_text(json.dumps(census,indent=2))
shutil.copyfile(root/'eng/validation/archive-writing/run_edit_storage_batch.py',out/'executed-storage-batch.py')
shutil.copyfile(Path(__file__),out/'executed-seal-repair.py')
(out/'seal-repair.json').write_text(json.dumps({'test_or_product_source_changed':False,'tests_repeated':False,
    'reason':'Original sealer required PDB coverage from the Windows-only test project on Unix; retain its source hash and Windows lane coverage separately.'},indent=2))
rows=[dict(path=path.relative_to(out).as_posix(),bytes=path.stat().st_size,sha256=sha(path))for path in sorted(out.rglob('*'))if path.is_file()]
(out/'files.json').write_text(json.dumps({'files':rows},indent=2));packet=task/'awe-storage-v1-evidence.tar.gz'
with tarfile.open(packet,'x:gz',compresslevel=1)as tar:tar.add(out,arcname=out.name)
packet.with_suffix('.json').write_text(json.dumps({'bytes':packet.stat().st_size,'sha256':sha(packet)},indent=2))
