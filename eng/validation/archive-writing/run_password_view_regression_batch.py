"""Guest-only application ownership continuation using exact sealed password-browsing binaries; no rebuild."""
from pathlib import Path
import hashlib, importlib.util, json, shutil, subprocess, sys, tarfile, xml.etree.ElementTree as ET

FOLDER = 'awp-v3-ui-regression-v1'
PARENT = 'awp-v3'
NS = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()

def batch_for(task):
    path = task/'archive-workflow-source-v1/eng/validation/archive-writing/run_password_browsing_batch.py'
    spec = importlib.util.spec_from_file_location('password_view_parent', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    batch = module.batch_for(task); batch.out = task/FOLDER
    return batch

def check(task):
    batch = batch_for(task); batch.idle()
    parent = task/PARENT
    rows = json.loads((parent/'files.json').read_text())['files']
    proof = {Path(row['path']).name:row for row in rows if row['path'].startswith('binaries/')}
    assert (parent/'tests-complete.json').is_file()
    checked = []
    for path in (batch.root/'tests/FileCat.App.Tests/bin/Release/net10.0').iterdir():
        if path.suffix not in ('.dll', '.pdb') or not (path.name.startswith('FileCat.') or path.name in ('FileCat.dll', 'FileCat.pdb')): continue
        row = proof[path.name]
        assert path.stat().st_size == row['bytes'] and sha(path) == row['sha256'], str(path)
        checked.append({'path':str(path), 'bytes':row['bytes'], 'sha256':row['sha256']})
    names = {Path(row['path']).name for row in checked}
    assert {name+ext for name in ('FileCat.App.Tests', 'FileCat', 'FileCat.Core', 'FileCat.Archives') for ext in ('.dll', '.pdb')} <= names
    return {'parent':PARENT, 'parent_manifest_sha256':sha(parent/'files.json'),
        'candidate_source_commit':json.loads((parent/'delta.json').read_text())['candidate_commit'],
        'unchanged_compiled_binaries':True, 'rebuild':False, 'checked':checked}

def prepare(task):
    proof = check(task); out = task/FOLDER; out.mkdir()
    (out/'parent.json').write_text(json.dumps(proof, indent=2))
    shutil.copyfile(Path(__file__), out/'executed-driver.py')

def tests(task):
    proof = check(task); batch = batch_for(task)
    assert proof == json.loads((batch.out/'parent.json').read_text())
    selected = '|'.join('FullyQualifiedName~'+name for name in ('ArchiveMoveUiTests', 'MutationPreparationTests', 'DialogFocusOwnershipTests',
        'ExitWithWaitingJobTests', 'OperationScopeTests', 'SortFocusContinuityTests', 'ArchiveCreationCleanupUiTests', 'ArchiveRetainedCleanupUiTests'))
    batch.execute('application-ownership-regressions', batch.test_args('FileCat.App.Tests', selected, 'application-ownership-regressions'))
    tree = ET.parse(batch.out/'results/application-ownership-regressions.trx')
    counters = tree.find('.//t:Counters', NS).attrib
    assert counters['total'] == '114' and counters['failed'] == '0' and counters['passed'] == ('114' if sys.platform == 'win32' else '113'), counters
    assert check(task) == proof
    (batch.out/'tests-complete.json').write_text(json.dumps(counters, indent=2))

def seal(task):
    proof = check(task); out = task/FOLDER
    assert proof == json.loads((out/'parent.json').read_text()) and not (out/'files.json').exists()
    census = {'owned_filecat_or_test_processes':0, 'production_backend_qualified':False}
    if sys.platform == 'darwin':
        census['preserved_wake_helper'] = subprocess.check_output(['/bin/ps', '-p', '36765', '-o', 'pid=,command='], text=True).strip()
        assert census['preserved_wake_helper']
    (out/'final-census.json').write_text(json.dumps(census, indent=2))
    rows = [{'path':path.relative_to(out).as_posix(), 'bytes':path.stat().st_size, 'sha256':sha(path)} for path in sorted(out.rglob('*')) if path.is_file()]
    (out/'files.json').write_text(json.dumps({'files':rows}, indent=2))
    packet = task/(FOLDER+'-evidence.tar.gz')
    with tarfile.open(packet, 'x:gz', compresslevel=1) as tar: tar.add(out, arcname=out.name)
    packet.with_suffix('.json').write_text(json.dumps({'bytes':packet.stat().st_size, 'sha256':sha(packet)}, indent=2))

if __name__ == '__main__':
    phase, task = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task)
    elif phase == 'tests': tests(task)
    elif phase == 'seal': seal(task)
    elif phase == 'run':
        prepare(task)
        try: tests(task)
        finally: seal(task)
    else: raise SystemExit('Unknown phase')
