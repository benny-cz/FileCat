"""Guest-only password view/queued extraction gate; retain failed runs and exact compiler evidence."""
from pathlib import Path
import hashlib, importlib.util, json, os, shutil, subprocess, sys, tarfile, xml.etree.ElementTree as ET

FOLDER = 'awp-v1'
PACKET = 'archive-password-browsing-delta-v1.tar.gz'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()

def base(task):
    path = task/'archive-workflow-source-v1/eng/validation/archive-writing/run_job_backend_batch.py'
    spec = importlib.util.spec_from_file_location('password_browsing_base', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.BATCH_FOLDER, module.PACKET_NAME = FOLDER, PACKET
    module.PREVIOUS_FOLDER = 'awm-v5' if sys.platform == 'darwin' else 'awm-v4'
    return module

def batch_for(task): return base(task).batch_for(task)

def prepare(task, expected):
    if shutil.disk_usage(task).free < 2 << 30: raise RuntimeError('Guest needs 2 GiB free.')
    module = base(task); previous = task/module.PREVIOUS_FOLDER
    rows = json.loads((previous/'files.json').read_text())['files']
    ledger = next(row for row in rows if row['path'] == 'source-files.json')
    assert sha(previous/'source-files.json') == ledger['sha256']
    module.prepare(task, expected)

def build(task):
    batch = batch_for(task)
    for project in ('tests/FileCat.Core.Tests', 'tests/FileCat.App.Tests', 'eng/validation/archive-writing/PdbSourceAudit'):
        batch.execute(Path(project).name+'-build', [batch.dotnet, 'build', str(batch.root/project/(Path(project).name+'.csproj')),
            '-c', 'Release', '--disable-build-servers', '-p:NuGetAudit=false', '-m:1', '-v', 'minimal'])
    (batch.out/'build-complete.json').write_text(json.dumps({'built': True}))

def tests(task):
    batch = batch_for(task); batch.idle()
    rows = {}
    def run(name, assembly, selected, expected=None):
        batch.execute(name, batch.test_args(assembly, selected, name))
        counters = ET.parse(batch.out/'results'/(name+'.trx')).find('.//t:Counters', NS).attrib
        assert counters['failed'] == '0' and int(counters['passed']) > 0, counters
        if expected is not None: assert counters['passed'] == str(expected) and counters['total'] == str(expected), counters
        rows[name] = counters
        (batch.out/'passed-gates.json').write_text(json.dumps(rows, indent=2))
    run('password-read-core', 'FileCat.Core.Tests', 'FullyQualifiedName~ArchivePasswordReadSessionTests', 18)
    run('password-read-ui', 'FileCat.App.Tests', 'FullyQualifiedName~ArchivePasswordBrowsingUiTests|FullyQualifiedName~PreparedNavigationOwnershipTests', 11)
    # Stop at the focused gate on failure, before spending time on the unchanged regression inventory.
    run('navigation-member-regressions', 'FileCat.App.Tests', '|'.join('FullyQualifiedName~'+name for name in (
        'NavigationIntentTests', 'ClipboardIntentTests', 'ArchiveMemberCommandUiTests', 'ArchiveTargetUpdateUiTests',
        'ArchiveCreationWorkflowTests&FullyQualifiedName!~Actual_confined_')))
    run('core-regressions', 'FileCat.Core.Tests', 'FullyQualifiedName~FileCat.Core.Tests')
    batch.idle()
    (batch.out/'tests-complete.json').write_text(json.dumps(rows, indent=2))

def run_tests(task):
    if sys.platform != 'linux': return tests(task)
    batch = batch_for(task)
    batch.execute('private-tests', ['dbus-run-session', '--', sys.executable, '-B', str(Path(__file__).resolve()), 'inside-private', str(task)])

def private_tests(task):
    path = task/'archive-workflow-source-v1/eng/validation/archive-writing/run_native_job_batch.py'
    spec = importlib.util.spec_from_file_location('password_private_bus', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.BATCH_FOLDER = FOLDER
    module.tests = lambda batch: tests(batch.task)
    module.private_tests(task)

def seal(task):
    batch = batch_for(task); batch.idle(); out = batch.out
    assert not (out/'files.json').exists()
    subprocess.run([batch.dotnet, '--info'], env=batch.env, stdout=(out/'dotnet-info.txt').open('x'), check=True)
    binaries = out/'binaries'; binaries.mkdir()
    for project in ('tests/FileCat.Core.Tests', 'tests/FileCat.App.Tests'):
        for path in (batch.root/project/'bin/Release/net10.0').iterdir():
            if path.suffix not in ('.dll', '.pdb') or not (path.name.startswith('FileCat.') or path.name in ('FileCat.dll', 'FileCat.pdb')): continue
            target = binaries/path.name
            if target.exists(): assert sha(target) == sha(path), str(path)
            else: shutil.copyfile(path, target)
    batch.execute('source-audit', [batch.dotnet, str(batch.root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),
        str(batch.root), str(binaries), str(out/'compiler-sources'),
        'C:/Users/Admin/.nuget/packages' if sys.platform == 'win32' else str(Path.home()/'.nuget/packages')])
    audit = json.loads((out/'compiler-sources/facts.json').read_text())
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in json.loads((out/'delta.json').read_text())['files']:
        if not row['path'].endswith('.cs'): continue
        found = [item for item in audit['sources'] if item['document'].replace('\\', '/').endswith('/'+row['path'])]
        assert found and all(item['sha256'].lower() == row['sha256'].lower() for item in found), row['path']
    census = {'owned_filecat_or_test_processes': 0, 'production_backend_qualified': False}
    if sys.platform == 'darwin':
        census['preserved_wake_helper'] = subprocess.check_output(['/bin/ps', '-p', '36765', '-o', 'pid=,command='], text=True).strip()
        assert census['preserved_wake_helper']
    batch.idle(); (out/'final-census.json').write_text(json.dumps(census, indent=2))
    shutil.copyfile(Path(__file__), out/'executed-password-browsing-batch.py')
    rows = [{'path':path.relative_to(out).as_posix(), 'bytes':path.stat().st_size, 'sha256':sha(path)} for path in sorted(out.rglob('*')) if path.is_file()]
    (out/'files.json').write_text(json.dumps({'files': rows}, indent=2))
    packet = task/(FOLDER+'-evidence.tar.gz')
    with tarfile.open(packet, 'x:gz', compresslevel=1) as tar: tar.add(out, arcname=out.name)
    packet.with_suffix('.json').write_text(json.dumps({'bytes':packet.stat().st_size, 'sha256':sha(packet)}, indent=2))

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'build': build(task)
    elif phase == 'tests': run_tests(task)
    elif phase == 'inside-private': private_tests(task)
    elif phase == 'seal': seal(task)
    elif phase == 'run':
        prepare(task, rest[0]); build(task)
        try: run_tests(task)
        finally: seal(task)
    else: raise SystemExit('Unknown phase')
