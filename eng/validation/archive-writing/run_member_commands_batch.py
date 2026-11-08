"""Guest-only focused gate for connected member/F5/F6 changes. Preserves compiler-bound evidence on failure too."""
from pathlib import Path
import hashlib, importlib.util, json, os, shutil, subprocess, sys, tarfile, xml.etree.ElementTree as ET

FOLDER = 'awm-v3'
PACKET = 'archive-member-commands-delta-v3.tar.gz'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def module(task):
    path = task / 'archive-workflow-source-v1/eng/validation/archive-writing/run_native_job_batch.py'
    spec = importlib.util.spec_from_file_location('member_commands_base', path)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value)
    value.BATCH_FOLDER = FOLDER
    value.PREVIOUS_FOLDER = 'awm-v2'
    value.PACKET_NAME = PACKET
    return value

def batch_for(task): return module(task).batch_for(task)

def prepare(task, expected):
    if shutil.disk_usage(task).free < 2 << 30: raise RuntimeError('Guest validation needs 2 GiB free.')
    module(task).prepare(task, expected)

def build(task):
    batch = batch_for(task); batch.build()
    if sys.platform != 'win32':
        batch.execute('FileCat.App.Tests-build', [batch.dotnet, 'build', str(batch.root/'tests/FileCat.App.Tests/FileCat.App.Tests.csproj'),
            '-c', 'Release', '--disable-build-servers', '-p:NuGetAudit=false', '-m:1', '-v', 'minimal'])

def tests(task):
    batch = batch_for(task); batch.idle()
    if sys.platform == 'win32':
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(batch.out/'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(batch.out/'app'),
            FILECAT_ARCHIVE_MEMBER_UI_EXPORT_ROOT=str(batch.out/'member-ui-vectors'))
    batch.execute('member-core', batch.test_args('FileCat.Core.Tests', '|'.join('FullyQualifiedName~'+name for name in (
        'Member_command', 'Skip_and_replace_reduce', 'Type_conflicts_skip', 'An_all_skipped_selection', 'Borrowed_views_retain',
        'Subset_capture_refuses', 'Listing_revision_is_checked', 'Assessment_reports_password_required', 'ArchiveTargetReviewTests',
        'ArchiveWorkflowReview', 'Shared_workflow', 'Recursive_workflow', 'ArchiveProviderWritingTests')), 'member-core'))
    batch.execute('member-ui', batch.test_args('FileCat.App.Tests',
        'FullyQualifiedName~ArchiveMemberCommandUiTests|FullyQualifiedName~ArchiveTargetUpdateUiTests|FullyQualifiedName~ArchiveCreationWorkflowTests&FullyQualifiedName!~Actual_confined_', 'member-ui'))
    if sys.platform == 'win32':
        batch.execute('member-native-ui', batch.test_args('FileCat.App.Tests', 'FullyQualifiedName~ArchiveMemberNativeUiTests', 'member-native-ui'))
    rows = {}
    for name in (('member-core', 'member-ui', 'member-native-ui') if sys.platform == 'win32' else ('member-core', 'member-ui')):
        tree = ET.parse(batch.out/'results'/(name+'.trx'))
        counters = tree.find('.//t:Counters', NS).attrib
        assert counters['failed'] == '0' and int(counters['passed']) > 0, counters
        rows[name] = counters
    (batch.out/'focused-complete.json').write_text(json.dumps(rows, indent=2))

def seal(task):
    batch = batch_for(task); batch.idle(); out, root = batch.out, batch.root
    assert not (out/'files.json').exists()
    binaries = out/'binaries'; binaries.mkdir()
    for project in ('tests/FileCat.Core.Tests', 'tests/FileCat.WorkerProbe', 'src/FileCat.ArchiveWorker', 'src/FileCat.Archives.Writing', 'tests/FileCat.App.Tests'):
        for path in (root/project/'bin/Release/net10.0').iterdir():
            if path.suffix not in ('.dll', '.pdb') or not (path.name.startswith('FileCat.') or path.name in ('FileCat.dll', 'FileCat.pdb')): continue
            destination = binaries/path.name
            if destination.exists(): assert sha(destination) == sha(path), str(path)
            else: shutil.copyfile(path, destination)
    if sys.platform == 'win32':
        for name in ('app', 'probe'):
            for path in (out/name).iterdir():
                if path.suffix in ('.dll', '.pdb') and (binaries/path.name).exists(): assert sha(path) == sha(binaries/path.name), str(path)
    extra = 'C:/Users/Admin/.nuget/packages' if sys.platform == 'win32' else str(Path.home()/'.nuget/packages')
    batch.execute('source-audit', [batch.dotnet, str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),
        str(root), str(binaries), str(out/'compiler-sources'), extra])
    audit = json.loads((out/'compiler-sources/facts.json').read_text())
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in json.loads((out/'delta.json').read_text())['files']:
        if not row['path'].endswith('.cs'): continue
        found = [source for source in audit['sources'] if source['document'].replace('\\','/').endswith('/'+row['path'])]
        assert found and all(source['sha256'].lower() == row['sha256'].lower() for source in found), row['path']
    batch.idle()
    census = {'owned_filecat_or_test_processes': 0, 'production_backend_qualified': False}
    if sys.platform == 'darwin': census['preserved_wake_helper'] = subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='], text=True).strip(); assert census['preserved_wake_helper']
    (out/'final-census.json').write_text(json.dumps(census, indent=2))
    shutil.copyfile(Path(__file__), out/'executed-member-commands-batch.py')
    rows = [{'path':path.relative_to(out).as_posix(),'bytes':path.stat().st_size,'sha256':sha(path)} for path in sorted(out.rglob('*')) if path.is_file()]
    (out/'files.json').write_text(json.dumps({'files': rows}, indent=2))
    packet = task/(FOLDER+'-evidence.tar.gz')
    with tarfile.open(packet, 'x:gz', compresslevel=1) as tar: tar.add(out, arcname=out.name)
    packet.with_suffix('.json').write_text(json.dumps({'bytes':packet.stat().st_size,'sha256':sha(packet)}, indent=2))

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'build': build(task)
    elif phase == 'tests': tests(task)
    elif phase == 'seal': seal(task)
    elif phase == 'run':
        prepare(task, rest[0]); build(task)
        try: tests(task)
        finally: seal(task)
    else: raise SystemExit('Unknown phase')
