"""Windows-only directory guard follow-up; preserve and prove the unchanged complete Core regression."""
from pathlib import Path
import hashlib, importlib.util, json, shutil, sys, xml.etree.ElementTree as ET

SOURCE_FOLDER = 'archive-workflow-source-v1'
BATCH_FOLDER = 'archive-native-moves-v4'
PREVIOUS_FOLDER = 'archive-native-moves-v3'

def native(task):
    assert sys.platform == 'win32'
    path = task / SOURCE_FOLDER / 'eng/validation/archive-writing/run_native_job_batch.py'
    spec = importlib.util.spec_from_file_location('archive_native_guard_base', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.BATCH_FOLDER, module.PREVIOUS_FOLDER = BATCH_FOLDER, PREVIOUS_FOLDER
    module.PACKET_NAME = 'archive-native-moves-windows-delta-v4.tar.gz'
    return module

def batch_for(task): return native(task).batch_for(task)
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def prepare(task, expected):
    native(task).prepare(task, expected)
    batch = batch_for(task); previous = task / PREVIOUS_FOLDER
    (batch.out / 'executed-native-move-guard-batch.py').write_bytes(Path(__file__).read_bytes())
    delta = json.loads((batch.out / 'delta.json').read_text())['files']
    assert {row['path'] for row in delta} == {
        'src/FileCat.Platform.Windows/Archives/WindowsArchiveJobBackend.cs',
        'tests/FileCat.Platform.Windows.Tests/WindowsArchiveSourceRemovalTests.cs',
        'tests/FileCat.Platform.Windows.Tests/ArchiveAppContainerMoveJobTests.cs',
        'eng/validation/archive-writing/run_native_move_guard_batch.py'}
    ledger = json.loads((previous / 'files.json').read_text())['files']
    retained = []
    for row in ledger:
        name = row['path']
        if not (name.startswith(('catalog/', 'workers/', 'workflows/')) or name in
                ('results/archive-native-moves-core.trx', 'full-case-inventory.json')): continue
        source, target = previous / name, batch.out / name
        assert source.stat().st_size == row['bytes'] and sha(source) == row['sha256'], name
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target); retained.append(row)
    assert retained and (batch.out / 'results/archive-native-moves-core.trx').is_file()
    (batch.out / 'reused-evidence.json').write_text(json.dumps({'previous_manifest_sha256': sha(previous / 'files.json'),
        'complete_Core_regression_reused_not_rerun': True, 'files': retained}, indent=2))

def cases(path):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}; tree = ET.parse(path)
    counts = tree.find('.//t:Counters', ns).attrib
    assert counts['failed'] == '0' and int(counts['executed']) > 0, (path, counts)
    return tree.findall('.//t:UnitTestResult', ns)

def tests(batch):
    batch.idle(); out = batch.out; previous = batch.task / PREVIOUS_FOLDER
    # Refuse evidence reuse if any FileCat assembly/PDB in the actual Core test closure changed.
    proof = []
    for path in sorted((batch.root / 'tests/FileCat.Core.Tests/bin/Release/net10.0').iterdir()):
        if not path.name.startswith('FileCat.') or path.suffix not in ('.dll', '.pdb'): continue
        old = previous / 'failed-binaries' / path.name
        assert old.is_file() and sha(path) == sha(old), 'Full Core rerun required: ' + str(path)
        proof.append({'name': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path)})
    assert proof and any(row['name'] == 'FileCat.Core.Tests.dll' for row in proof)
    (out / 'unchanged-core-binaries.json').write_text(json.dumps({'files': proof}, indent=2))
    template = Path('C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture'); assert template.is_dir()
    batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
        FILECAT_ARCHIVE_PROBE_ASSETS=str(out / 'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(out / 'app'), FILECAT_ARCHIVE_CASE_ROOT=str(template),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out / 'native-vectors'), FILECAT_ARCHIVE_JOB_EXPORT_ROOT=str(out / 'jobs'),
        FILECAT_ARCHIVE_MOVE_EXPORT_ROOT=str(out / 'moves'), FILECAT_ARCHIVE_SOURCE_CASE_EXPORT_ROOT=str(out / 'source-case'))
    batch.execute('source-removal', batch.test_args('FileCat.Platform.Windows.Tests', 'FullyQualifiedName~Native_source_removal_', 'source-removal'))
    rows = cases(out / 'results/source-removal.trx'); assert len(rows) == 23 and all(row.get('outcome') == 'Passed' for row in rows)
    batch.execute('legacy-moves', batch.test_args('FileCat.Platform.Windows.Tests', 'FullyQualifiedName~ArchiveMoveTests', 'legacy-moves'))
    rows = cases(out / 'results/legacy-moves.trx'); added = [row for row in rows if 'Case_distinct_sources_' in row.get('testName')]
    assert len(added) == 4 and all(row.get('outcome') == 'Passed' for row in added)
    batch.execute('move-jobs', batch.test_args('FileCat.Platform.Windows.Tests', 'FullyQualifiedName~Actual_application_native_move_jobs_', 'move-jobs'))
    rows = cases(out / 'results/move-jobs.trx'); assert len(rows) == 10 and all(row.get('outcome') == 'Passed' for row in rows)
    batch.execute('native', batch.test_args('FileCat.Platform.Windows.Tests',
        '(FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent)&FullyQualifiedName!~Actual_application_native_move_jobs_', 'native'))
    rows = cases(out / 'results/native.trx'); assert sum(row.get('outcome') == 'Passed' for row in rows) == 197
    batch.execute('app-entry', batch.test_args('FileCat.App.Tests', 'FullyQualifiedName~ArchiveWorkerEntryTests', 'app-entry'))
    (out / 'tests-complete.txt').write_text('PASS\n')

def move_driver(task):
    path = task / SOURCE_FOLDER / 'eng/validation/archive-writing/run_native_move_batch.py'
    spec = importlib.util.spec_from_file_location('archive_native_guard_readers', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.native, module.batch_for = native, batch_for
    return module

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'build': batch_for(task).build()
    elif phase == 'tests': tests(batch_for(task))
    elif phase == 'oracle': move_driver(task).oracles(task)
    elif phase == 'finish': move_driver(task).finish(task)
    else: raise SystemExit('Unknown phase')
