"""Guest-only typed-job regression, native worker, independent-reader and compiler-input batch."""
from pathlib import Path, PurePosixPath
import hashlib, json, runpy, shutil, sys, tarfile, xml.etree.ElementTree as ET

SOURCE_FOLDER = 'archive-workflow-source-v1'
BATCH_FOLDER = 'archive-job-backend-v1'
PREVIOUS_FOLDER = 'archive-workflow-v3'
PACKET_NAME = 'archive-job-backend-delta-v1.tar.gz'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def batch_for(task):
    root = task / SOURCE_FOLDER
    batch = runpy.run_path(str(root / 'eng/validation/archive-writing/sevenzip-feasibility/run_pipeline_batch.py'))['Pipeline'](task)
    batch.root = root
    batch.out = task / BATCH_FOLDER
    batch.env['FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY'] = str(batch.out / 'child-diagnostics')
    return batch

def prepare(task, expected):
    batch = batch_for(task)
    batch.idle()
    root, out, previous, packet = batch.root, batch.out, task / PREVIOUS_FOLDER, task / PACKET_NAME
    assert root.is_dir() and previous.is_dir() and not out.exists()
    assert sha(packet) == expected
    baseline = json.loads((previous / 'source-files.json').read_text())
    rows = {row['path']: row for row in baseline['files']}
    with tarfile.open(packet) as tar:
        delta = json.load(tar.extractfile('delta.json'))
        assert set(tar.getnames()) == {'delta.json', *(row['path'] for row in delta['files'])}
        checked = []
        for row in delta['files']:
            path = PurePosixPath(row['path'])
            assert not path.is_absolute() and '..' not in path.parts and '\\' not in str(path)
            target = root / str(path)
            assert target.resolve().is_relative_to(root.resolve()) and not target.is_symlink()
            assert (sha(target) if target.exists() else None) == row['previous_sha256'], row['path']
            assert (rows.get(row['path']) or {}).get('sha256') == row['previous_sha256'], row['path']
            data = tar.extractfile(row['path']).read()
            assert len(data) == row['bytes'] and hashlib.sha256(data).hexdigest() == row['sha256']
            checked.append((row, target, data))
        out.mkdir()
        history = out / 'previous-source'
        for row, target, data in checked:
            if target.exists():
                backup = history / row['path']
                backup.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(target, backup)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            rows[row['path']] = {key: row[key] for key in ('path', 'bytes', 'sha256')}
    baseline['files'] = [rows[key] for key in sorted(rows)]
    (out / 'source-files.json').write_text(json.dumps(baseline, indent=2))
    (out / 'delta.json').write_text(json.dumps(delta, indent=2))
    (out / 'previous-evidence.json').write_text(json.dumps({'folder': PREVIOUS_FOLDER,
        'manifest_sha256': sha(previous / 'files.json'), 'source_ledger_sha256': sha(previous / 'source-files.json')}, indent=2))
    shutil.copyfile(packet, out / PACKET_NAME)
    shutil.copyfile(Path(__file__), out / 'executed-batch.py')

def tests(batch):
    batch.idle()
    out = batch.out
    batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out / 'catalog'), FILECAT_7Z_WORKER_EXPORT_ROOT=str(out / 'workers'),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out / 'native-vectors'), FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out / 'workflows'))
    if sys.platform == 'win32':
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(out / 'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(out / 'app'))
    batch.execute('job-preflight', batch.test_args('FileCat.Core.Tests', 'FullyQualifiedName~ArchiveJobBackendTests', 'job-preflight'))
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counts = ET.parse(out / 'results/job-preflight.trx').find('.//t:Counters', ns).attrib
    assert counts['total'] == '45' and counts['passed'] == '45', counts
    if sys.platform == 'win32':
        batch.execute('native-preflight', batch.test_args('FileCat.Platform.Windows.Tests',
            'FullyQualifiedName~Actual_application_7z_updates_and_fresh_snapshot|FullyQualifiedName~Actual_application_RAR_updates_and_fresh_snapshot', 'native-preflight'))
    # All core cases exercise scheduling beyond archive jobs: transfers, synchronization, retirement and Undo.
    batch.execute('tests', batch.test_args('FileCat.Core.Tests', 'FullyQualifiedName~FileCat.Core.Tests', 'archive-jobs-core'))
    if sys.platform == 'win32':
        batch.execute('native', batch.test_args('FileCat.Platform.Windows.Tests',
            'FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent', 'native'))
        batch.execute('app-entry', batch.test_args('FileCat.App.Tests', 'FullyQualifiedName~ArchiveWorkerEntryTests', 'app-entry'))
    (out / 'tests-complete.txt').write_text('PASS\n')

def workflow(batch):
    return runpy.run_path(str(batch.root / 'eng/validation/archive-writing/run_workflow_batch.py'))

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]
    task = Path(task).resolve()
    if phase == 'prepare':
        prepare(task, rest[0])
    elif phase == 'run':
        prepare(task, rest[0])
        batch = batch_for(task)
        batch.build()
        tests(batch)
        workflow(batch)['oracles'](batch)
        workflow(batch)['finish'](batch)
    else:
        batch = batch_for(task)
        if phase == 'build': batch.build()
        elif phase == 'tests': tests(batch)
        elif phase == 'oracle': workflow(batch)['oracles'](batch)
        elif phase == 'finish': workflow(batch)['finish'](batch)
        else: raise SystemExit('Unknown phase')
