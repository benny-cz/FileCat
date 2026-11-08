"""Guest-only early storage/record gates. Full integration/release qualification remains separate."""
from pathlib import Path
import importlib.util,json,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as ET

FOLDER='awe-storage-v9'
PACKET='archive-edit-storage-delta-v9.tar.gz'
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def sha(path):
    import hashlib
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def base(task):
    path=task/'archive-workflow-source-v1/eng/validation/archive-writing/run_job_backend_batch.py'
    spec=importlib.util.spec_from_file_location('edit_storage_base',path);value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value)
    value.BATCH_FOLDER=FOLDER;value.PACKET_NAME=PACKET;value.PREVIOUS_FOLDER='awe-storage-v8'
    return value
def batch_for(task):return base(task).batch_for(task)
def prepare(task,expected):
    value=base(task);previous=task/value.PREVIOUS_FOLDER
    proof=json.loads((previous/'files.json').read_text())['files'];row=next(row for row in proof if row['path']=='source-files.json')
    assert sha(previous/'source-files.json')==row['sha256'];value.prepare(task,expected)
def build(task):
    batch=batch_for(task);projects=['tests/FileCat.Core.Tests','tests/FileCat.App.Tests','eng/validation/archive-writing/PdbSourceAudit']
    if sys.platform=='win32':projects.insert(2,'tests/FileCat.Platform.Windows.Tests')
    for project in projects:
        batch.execute(Path(project).name+'-build',[batch.dotnet,'build',str(batch.root/project/(Path(project).name+'.csproj')),
            '-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])
def tests(task):
    batch=batch_for(task);batch.idle()
    fixture=Path.home()/'AppData/Local/Temp'/(FOLDER+'-native-fixtures') if sys.platform=='win32' else task/FOLDER/'native-fixtures'
    fixture.mkdir();batch.env['FILECAT_EDIT_NATIVE_ROOT']=str(fixture)
    (batch.out/'native-fixture-root.json').write_text(json.dumps({'path':str(fixture),'account_home':str(Path.home())},indent=2))
    rows={}
    def run(name,assembly,selected,expected=None):
        batch.execute(name,batch.test_args(assembly,selected,name));counters=ET.parse(batch.out/'results'/(name+'.trx')).find('.//t:Counters',NS).attrib
        assert counters['failed']=='0' and int(counters['passed'])>0,counters
        if expected is not None:assert counters['total']==str(expected) and counters['passed']==str(expected),counters
        rows[name]=counters;(batch.out/'passed-gates.json').write_text(json.dumps(rows,indent=2))
    run('record-recovery','FileCat.Core.Tests','FullyQualifiedName~EditSessionRecoveryRecordTests',19)
    run('native-directory','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditDirectoryLeaseTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditDirectoryLeaseTests',20)
    run('native-storage','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditStorageTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditStorageTests',14 if sys.platform=='win32' else 19)
    run('native-snapshot','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditSnapshotTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditSnapshotTests',34)
    run('commit-copy','FileCat.Core.Tests','FullyQualifiedName~EditCommitCopyTests',10)
    run('edit-core','FileCat.Core.Tests','FullyQualifiedName~EditSession&FullyQualifiedName!~EditSessionRecoveryRecordTests|FullyQualifiedName~ArchivePasswordReadSessionTests')
    run('job-input-regressions','FileCat.Core.Tests','FullyQualifiedName~ArchiveJobBackendTests|FullyQualifiedName~ArchiveCapturedInputsTests')
    run('edit-ui','FileCat.App.Tests','FullyQualifiedName~EditSession|FullyQualifiedName~EditCommit')
    batch.idle();(batch.out/'tests-complete.json').write_text(json.dumps(rows,indent=2))
def seal(task):
    batch=batch_for(task);batch.idle();out=batch.out;binaries=out/'binaries';binaries.mkdir()
    projects=['tests/FileCat.Core.Tests','tests/FileCat.App.Tests']
    if sys.platform=='win32':projects.append('tests/FileCat.Platform.Windows.Tests')
    for project in projects:
        for path in (batch.root/project/'bin/Release/net10.0').iterdir():
            if path.suffix not in ('.dll','.pdb') or not(path.name.startswith('FileCat.') or path.name in ('FileCat.dll','FileCat.pdb')):continue
            destination=binaries/path.name
            if destination.exists():assert sha(destination)==sha(path),str(path)
            else:shutil.copyfile(path,destination)
    batch.execute('source-audit',[batch.dotnet,str(batch.root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),
        str(batch.root),str(binaries),str(out/'compiler-sources'),'C:/Users/Admin/.nuget/packages' if sys.platform=='win32' else str(Path.home()/'.nuget/packages')])
    audit=json.loads((out/'compiler-sources/facts.json').read_text());assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    uncompiled=[]
    for row in json.loads((out/'delta.json').read_text())['files']:
        if not row['path'].endswith('.cs'):continue
        if sys.platform!='win32' and row['path'].startswith('tests/FileCat.Platform.Windows.Tests/'):
            uncompiled.append({'path':row['path'],'sha256':row['sha256'],'reason':'Windows-only test project; compiled in the Windows lane.'});continue
        found=[item for item in audit['sources'] if item['document'].replace('\\','/').endswith('/'+row['path'])]
        assert found and all(item['sha256'].lower()==row['sha256'] for item in found),row['path']
    (out/'uncompiled-platform-sources.json').write_text(json.dumps(uncompiled,indent=2))
    census={'owned_filecat_or_test_processes':0,'production_backend_qualified':False,'storage_integrated':False}
    if sys.platform=='darwin':
        census['preserved_wake_helper']=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True).strip();assert census['preserved_wake_helper']
    batch.idle();(out/'final-census.json').write_text(json.dumps(census,indent=2));shutil.copyfile(Path(__file__),out/'executed-storage-batch.py')
    rows=[dict(path=path.relative_to(out).as_posix(),bytes=path.stat().st_size,sha256=sha(path)) for path in sorted(out.rglob('*')) if path.is_file()]
    (out/'files.json').write_text(json.dumps({'files':rows},indent=2));packet=task/(FOLDER+'-evidence.tar.gz')
    with tarfile.open(packet,'x:gz',compresslevel=1) as tar:tar.add(out,arcname=out.name)
    packet.with_suffix('.json').write_text(json.dumps({'bytes':packet.stat().st_size,'sha256':sha(packet)},indent=2))
if __name__=='__main__':
    phase,task,*rest=sys.argv[1:];task=Path(task).resolve()
    if phase=='prepare':prepare(task,rest[0])
    elif phase=='build':build(task)
    elif phase=='tests':tests(task)
    elif phase=='seal':seal(task)
    elif phase=='run':
        prepare(task,rest[0]);build(task)
        try:tests(task)
        finally:seal(task)
    else:raise SystemExit('Unknown phase')
