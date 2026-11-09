"""Guest-only early storage/record gates. Full integration/release qualification remains separate."""
from pathlib import Path
from datetime import datetime,timezone
import importlib.util,json,shutil,subprocess,sys,tarfile,tempfile,time,uuid,xml.etree.ElementTree as ET

FOLDER='awe-storage-v30'
PACKET='archive-edit-storage-delta-v30.tar.gz'
VALIDATION_SCOPE='full'
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def sha(path):
    import hashlib
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def base(task):
    path=task/'archive-workflow-source-v1/eng/validation/archive-writing/run_job_backend_batch.py'
    spec=importlib.util.spec_from_file_location('edit_storage_base',path);value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value)
    value.BATCH_FOLDER=FOLDER;value.PACKET_NAME=PACKET;value.PREVIOUS_FOLDER='awe-storage-v29'
    return value
def batch_for(task):
    batch=base(task).batch_for(task);execute=batch.execute
    def measured(name,args,env=None):
        # Diagnostic timings only; these never replace raw results or grant test completion.
        operation=uuid.uuid4().hex;started=time.monotonic()
        def record(**fields):
            with (batch.out/'execution-timings.jsonl').open('a',encoding='utf-8') as output:
                output.write(json.dumps(dict(operation=operation,name=name,utc=datetime.now(timezone.utc).isoformat(),**fields))+'\n')
        record(event='started',command=[str(arg) for arg in args],cwd=str(batch.root))
        try:execute(name,args,env=env)
        except BaseException as error:
            record(event='failed',elapsed_seconds=time.monotonic()-started,error_type=type(error).__name__,
                returncode=error.returncode if isinstance(error,subprocess.CalledProcessError) else None)
            raise
        else:record(event='returned',elapsed_seconds=time.monotonic()-started,returncode=0)
    batch.execute=measured
    return batch
def prepare(task,expected):
    value=base(task);previous=task/value.PREVIOUS_FOLDER
    proof=json.loads((previous/'files.json').read_text())['files'];row=next(row for row in proof if row['path']=='source-files.json')
    assert sha(previous/'source-files.json')==row['sha256'];value.prepare(task,expected)
def build(task):
    batch=batch_for(task);projects=['tests/FileCat.Core.Tests','tests/FileCat.App.Tests','tests/FileCat.Remote.Tests','eng/validation/archive-writing/PdbSourceAudit']
    require_space(batch,'build')
    if sys.platform=='win32':projects.insert(2,'tests/FileCat.Platform.Windows.Tests')
    for project in projects:
        batch.execute(Path(project).name+'-build',[batch.dotnet,'build',str(batch.root/project/(Path(project).name+'.csproj')),
            '-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])

def require_space(batch,phase,minimum=8*1024**3):
    # Keep room for ordinary regression fixtures and diagnostic preservation. This does not replace
    # product quota/no-space coverage or promise that the filesystem cannot fill during a suite.
    paths={str(batch.root),str(batch.out),str(batch.env.get('TEMP') or batch.env.get('TMPDIR') or tempfile.gettempdir())}
    if batch.env.get('FILECAT_EDIT_NATIVE_ROOT'):paths.add(batch.env['FILECAT_EDIT_NATIVE_ROOT'])
    rows=[dict(path=path,free=shutil.disk_usage(path).free,minimum_free=minimum) for path in sorted(paths)]
    with (batch.out/'resource-preflight.jsonl').open('a',encoding='utf-8') as output:
        output.write(json.dumps(dict(phase=phase,utc=datetime.now(timezone.utc).isoformat(),filesystems=rows))+'\n')
    if any(row['free']<minimum for row in rows):
        raise RuntimeError('Validation filesystem has less than the 8 GiB working reserve. Preserve evidence and reclaim only owned artifacts before retrying.')

def test_environment(task):
    # Reuse the qualified full-inventory fixture: native case-sensitive Mac volume and private Linux bus/keyring.
    path=task/'archive-workflow-source-v1/eng/validation/archive-writing/run_password_browsing_batch.py'
    spec=importlib.util.spec_from_file_location('edit_storage_environment',path)
    value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value)
    value.FOLDER=FOLDER;value.tests=tests;value.__file__=str(Path(__file__).resolve())
    return value
def tests(task):
    batch=batch_for(task);batch.idle()
    assert VALIDATION_SCOPE in ('focused','full')
    (batch.out/'validation-scope.json').write_text(json.dumps(dict(scope=VALIDATION_SCOPE,
        full_regressions={name:'not-run' if VALIDATION_SCOPE=='focused' else 'required' for name in ('complete-core','complete-remote','complete-app')},
        expected_new_core_cases=309,production_backend_qualified=False),indent=2))
    fixture=Path.home()/'AppData/Local/Temp'/(FOLDER+'-native-fixtures') if sys.platform=='win32' else task/FOLDER/'native-fixtures'
    fixture.mkdir();batch.env['FILECAT_EDIT_NATIVE_ROOT']=str(fixture)
    require_space(batch,'focused')
    (batch.out/'native-fixture-root.json').write_text(json.dumps({'path':str(fixture),'account_home':str(Path.home())},indent=2))
    rows={}
    def run(name,assembly,selected,expected=None):
        batch.execute(name,batch.test_args(assembly,selected,name));counters=ET.parse(batch.out/'results'/(name+'.trx')).find('.//t:Counters',NS).attrib
        assert counters['failed']=='0' and int(counters['passed'])>0,counters
        if expected is not None:assert counters['total']==str(expected) and counters['passed']==str(expected),counters
        rows[name]=counters;(batch.out/'passed-gates.json').write_text(json.dumps(rows,indent=2))
    run('storage-admission','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditStorageAdmissionTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditStorageAdmissionTests',34)
    run('save-copy-intent','FileCat.Core.Tests','FullyQualifiedName~EditSaveCopyIntentTests',14)
    run('save-copy-session','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditSaveCopySessionTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditSaveCopySessionTests',25)
    run('cancellation-fixture','FileCat.Core.Tests','FullyQualifiedName~JobEngineTests.Cancel_leaves_no_partial_destination|FullyQualifiedName~JobEngineTests.Cancellation_fixture_reports_startup_error',2)
    run('comparison-retirement','FileCat.Core.Tests','FullyQualifiedName~ComparisonContentRetirementTests',108)
    run('save-copy-record','FileCat.Core.Tests','FullyQualifiedName~EditSaveCopyRecordTests',25)
    run('save-copy-publication','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditSaveCopyPublicationTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditSaveCopyPublicationTests',25 if sys.platform=='win32' else 26)
    run('save-copy-preparation','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditSaveCopyPreparationTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditSaveCopyPreparationTests',17)
    run('native-sftp','FileCat.App.Tests','FullyQualifiedName~SftpRetainedEditTests',23)
    run('verification-ui','FileCat.App.Tests','FullyQualifiedName~VerificationUiTests')
    run('history-retirement-core','FileCat.Core.Tests','FullyQualifiedName~JobRetirementOwnershipTests')
    run('history-retirement-ui','FileCat.App.Tests','FullyQualifiedName~OperationHistoryRetirementTests',18)
    if sys.platform=='linux':run('picture-boundary','FileCat.App.Tests','FullyQualifiedName~LinuxPicturePolicyTests')
    run('record-recovery','FileCat.Core.Tests','FullyQualifiedName~EditSessionRecoveryRecordTests',25)
    run('native-directory','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditDirectoryLeaseTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditDirectoryLeaseTests',21)
    run('native-storage','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditStorageTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditStorageTests',14 if sys.platform=='win32' else 19)
    run('native-snapshot','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditSnapshotTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditSnapshotTests',35 if sys.platform=='win32' else 34)
    run('capture-recovery','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditCaptureRecoveryTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditCaptureRecoveryTests',31)
    run('native-legacy-zip','FileCat.Platform.Windows.Tests' if sys.platform=='win32' else 'FileCat.Core.Tests',
        'FullyQualifiedName~WindowsEditLegacyZipTests' if sys.platform=='win32' else 'FullyQualifiedName~UnixEditLegacyZipTests',18)
    run('origin-policy','FileCat.Core.Tests','FullyQualifiedName~ArchiveCreationOriginTests')
    if sys.platform=='win32':
        run('native-origin-regressions','FileCat.Platform.Windows.Tests','FullyQualifiedName~WindowsArchiveOriginTests|FullyQualifiedName~WindowsArchiveOriginEvidenceTests')
    run('commit-copy','FileCat.Core.Tests','FullyQualifiedName~EditCommitCopyTests',10)
    run('edit-core','FileCat.Core.Tests','FullyQualifiedName~EditSession&FullyQualifiedName!~EditSessionRecoveryRecordTests|FullyQualifiedName~ArchivePasswordReadSessionTests')
    run('job-input-regressions','FileCat.Core.Tests','FullyQualifiedName~ArchiveJobBackendTests|FullyQualifiedName~ArchiveCapturedInputsTests')
    run('edit-ui','FileCat.App.Tests','FullyQualifiedName~EditSession|FullyQualifiedName~EditCommit')
    if VALIDATION_SCOPE=='full':run_full_regressions(batch,run)
    batch.idle();(batch.out/'tests-complete.json').write_text(json.dumps(rows,indent=2))

def run_full_regressions(batch,run):
    """Collect independent findings after an ordinary terminal test failure; never hide a failed gate."""
    failures=[];attempts=[]
    for name,assembly in [('complete-core','FileCat.Core.Tests'),('complete-remote','FileCat.Remote.Tests'),('complete-app','FileCat.App.Tests')]:
        # The previous subprocess returned. Refuse to overlap any surviving test/other tester.
        # An idle refusal is not a test failure and must stop this machine's sequence immediately.
        batch.idle()
        require_space(batch,name)
        attempt=dict(name=name,assembly=assembly,status='started');attempts.append(attempt)
        def record():
            (batch.out/'full-regression-attempts.json').write_text(json.dumps(attempts,indent=2))
        record()
        try:
            run(name,assembly,'FullyQualifiedName~'+assembly)
            if name=='complete-core':review_core_inventory(batch)
        except (subprocess.CalledProcessError,AssertionError) as error:
            attempt.update(status='failed',error_type=type(error).__name__,message=str(error)[:4096])
            if isinstance(error,subprocess.CalledProcessError):attempt['returncode']=error.returncode
            failures.append(name)
        except BaseException as error:
            attempt.update(status='interrupted',error_type=type(error).__name__,message=str(error)[:4096])
            raise
        else:attempt['status']='passed'
        finally:record()
    if failures:raise RuntimeError('Full regression gates failed: '+', '.join(failures))

def review_core_inventory(batch):
    previous=batch.task/'awe-v3';index=json.loads((previous/'files.json').read_text())['files']
    path='results/core-regressions.trx';expected=next(row for row in index if row['path']==path)
    source=previous/path;assert source.stat().st_size==expected['bytes'] and sha(source)==expected['sha256']
    shutil.copyfile(source,batch.out/'previous-full-core.trx')
    (batch.out/'baseline-core.json').write_text(json.dumps({'folder':'awe-v3','manifest_sha256':sha(previous/'files.json'),'trx_sha256':sha(source)},indent=2))
    old=ET.parse(source).findall('.//t:UnitTestResult',NS)
    current=ET.parse(batch.out/'results/complete-core.trx').findall('.//t:UnitTestResult',NS)
    spec=importlib.util.spec_from_file_location('edit_inventory',batch.root/'eng/validation/archive-writing/edit_core_inventory.py')
    helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
    inventory=helper.review(old,current,309)
    (batch.out/'full-case-inventory.json').write_text(json.dumps(inventory,indent=2))
    helper.require_complete(inventory)
def seal(task):
    batch=batch_for(task);batch.idle();out=batch.out;binaries=out/'binaries';binaries.mkdir()
    projects=['tests/FileCat.Core.Tests','tests/FileCat.App.Tests','tests/FileCat.Remote.Tests']
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
    elif phase=='tests':test_environment(task).run_tests(task)
    elif phase=='inside-private':test_environment(task).private_tests(task)
    elif phase=='seal':seal(task)
    elif phase=='run':
        prepare(task,rest[0]);build(task)
        try:test_environment(task).run_tests(task)
        finally:seal(task)
    else:raise SystemExit('Unknown phase')
