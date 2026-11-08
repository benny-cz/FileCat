"""Windows-only addendum over sealed edit bytes; no rebuild or second full Core run."""
from pathlib import Path
import hashlib, json, runpy, shutil, subprocess, sys, tarfile, xml.etree.ElementTree as ET

FOLDER = 'awe-v3-native-v1'
PARENT = 'awe-v3'
NS = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()
def batch_for(task):
    batch = runpy.run_path(str(task/'archive-workflow-source-v1/eng/validation/archive-writing/run_job_backend_batch.py'))['batch_for'](task)
    batch.out = task/FOLDER
    batch.env.update(FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(batch.out/'child-diagnostics'),
        FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
        FILECAT_ARCHIVE_APP_ASSETS=str(task/PARENT/'app'), FILECAT_ARCHIVE_PROBE_ASSETS=str(task/PARENT/'probe'),
        FILECAT_ARCHIVE_CASE_ROOT='C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture')
    return batch
def prepare(task, expected):
    assert sys.platform == 'win32'
    batch = batch_for(task); batch.idle(); parent = task/PARENT
    assert sha(parent/'files.json') == expected and not batch.out.exists()
    proof = {row['path']:row for row in json.loads((parent/'files.json').read_text())['files']}
    checked = []
    for name,row in proof.items():
        if name not in ('source-files.json','delta.json','tests-complete.json','final-census.json') and not name.startswith(('app/','probe/','binaries/')): continue
        path = parent/name; assert path.stat().st_size == row['bytes'] and sha(path) == row['sha256'], name
        checked.append(row)
    assert json.loads((parent/'final-census.json').read_text())['owned_filecat_or_test_processes'] == 0
    gates = json.loads((parent/'tests-complete.json').read_text()); assert gates['native-edit-ui']['passed'] == '10'
    assert gates['core-regressions']['failed'] == '0'
    for row in json.loads((parent/'source-files.json').read_text())['files']:
        assert sha(batch.root/row['path']) == row['sha256'], row['path']
    batch.out.mkdir()
    for name in ('source-files.json','delta.json'): shutil.copyfile(parent/name,batch.out/name)
    (batch.out/'reused-parent.json').write_text(json.dumps({'folder':PARENT,'manifest_sha256':expected,'checked':checked},indent=2))
    shutil.copyfile(Path(__file__),batch.out/'executed-native-regressions.py')
def tests(task):
    batch = batch_for(task); batch.idle(); rows = {}
    for name,assembly,selected,expected in (
        ('native','FileCat.Platform.Windows.Tests','FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent',217),
        ('cleanup-crashes','FileCat.Platform.Windows.Tests','FullyQualifiedName~Real_creation_cleanup_process_crashes_',18),
        ('app-entry','FileCat.App.Tests','FullyQualifiedName~ArchiveWorkerEntryTests|FullyQualifiedName~ArchiveRetainedCleanupNativeUiTests',14),
        ('member-native-ui','FileCat.App.Tests','FullyQualifiedName~ArchiveMemberNativeUiTests',10)):
        batch.execute(name,batch.test_args(assembly,selected,name))
        counters = ET.parse(batch.out/'results'/(name+'.trx')).find('.//t:Counters',NS).attrib
        assert counters['passed'] == str(expected) and counters['total'] == str(expected) and counters['failed'] == '0',counters
        rows[name] = counters; (batch.out/'passed-gates.json').write_text(json.dumps(rows,indent=2))
    batch.idle(); (batch.out/'tests-complete.json').write_text(json.dumps(rows,indent=2))
def seal(task):
    batch = batch_for(task); batch.idle(); out = batch.out; binaries = out/'binaries'; binaries.mkdir()
    for project in ('tests/FileCat.Core.Tests','tests/FileCat.App.Tests','tests/FileCat.Platform.Windows.Tests','tests/FileCat.WorkerProbe'):
        for path in (batch.root/project/'bin/Release/net10.0').iterdir():
            if path.suffix not in ('.dll','.pdb') or not (path.name.startswith('FileCat.') or path.name in ('FileCat.dll','FileCat.pdb')): continue
            destination = binaries/path.name
            if destination.exists(): assert sha(destination) == sha(path),str(path)
            else: shutil.copyfile(path,destination)
    batch.execute('source-audit',[batch.dotnet,str(batch.root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),
        str(batch.root),str(binaries),str(out/'compiler-sources'),'C:/Users/Admin/.nuget/packages'])
    audit = json.loads((out/'compiler-sources/facts.json').read_text())
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    ledger = {row['path']:row for row in json.loads((out/'source-files.json').read_text())['files']}
    for row in json.loads((out/'delta.json').read_text())['files']:
        if not row['path'].endswith('.cs'): continue
        matches = [item for item in audit['sources'] if item['document'].replace('\\','/').endswith('/'+row['path'])]
        assert matches and all(item['sha256'].lower() == ledger[row['path']]['sha256'] for item in matches),row['path']
    batch.idle(); (out/'final-census.json').write_text(json.dumps({'owned_filecat_or_test_processes':0,'production_backend_qualified':False}))
    rows = [{'path':path.relative_to(out).as_posix(),'bytes':path.stat().st_size,'sha256':sha(path)} for path in sorted(out.rglob('*')) if path.is_file()]
    (out/'files.json').write_text(json.dumps({'files':rows},indent=2))
    packet = task/(FOLDER+'-evidence.tar.gz')
    with tarfile.open(packet,'x:gz',compresslevel=1) as tar: tar.add(out,arcname=out.name)
    packet.with_suffix('.json').write_text(json.dumps({'bytes':packet.stat().st_size,'sha256':sha(packet)},indent=2))
if __name__ == '__main__':
    phase,task,*rest = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task,rest[0])
    elif phase == 'build': pass
    elif phase == 'tests': tests(task)
    elif phase == 'seal': seal(task)
    else: raise SystemExit('Unknown phase')
