"""Guest-only complete Core regression plus actual native archive move and independent-reader batch."""
from pathlib import Path
from collections import Counter
import importlib.util, json, subprocess, sys, xml.etree.ElementTree as ET

SOURCE_FOLDER = 'archive-workflow-source-v1'
BATCH_FOLDER = 'archive-native-moves-v1'

def native(task):
    path = task / SOURCE_FOLDER / 'eng/validation/archive-writing/run_native_job_batch.py'
    spec = importlib.util.spec_from_file_location('archive_native_move_base', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.BATCH_FOLDER, module.PREVIOUS_FOLDER = BATCH_FOLDER, 'archive-source-case-v1'
    module.PACKET_NAME = 'archive-native-moves-' + ('windows' if sys.platform == 'win32' else 'unix') + '-delta-v1.tar.gz'
    module.tests = tests
    return module

def batch_for(task): return native(task).batch_for(task)

def prepare(task, expected):
    native(task).prepare(task, expected)
    out = batch_for(task).out
    (out / 'executed-native-move-batch.py').write_bytes(Path(__file__).read_bytes())
    (out / 'previous-full-core.trx').write_bytes((task / 'archive-source-case-v1/reused-full-core.trx').read_bytes())

def cases(path):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    tree = ET.parse(path); counts = tree.find('.//t:Counters', ns).attrib
    assert counts['failed'] == '0' and int(counts['executed']) > 0, (path, counts)
    return tree.findall('.//t:UnitTestResult', ns)

def check_inventory(out):
    old, actual = cases(out / 'previous-full-core.trx'), cases(out / 'results/archive-native-moves-core.trx')
    names = Counter(row.get('testName') for row in actual)
    previous = Counter(row.get('testName') for row in old)
    assert all(names[name] == count for name, count in previous.items()), 'A previous full-suite case disappeared.'
    passed = Counter(row.get('testName') for row in actual if row.get('outcome') == 'Passed')
    old_passed = Counter(row.get('testName') for row in old if row.get('outcome') == 'Passed')
    assert all(passed[name] >= count for name, count in old_passed.items()), 'A previous pass regressed.'
    added = [row for row in actual if row.get('testName') not in previous]
    assert len(added) == 12, [row.get('testName') for row in added]
    for row in added:
        name = row.get('testName')
        assert any(part in name for part in ('Job_planning_keeps_case_distinct_', 'ArchiveCapturedInputsTests.Move_',
            'Case_distinct_sources_are_separately_'))
        assert row.get('outcome') == ('NotExecuted' if sys.platform == 'win32' and 'Case_distinct_sources_' in name else 'Passed'), row.attrib
    (out / 'full-case-inventory.json').write_text(json.dumps({'previous_cases': len(old), 'current_cases': len(actual),
        'all_previous_passes_still_pass': True, 'added': [{'name': row.get('testName'), 'outcome': row.get('outcome')} for row in added]}, indent=2))

def tests(batch):
    batch.idle(); out = batch.out
    batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out / 'catalog'), FILECAT_7Z_WORKER_EXPORT_ROOT=str(out / 'workers'),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out / 'native-vectors'), FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out / 'workflows'),
        FILECAT_ARCHIVE_JOB_EXPORT_ROOT=str(out / 'jobs'), FILECAT_ARCHIVE_MOVE_EXPORT_ROOT=str(out / 'moves'),
        FILECAT_ARCHIVE_SOURCE_CASE_EXPORT_ROOT=str(out / 'source-case'))
    if sys.platform == 'win32':
        template = Path('C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture'); assert template.is_dir()
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(out / 'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(out / 'app'), FILECAT_ARCHIVE_CASE_ROOT=str(template))
    batch.execute('move-preflight', batch.test_args('FileCat.Core.Tests', 'FullyQualifiedName~ArchiveCapturedInputsTests.Move_', 'move-preflight'))
    rows = cases(out / 'results/move-preflight.trx'); assert len(rows) == 6 and all(row.get('outcome') == 'Passed' for row in rows)
    if sys.platform == 'win32':
        batch.execute('source-removal', batch.test_args('FileCat.Platform.Windows.Tests', 'FullyQualifiedName~Native_source_removal_', 'source-removal'))
        rows = cases(out / 'results/source-removal.trx'); assert len(rows) == 15 and all(row.get('outcome') == 'Passed' for row in rows)
    batch.execute('tests', batch.test_args('FileCat.Core.Tests', 'FullyQualifiedName~FileCat.Core.Tests', 'archive-native-moves-core'))
    check_inventory(out)
    if sys.platform == 'win32':
        batch.execute('legacy-moves', batch.test_args('FileCat.Platform.Windows.Tests', 'FullyQualifiedName~ArchiveMoveTests', 'legacy-moves'))
        rows = cases(out / 'results/legacy-moves.trx')
        added = [row for row in rows if 'Case_distinct_sources_' in row.get('testName')]
        assert len(added) == 4 and all(row.get('outcome') == 'Passed' for row in added)
        batch.execute('native', batch.test_args('FileCat.Platform.Windows.Tests',
            'FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent', 'native'))
        rows = cases(out / 'results/native.trx'); assert sum(row.get('outcome') == 'Passed' for row in rows) == 207
        batch.execute('app-entry', batch.test_args('FileCat.App.Tests', 'FullyQualifiedName~ArchiveWorkerEntryTests', 'app-entry'))
    (out / 'tests-complete.txt').write_text('PASS\n')

def run_tests(task):
    if sys.platform.startswith('linux'):
        subprocess.run(['dbus-run-session', '--', sys.executable, str(Path(__file__).resolve()), 'inside-private', str(task)], check=True)
    else: tests(batch_for(task))

def oracles(task):
    batch = batch_for(task); native(task).oracles(batch)
    if sys.platform == 'win32':
        helpers = batch.root / 'eng/validation/archive-writing/sevenzip-feasibility'
        tool, modules = task / 'password-oracle-tools-v1/7zip/7z.exe', task / 'password-oracle-tools-v1/modules'
        batch.execute('moves-sevenzip', [sys.executable, '-B', str(helpers / 'verify_pipeline.py'), str(batch.out / 'moves'), str(tool), str(modules), str(tool)])
        sevenzip = json.loads((batch.out / 'moves/independent-pipeline.json').read_text())
        assert (sevenzip['archives'], sevenzip['metadata'], sevenzip['payloads']) == (6, 33, 21)
        batch.execute('moves-rar', [sys.executable, '-B', str(batch.root / 'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),
            str(batch.out / 'moves'), str(tool), str(modules), '0', '0', '--moves'])
        batch.execute('moves-tar', [sys.executable, '-B', str(batch.root / 'eng/validation/archive-writing/verify-workflow-tar.py'),
            str(batch.out / 'moves'), str(tool), str(modules), str(helpers / 'verify_pipeline.py'), '--moves'])
    (batch.out / 'move-oracles-complete.txt').write_text('PASS\n')

def finish(task):
    batch = batch_for(task); assert (batch.out / 'move-oracles-complete.txt').read_text() == 'PASS\n'
    native(task).finish(batch)

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'build': batch_for(task).build()
    elif phase == 'tests': run_tests(task)
    elif phase == 'inside-private': native(task).private_tests(task)
    elif phase == 'oracle': oracles(task)
    elif phase == 'finish': finish(task)
    elif phase == 'run':
        prepare(task, rest[0]); batch_for(task).build(); run_tests(task); oracles(task); finish(task)
    else: raise SystemExit('Unknown phase')
