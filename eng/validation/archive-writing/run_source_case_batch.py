"""Guest-only native source-case qualification; reuse unchanged broad-core evidence and run the affected archive closure."""
from pathlib import Path
from collections import Counter
import importlib.util, json, os, subprocess, sys, xml.etree.ElementTree as ET

SOURCE_FOLDER = 'archive-workflow-source-v1'
BATCH_FOLDER = 'archive-source-case-v1'

def native(task):
    path = task / SOURCE_FOLDER / 'eng/validation/archive-writing/run_native_job_batch.py'
    spec = importlib.util.spec_from_file_location('archive_source_case_native', path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.BATCH_FOLDER = BATCH_FOLDER
    module.PREVIOUS_FOLDER = 'archive-native-jobs-v5' if sys.platform == 'win32' else 'archive-native-jobs-v4'
    module.PACKET_NAME = 'archive-source-case-' + ('windows' if sys.platform == 'win32' else 'unix') + '-delta-v1.tar.gz'
    module.tests = tests
    return module

def batch_for(task): return native(task).batch_for(task)

def prepare(task, expected):
    native(task).prepare(task, expected)
    batch = batch_for(task)
    (batch.out / 'executed-source-case-batch.py').write_bytes(Path(__file__).read_bytes())
    # The preceding full-core result remains part of the evidence; the changed code is in the archive closure.
    previous = task / ('archive-native-jobs-v5' if sys.platform == 'win32' else 'archive-native-jobs-v4')
    (batch.out / 'reused-full-core.trx').write_bytes((previous / 'results/archive-native-jobs-core.trx').read_bytes())

def counters(path):
    return ET.parse(path).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib

def check_core_inventory(out):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    prefixes = tuple('FileCat.Core.Tests.' + name for name in
        ('Archive', 'Strict', 'SevenZip', 'Rar', 'EmptySevenZip', 'UnixArchive', 'Zip'))
    prior = [row for row in ET.parse(out / 'reused-full-core.trx').findall('.//t:UnitTestResult', ns) if row.get('testName', '').startswith(prefixes)]
    actual = ET.parse(out / 'results/archive-source-case-core.trx').findall('.//t:UnitTestResult', ns)
    wanted = Counter(row.get('testName') for row in prior)
    added = ['FileCat.Core.Tests.ArchiveCapturedInputsTests.Job_planning_keeps_case_distinct_source_leaves_and_parents_separate(parents: ' + value + ')' for value in ('False', 'True')]
    wanted.update(added)
    assert Counter(row.get('testName') for row in actual) == wanted, 'The affected closure differs from the preceding full-suite inventory.'
    previous_passes = Counter(row.get('testName') for row in prior if row.get('outcome') == 'Passed')
    passes = Counter(row.get('testName') for row in actual if row.get('outcome') == 'Passed')
    assert all(passes[name] >= count for name, count in previous_passes.items())
    assert all(passes[name] == 1 for name in added)
    (out / 'affected-case-inventory.json').write_text(json.dumps({'previous_cases': len(prior), 'current_cases': len(actual),
        'added_cases': added, 'all_previous_passes_still_pass': True}, indent=2))

def configure(batch):
    out = batch.out
    batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out / 'catalog'), FILECAT_7Z_WORKER_EXPORT_ROOT=str(out / 'workers'),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out / 'native-vectors'), FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out / 'workflows'),
        FILECAT_ARCHIVE_JOB_EXPORT_ROOT=str(out / 'jobs'), FILECAT_ARCHIVE_SOURCE_CASE_EXPORT_ROOT=str(out / 'source-case'))
    if sys.platform == 'win32':
        template = Path('C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture')
        assert template.is_dir()
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(out / 'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(out / 'app'), FILECAT_ARCHIVE_CASE_ROOT=str(template))

def tests(batch):
    batch.idle(); configure(batch); out = batch.out
    selected = '|'.join('FullyQualifiedName~' + name for name in ('ArchiveJobBackendTests', 'ArchiveJobReviewTextTests',
        'ArchiveCapturedInputsTests.Job_', 'ArchiveCapturedInputsTests.Forest_', 'ArchiveCapturedInputsTests.Native_baseline_'))
    batch.execute('job-preflight', batch.test_args('FileCat.Core.Tests', selected, 'job-preflight'))
    assert counters(out / 'results/job-preflight.trx')['total'] == counters(out / 'results/job-preflight.trx')['passed'] == '116'
    if sys.platform == 'win32':
        batch.execute('source-case-preflight', batch.test_args('FileCat.Platform.Windows.Tests',
            'FullyQualifiedName~Actual_application_native_case_jobs_', 'source-case-preflight'))
        assert counters(out / 'results/source-case-preflight.trx')['total'] == counters(out / 'results/source-case-preflight.trx')['passed'] == '20'
    selected = '|'.join('FullyQualifiedName~FileCat.Core.Tests.' + name for name in
        ('Archive', 'Strict', 'SevenZip', 'Rar', 'EmptySevenZip', 'UnixArchive', 'Zip'))
    batch.execute('tests', batch.test_args('FileCat.Core.Tests', selected, 'archive-source-case-core'))
    check_core_inventory(out)
    finish_windows_tests(batch)

def finish_windows_tests(batch):
    out = batch.out
    if sys.platform == 'win32':
        batch.execute('native', batch.test_args('FileCat.Platform.Windows.Tests',
            'FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent', 'native'))
        assert counters(out / 'results/native.trx')['passed'] == '197'
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
        batch.execute('source-case-sevenzip', [sys.executable, '-B', str(helpers / 'verify_pipeline.py'), str(batch.out / 'source-case'), str(tool), str(modules), str(tool)])
        batch.execute('source-case-rar', [sys.executable, '-B', str(batch.root / 'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),
            str(batch.out / 'source-case'), str(tool), str(modules), '0', '0', '--source-case'])
        batch.execute('source-case-tar', [sys.executable, '-B', str(batch.root / 'eng/validation/archive-writing/verify-workflow-tar.py'),
            str(batch.out / 'source-case'), str(tool), str(modules), str(helpers / 'verify_pipeline.py'), '--source-case'])
    (batch.out / 'source-case-oracles-complete.txt').write_text('PASS\n')

def finish(task):
    batch = batch_for(task)
    assert (batch.out / 'source-case-oracles-complete.txt').read_text() == 'PASS\n'
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
