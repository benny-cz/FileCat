"""Guest-only coherent creation UI, password, native, complete Core and independent-reader validation."""
from pathlib import Path
from collections import Counter
import importlib.util,json,os,plistlib,shutil,subprocess,sys,xml.etree.ElementTree as ET

SOURCE_FOLDER='archive-workflow-source-v1'
BATCH_FOLDER='archive-creation-ui-v5'
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def native(task):
    spec=importlib.util.spec_from_file_location('creation_ui_base',task/SOURCE_FOLDER/'eng/validation/archive-writing/run_native_job_batch.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.BATCH_FOLDER=BATCH_FOLDER;module.PREVIOUS_FOLDER='archive-creation-ui-v4'
    module.PACKET_NAME='archive-creation-ui-delta-v5.tar.gz';module.tests=tests
    return module

def batch_for(task):return native(task).batch_for(task)
def prepare(task,expected):
    free=shutil.disk_usage(task).free
    if free < 2 << 30:
        raise RuntimeError(f'Guest validation needs 2 GiB free before applying the packet; available: {free} bytes.')
    native(task).prepare(task,expected);out=batch_for(task).out
    (out/'initial-free-space.json').write_text(json.dumps({'free_bytes':free,'minimum_bytes':2 << 30},indent=2))
    (out/'executed-creation-ui-batch.py').write_bytes(Path(__file__).read_bytes())
    (out/'previous-full-core.trx').write_bytes((task/'archive-native-cleanup-v7/results/archive-native-cleanup-core.trx').read_bytes())

def build(task):
    batch=batch_for(task);native(task).build(batch)
    if not (batch.out/'FileCat.App.Tests-build.log').exists():
        batch.execute('FileCat.App.Tests-build',[batch.dotnet,'build',str(batch.root/'tests/FileCat.App.Tests/FileCat.App.Tests.csproj'),
            '-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])

def cases(path):
    tree=ET.parse(path);counts=tree.find('.//t:Counters',NS).attrib
    assert counts['failed']=='0' and int(counts['executed'])>0,(path,counts)
    return tree.findall('.//t:UnitTestResult',NS)

def tests(batch):
    batch.idle();out=batch.out
    batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out/'catalog'),FILECAT_7Z_WORKER_EXPORT_ROOT=str(out/'workers'),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out/'native-vectors'),FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out/'workflows'),
        FILECAT_ARCHIVE_JOB_EXPORT_ROOT=str(out/'jobs'),FILECAT_ARCHIVE_MOVE_EXPORT_ROOT=str(out/'moves'),
        FILECAT_ARCHIVE_SOURCE_CASE_EXPORT_ROOT=str(out/'source-case'),FILECAT_ARCHIVE_CLEANUP_EXPORT_ROOT=str(out/'cleanup'),
        FILECAT_ARCHIVE_CREATION_UI_EXPORT_ROOT=str(out/'creation-ui'))
    if sys.platform=='win32':
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1',FILECAT_ARCHIVE_TXF_NATIVE='1',FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(out/'probe'),FILECAT_ARCHIVE_APP_ASSETS=str(out/'app'),
            FILECAT_ARCHIVE_CASE_ROOT='C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture')
    elif sys.platform=='darwin':batch.env['FILECAT_ARCHIVE_CASE_ROOT']=os.environ['FILECAT_ARCHIVE_CASE_ROOT']
    batch.execute('password-preflight',batch.test_args('FileCat.Core.Tests','FullyQualifiedName~ArchiveJobPasswordTests','password-preflight'))
    assert len(cases(out/'results/password-preflight.trx'))==21
    batch.execute('creation-smoke',batch.test_args('FileCat.App.Tests','FullyQualifiedName~Creation_keeps_the_captured_source_','creation-smoke'))
    assert len(cases(out/'results/creation-smoke.trx'))==2
    batch.execute('creation-ui',batch.test_args('FileCat.App.Tests','FullyQualifiedName~ArchiveCreationWorkflowTests&FullyQualifiedName!~Actual_confined_','creation-ui'))
    rows=cases(out/'results/creation-ui.trx');assert len(rows)==20 and all(row.get('outcome')=='Passed' for row in rows)
    if sys.platform=='win32':
        batch.execute('creation-native-ui',batch.test_args('FileCat.App.Tests','FullyQualifiedName~Actual_confined_application_workers_create_','creation-native-ui'))
        rows=cases(out/'results/creation-native-ui.trx');assert len(rows)==10 and all(row.get('outcome')=='Passed' for row in rows)
    selected='|'.join('FullyQualifiedName~'+name for name in ('ArchiveMoveUiTests','MutationPreparationTests','DialogFocusOwnershipTests',
        'ExitWithWaitingJobTests','OperationScopeTests','SortFocusContinuityTests','ArchiveCreationCleanupUiTests','ArchiveRetainedCleanupUiTests'))
    batch.execute('application-regressions',batch.test_args('FileCat.App.Tests',selected,'application-regressions'));cases(out/'results/application-regressions.trx')
    batch.execute('tests',batch.test_args('FileCat.Core.Tests','FullyQualifiedName~FileCat.Core.Tests','archive-creation-core'))
    current=cases(out/'results/archive-creation-core.trx');previous=cases(out/'previous-full-core.trx')
    names=Counter(row.get('testName') for row in current);old=Counter(row.get('testName') for row in previous)
    passed=Counter(row.get('testName') for row in current if row.get('outcome')=='Passed')
    old_passed=Counter(row.get('testName') for row in previous if row.get('outcome')=='Passed')
    assert all(names[name]==count for name,count in old.items()) and all(passed[name]>=count for name,count in old_passed.items())
    assert len(current)==len(previous)+13
    (out/'full-case-inventory.json').write_text(json.dumps({'previous':len(previous),'current':len(current),'prior_cases_present':True,'prior_passes_preserved':True},indent=2))
    if sys.platform=='win32':
        batch.execute('native',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent','native'))
        rows=cases(out/'results/native.trx');assert len(rows)==217 and all(row.get('outcome')=='Passed' for row in rows)
        batch.execute('creation-cleanup-crashes',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~Real_creation_cleanup_process_crashes_','creation-cleanup-crashes'))
        assert len(cases(out/'results/creation-cleanup-crashes.trx'))==18
        batch.execute('app-entry',batch.test_args('FileCat.App.Tests','FullyQualifiedName~ArchiveWorkerEntryTests|FullyQualifiedName~ArchiveRetainedCleanupNativeUiTests','app-entry'))
        assert len(cases(out/'results/app-entry.trx'))==14
    (out/'tests-complete.txt').write_text('PASS\n')

def run_tests(task):
    if sys.platform.startswith('linux'):
        subprocess.run(['dbus-run-session','--',sys.executable,str(Path(__file__).resolve()),'inside-private',str(task)],check=True)
    elif sys.platform=='darwin':
        batch=batch_for(task);batch.idle()
        attach=subprocess.run(['diskutil','image','attach','--nobrowse','--plist',str(task/'native-move-case-v1.asif')],capture_output=True,check=True)
        (batch.out/'case-image-attach.plist').write_bytes(attach.stdout)
        entities=plistlib.loads(attach.stdout)['system-entities'];physical=next(row['dev-entry'] for row in entities if row.get('content-hint')=='GUID_partition_scheme')
        if not physical.startswith('/dev/'):physical='/dev/'+physical
        mounted=next(Path(row['mount-point']) for row in entities if 'mount-point' in row)
        try:
            info=subprocess.run(['diskutil','info','-plist',str(mounted)],capture_output=True,check=True)
            (batch.out/'case-volume.plist').write_bytes(info.stdout);assert 'case-sensitive' in plistlib.loads(info.stdout)['FilesystemName'].lower()
            target=mounted/(BATCH_FOLDER+'-cases');target.mkdir();os.environ['FILECAT_ARCHIVE_CASE_ROOT']=str(target)
            (batch.out/'case-fixture.json').write_text(json.dumps({'image':str(task/'native-move-case-v1.asif'),'mount':str(mounted),'source':str(target),'device':physical},indent=2))
            tests(batch)
        finally:
            result=subprocess.run(['hdiutil','detach',physical],capture_output=True,check=True);(batch.out/'case-image-detach.stdout').write_bytes(result.stdout)
    else:tests(batch_for(task))

def oracles(task):
    batch=batch_for(task);native(task).oracles(batch)
    if sys.platform=='win32':
        tool=task/'password-oracle-tools-v1/7zip/7z.exe';modules=task/'password-oracle-tools-v1/modules'
        helpers=batch.root/'eng/validation/archive-writing/sevenzip-feasibility'
        for folder,scope in [('moves','--moves'),('cleanup','--cleanup'),('creation-ui','--creation-ui')]:
            batch.execute(folder+'-sevenzip',[sys.executable,'-B',str(helpers/'verify_pipeline.py'),str(batch.out/folder),str(tool),str(modules),str(tool)])
            batch.execute(folder+'-rar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),str(batch.out/folder),str(tool),str(modules),'0','0',scope])
            batch.execute(folder+'-tar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/verify-workflow-tar.py'),str(batch.out/folder),str(tool),str(modules),str(helpers/'verify_pipeline.py'),scope])
            facts=json.loads((batch.out/folder/'independent-pipeline.json').read_text())
            assert (facts['archives'],facts['metadata'],facts['payloads'])=={'moves':(6,33,21),'cleanup':(6,9,9),'creation-ui':(3,3,3)}[folder]
    (batch.out/'creation-oracles-complete.txt').write_text('PASS\n')

def finish(task):
    batch=batch_for(task);assert (batch.out/'creation-oracles-complete.txt').read_text()=='PASS\n';native(task).finish(batch)

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
