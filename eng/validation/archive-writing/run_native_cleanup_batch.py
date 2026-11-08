"""Guest-only creation/update cleanup, recovery UI, complete Core and inherited independent-reader batch."""
from pathlib import Path
from collections import Counter
import hashlib, importlib.util, json, os, plistlib, shutil, subprocess, sys, xml.etree.ElementTree as ET

SOURCE_FOLDER='archive-workflow-source-v1'
BATCH_FOLDER='archive-native-cleanup-v7'

def native(task):
    spec=importlib.util.spec_from_file_location('native_cleanup_base',task/SOURCE_FOLDER/'eng/validation/archive-writing/run_native_job_batch.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.BATCH_FOLDER=BATCH_FOLDER
    module.PREVIOUS_FOLDER='archive-native-cleanup-v3' if sys.platform=='win32' else 'archive-native-cleanup-v4'
    module.PACKET_NAME='archive-native-cleanup-'+('windows' if sys.platform=='win32' else 'mac' if sys.platform=='darwin' else 'ubuntu')+'-delta-v7.tar.gz'
    module.tests=tests
    return module

def batch_for(task):return native(task).batch_for(task)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def prepare(task,expected):
    native(task).prepare(task,expected)
    (batch_for(task).out/'executed-native-cleanup-batch.py').write_bytes(Path(__file__).read_bytes())
    previous=task/('archive-native-moves-v4/results/archive-native-moves-core.trx' if sys.platform=='win32' else
        'archive-native-cleanup-v4/results/archive-native-cleanup-core.trx')
    cases(previous)
    (batch_for(task).out/'previous-full-core.trx').write_bytes(previous.read_bytes())
    if sys.platform!='win32':
        batch=batch_for(task);prior=task/'archive-native-cleanup-v4'
        delta=json.loads((batch.out/'delta.json').read_text())['files']
        assert {row['path'] for row in delta}=={'tests/FileCat.Core.Tests/ArchiveCreationCleanupTests.cs',
            'eng/validation/archive-writing/run_native_cleanup_batch.py',
            'eng/validation/archive-writing/sevenzip-feasibility/finish_pipeline_batch.py',
            'src/FileCat.Platform.Windows/Archives/WindowsArchiveCreationCleanupRecovery.cs',
            'src/FileCat.Platform.Windows/Archives/WindowsArchiveCleanupRecovery.cs',
            'src/FileCat.Platform.Windows/Archives/WindowsArchiveRestorationLease.cs',
            'src/FileCat.Platform.Windows/Archives/WindowsArchiveRecoveryIdentity.cs',
            'tests/FileCat.Platform.Windows.Tests/WindowsArchiveRecoveryIdentityTests.cs'}
        # Only two synthetic Core journal reopeners changed. Native Windows changes are exercised on Windows;
        # the Core/worker dependency closure must stay identical before reusing its complete Unix run.
        pins=[]
        for project in ('tests/FileCat.Core.Tests','tests/FileCat.WorkerProbe','src/FileCat.ArchiveWorker','src/FileCat.Archives.Writing'):
            for path in (batch.root/project/'bin/Release/net10.0').iterdir():
                if path.suffix in ('.dll','.pdb') and path.name.startswith('FileCat') and path.stem!='FileCat.Core.Tests':
                    pins.append({'path':str(path.relative_to(batch.root)),'sha256':sha(path)})
        retained=[]
        for row in json.loads((prior/'files.json').read_text())['files']:
            name=row['path']
            if not name.startswith(('catalog/','workers/','native-vectors/','workflows/','jobs/')) and name!='results/archive-native-cleanup-core.trx':continue
            path=prior/name;assert path.stat().st_size==row['bytes'] and sha(path)==row['sha256'],name
            target=batch.out/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,target);retained.append(row)
        assert retained and pins
        (batch.out/'reused-core-evidence.json').write_text(json.dumps({'previous_manifest_sha256':sha(prior/'files.json'),
            'full_Core_reused_not_rerun':True,'changed_test_scope':'two synthetic creation-cleanup journal reopeners; all 26 cases rerun',
            'unchanged_binaries':pins,'files':retained},indent=2))

def build(task):
    batch=batch_for(task)
    fixture_only=all(row['path'].startswith(('tests/','eng/validation/')) for row in json.loads((batch.out/'delta.json').read_text())['files'])
    if fixture_only:native(task).build(batch)
    else:batch.build()
    if sys.platform!='win32' or fixture_only:
        batch.execute('FileCat.App.Tests-build',[batch.dotnet,'build',str(batch.root/'tests/FileCat.App.Tests/FileCat.App.Tests.csproj'),
            '-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])
    reuse=batch.out/'reused-core-evidence.json'
    if reuse.exists():
        for row in json.loads(reuse.read_text())['unchanged_binaries']:assert sha(batch.root/row['path'])==row['sha256'],row['path']
        (batch.out/'unchanged-binaries-verified.txt').write_text('PASS\n')

def cases(path):
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};tree=ET.parse(path)
    counts=tree.find('.//t:Counters',ns).attrib
    assert counts['failed']=='0' and int(counts['executed'])>0,(path,counts)
    return tree.findall('.//t:UnitTestResult',ns)

def tests(batch):
    batch.idle();out=batch.out
    batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out/'catalog'),FILECAT_7Z_WORKER_EXPORT_ROOT=str(out/'workers'),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out/'native-vectors'),FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out/'workflows'),
        FILECAT_ARCHIVE_JOB_EXPORT_ROOT=str(out/'jobs'),FILECAT_ARCHIVE_MOVE_EXPORT_ROOT=str(out/'moves'),
        FILECAT_ARCHIVE_SOURCE_CASE_EXPORT_ROOT=str(out/'source-case'),FILECAT_ARCHIVE_CLEANUP_EXPORT_ROOT=str(out/'cleanup'))
    if sys.platform=='win32':
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1',FILECAT_ARCHIVE_TXF_NATIVE='1',FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(out/'probe'),FILECAT_ARCHIVE_APP_ASSETS=str(out/'app'),
            FILECAT_ARCHIVE_CASE_ROOT='C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture')
    elif sys.platform=='darwin':
        batch.env['FILECAT_ARCHIVE_CASE_ROOT']=os.environ['FILECAT_ARCHIVE_CASE_ROOT']
        assert Path(batch.env['FILECAT_ARCHIVE_CASE_ROOT']).is_dir()
    batch.execute('cleanup-preflight',batch.test_args('FileCat.Core.Tests','FullyQualifiedName~ArchiveCreationCleanupTests','cleanup-preflight'))
    rows=cases(out/'results/cleanup-preflight.trx');assert len(rows)==26 and all(row.get('outcome')=='Passed' for row in rows)
    selected='FullyQualifiedName~ArchiveCreationCleanupUiTests|FullyQualifiedName~ArchiveRetainedCleanupUiTests'
    batch.execute('cleanup-ui',batch.test_args('FileCat.App.Tests',selected,'cleanup-ui'))
    rows=cases(out/'results/cleanup-ui.trx');assert sum('Created_archives_offer_' in row.get('testName','') for row in rows)==10
    assert sum('RAR_and_7z_retained_' in row.get('testName','') for row in rows)==4
    if sys.platform=='win32':
        batch.execute('cleanup-identity',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~WindowsArchiveRecoveryIdentityTests','cleanup-identity'))
        rows=cases(out/'results/cleanup-identity.trx');assert len(rows)==13 and all(row.get('outcome')=='Passed' for row in rows)
        batch.execute('cleanup-native-preflight',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~Actual_native_jobs_automatically_clean_','cleanup-native-preflight'))
        rows=cases(out/'results/cleanup-native-preflight.trx');assert len(rows)==10 and all(row.get('outcome')=='Passed' for row in rows)
        batch.execute('cleanup-native-crashes',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~Real_creation_cleanup_process_crashes_','cleanup-native-crashes'))
        rows=cases(out/'results/cleanup-native-crashes.trx');assert len(rows)==18 and all(row.get('outcome')=='Passed' for row in rows)
    if not (out/'reused-core-evidence.json').exists():
        batch.execute('tests',batch.test_args('FileCat.Core.Tests','FullyQualifiedName~FileCat.Core.Tests','archive-native-cleanup-core'))
    else:assert (out/'unchanged-binaries-verified.txt').read_text()=='PASS\n'
    current=cases(out/'results/archive-native-cleanup-core.trx');previous=cases(out/'previous-full-core.trx')
    names=Counter(row.get('testName') for row in current);old=Counter(row.get('testName') for row in previous)
    passed=Counter(row.get('testName') for row in current if row.get('outcome')=='Passed')
    old_passed=Counter(row.get('testName') for row in previous if row.get('outcome')=='Passed')
    assert all(names[name]==count for name,count in old.items()) and all(passed[name]>=count for name,count in old_passed.items())
    (out/'full-case-inventory.json').write_text(json.dumps({'previous':len(previous),'current':len(current),'prior_cases_present':True,'prior_passes_preserved':True},indent=2))
    if sys.platform=='win32':
        selected='(FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent)&FullyQualifiedName!~Actual_native_jobs_automatically_clean_'
        batch.execute('native',batch.test_args('FileCat.Platform.Windows.Tests',selected,'native'))
        rows=cases(out/'results/native.trx');assert sum(row.get('outcome')=='Passed' for row in rows)==207
        batch.execute('app-entry',batch.test_args('FileCat.App.Tests','FullyQualifiedName~ArchiveWorkerEntryTests|FullyQualifiedName~ArchiveRetainedCleanupNativeUiTests','app-entry'))
    (out/'tests-complete.txt').write_text('PASS\n')

def run_tests(task):
    if sys.platform.startswith('linux'):
        subprocess.run(['dbus-run-session','--',sys.executable,str(Path(__file__).resolve()),'inside-private',str(task)],check=True)
    elif sys.platform=='darwin':
        batch=batch_for(task);batch.idle();image=task/'native-move-case-v1.asif';assert image.is_file()
        attach=subprocess.run(['diskutil','image','attach','--nobrowse','--plist',str(image)],capture_output=True,check=True)
        (batch.out/'case-image-attach.plist').write_bytes(attach.stdout)
        entities=plistlib.loads(attach.stdout)['system-entities'];physical=next(row['dev-entry'] for row in entities if row.get('content-hint')=='GUID_partition_scheme')
        if not physical.startswith('/dev/'):physical='/dev/'+physical
        mounted=next(Path(row['mount-point']) for row in entities if 'mount-point' in row)
        try:
            info=subprocess.run(['diskutil','info','-plist',str(mounted)],capture_output=True,check=True)
            (batch.out/'case-volume.plist').write_bytes(info.stdout)
            assert 'case-sensitive' in plistlib.loads(info.stdout)['FilesystemName'].lower()
            target=mounted/(BATCH_FOLDER+'-cases');target.mkdir()
            (batch.out/'case-fixture.json').write_text(json.dumps({'image':str(image),'mount':str(mounted),'source':str(target),'device':physical},indent=2))
            os.environ['FILECAT_ARCHIVE_CASE_ROOT']=str(target);tests(batch)
        finally:
            result=subprocess.run(['hdiutil','detach',physical],capture_output=True,check=True)
            (batch.out/'case-image-detach.stdout').write_bytes(result.stdout)
    else:tests(batch_for(task))

def oracles(task):
    batch=batch_for(task);native(task).oracles(batch)
    if sys.platform=='win32':
        tool=task/'password-oracle-tools-v1/7zip/7z.exe';modules=task/'password-oracle-tools-v1/modules'
        helpers=batch.root/'eng/validation/archive-writing/sevenzip-feasibility'
        batch.execute('moves-sevenzip',[sys.executable,'-B',str(helpers/'verify_pipeline.py'),str(batch.out/'moves'),str(tool),str(modules),str(tool)])
        facts=json.loads((batch.out/'moves/independent-pipeline.json').read_text());assert (facts['archives'],facts['metadata'],facts['payloads'])==(6,33,21)
        batch.execute('moves-rar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),str(batch.out/'moves'),str(tool),str(modules),'0','0','--moves'])
        batch.execute('moves-tar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/verify-workflow-tar.py'),str(batch.out/'moves'),str(tool),str(modules),str(helpers/'verify_pipeline.py'),'--moves'])
        batch.execute('cleanup-sevenzip',[sys.executable,'-B',str(helpers/'verify_pipeline.py'),str(batch.out/'cleanup'),str(tool),str(modules),str(tool)])
        facts=json.loads((batch.out/'cleanup/independent-pipeline.json').read_text());assert (facts['archives'],facts['metadata'],facts['payloads'])==(6,9,9)
        batch.execute('cleanup-rar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),str(batch.out/'cleanup'),str(tool),str(modules),'0','0','--cleanup'])
        batch.execute('cleanup-tar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/verify-workflow-tar.py'),str(batch.out/'cleanup'),str(tool),str(modules),str(helpers/'verify_pipeline.py'),'--cleanup'])
    (batch.out/'cleanup-oracles-complete.txt').write_text('PASS\n')

def finish(task):
    batch=batch_for(task);assert (batch.out/'cleanup-oracles-complete.txt').read_text()=='PASS\n'
    native(task).finish(batch)

if __name__=='__main__':
    phase,task,*rest=sys.argv[1:];task=Path(task).resolve()
    if phase=='prepare':prepare(task,rest[0])
    elif phase=='build':build(task)
    elif phase=='tests':run_tests(task)
    elif phase=='inside-private':native(task).private_tests(task)
    elif phase=='oracle':oracles(task)
    elif phase=='finish':finish(task)
    elif phase=='run':prepare(task,rest[0]);build(task);run_tests(task);oracles(task);finish(task)
    else:raise SystemExit('Unknown phase')
