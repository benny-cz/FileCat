"""Guest-only connected edit gate; freeze source once, fail early, then run the inherited inventory."""
from pathlib import Path
from collections import Counter
import importlib.util, json, os, shutil, sys, xml.etree.ElementTree as ET

FOLDER = 'awe-v2'
PACKET = 'archive-edit-sessions-delta-v2.tar.gz'
PREVIOUS = 'awe-v1'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def module(task):
    path = task/'archive-workflow-source-v1/eng/validation/archive-writing/run_password_browsing_batch.py'
    spec = importlib.util.spec_from_file_location('edit_batch_base', path)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value)
    value.FOLDER, value.PACKET = FOLDER, PACKET
    original = value.base
    def base(task):
        result = original(task); result.PREVIOUS_FOLDER = PREVIOUS; return result
    value.base = base
    value.tests = tests
    value.__file__ = str(Path(__file__).resolve())
    return value

def batch_for(task): return module(task).batch_for(task)

def prepare(task, expected):
    if shutil.disk_usage(task).free < 2 << 30: raise RuntimeError('Guest needs 2 GiB free.')
    value = module(task); previous = task/PREVIOUS
    rows = json.loads((previous/'files.json').read_text())['files']
    baseline = 'results/core-regressions.trx' if (previous/'results/core-regressions.trx').is_file() else 'previous-full-core.trx'
    for name in ('source-files.json', baseline):
        row = next(row for row in rows if row['path'] == name)
        assert (previous/name).stat().st_size == row['bytes'] and value.sha(previous/name) == row['sha256']
    value.base(task).prepare(task, expected)
    shutil.copyfile(previous/baseline, batch_for(task).out/'previous-full-core.trx')

def build(task):
    batch = batch_for(task)
    delta = json.loads((batch.out/'delta.json').read_text())['files']
    fixture_only = all(row['path'].startswith(('tests/', 'eng/validation/')) for row in delta)
    if sys.platform == 'win32' and fixture_only:
        batch.execute('FileCat.App.Tests-build', [batch.dotnet, 'build', str(batch.root/'tests/FileCat.App.Tests/FileCat.App.Tests.csproj'),
            '-c', 'Release', '--disable-build-servers', '-p:NuGetAudit=false', '-m:1', '-v', 'minimal'])
        previous = task/PREVIOUS; proof = json.loads((previous/'files.json').read_text())['files']; checked = []
        # Reference the immutable prior assets after verifying all their bytes; avoid cloning/repacking runtimes.
        for row in proof:
            if not row['path'].startswith(('app/', 'probe/')): continue
            path = previous/row['path']
            assert path.stat().st_size == row['bytes'] and module(task).sha(path) == row['sha256'], row['path']
            checked.append(row)
        assert any(row['path'] == 'app/FileCat.exe' for row in checked)
        for name, project in (('app', 'src/FileCat.App'), ('probe', 'tests/FileCat.WorkerProbe')):
            for path in (batch.root/project/'bin/Release/net10.0').iterdir():
                if path.suffix not in ('.dll', '.pdb') or not (path.name.startswith('FileCat.') or path.name in ('FileCat.dll', 'FileCat.pdb')): continue
                assert module(task).sha(path) == module(task).sha(previous/name/path.name), str(path)
        (batch.out/'runtime-reuse.json').write_text(json.dumps({'source':str(previous),
            'parent_manifest_sha256':module(task).sha(previous/'files.json'), 'checked':checked}, indent=2))
    elif sys.platform == 'win32': batch.build()
    else: module(task).build(task)

def tests(task):
    batch = batch_for(task); batch.idle(); rows = {}
    if sys.platform == 'win32':
        assets = task/PREVIOUS if (batch.out/'runtime-reuse.json').exists() else batch.out
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(assets/'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(assets/'app'),
            FILECAT_ARCHIVE_CASE_ROOT='C:/Users/fcarchive42829297/FileCatValidation/NativeCaseProbe-v2/fixture')
    def run(name, assembly, names):
        selected = '|'.join('FullyQualifiedName~'+name for name in names)
        batch.execute(name, batch.test_args(assembly, selected, name))
        counters = ET.parse(batch.out/'results'/(name+'.trx')).find('.//t:Counters', NS).attrib
        assert counters['failed'] == '0' and int(counters['passed']) > 0, counters
        rows[name] = counters; (batch.out/'passed-gates.json').write_text(json.dumps(rows, indent=2))
    run('edit-core', 'FileCat.Core.Tests', ('ArchiveEditSessionTests', 'EditSession', 'ArchiveTargetReviewTests', 'ArchivePasswordReadSessionTests', 'ArchiveUnixOriginTests'))
    run('edit-ui', 'FileCat.App.Tests', ('EditSession', 'EditCommit', 'ArchivePasswordBrowsingUiTests', 'PreparedNavigationOwnershipTests',
        'ArchiveMemberCommandUiTests', 'ArchiveTargetUpdateUiTests', 'ViewerAdmissionTests'))
    if sys.platform == 'win32':
        run('native-edit-ui', 'FileCat.App.Tests', ('ArchiveEditNativeUiTests',))
        assert rows['native-edit-ui']['passed'] == '10' and rows['native-edit-ui']['total'] == '10'
    # Complete the inherited inventory only after the connected edit gate has passed.
    run('core-regressions', 'FileCat.Core.Tests', ('FileCat.Core.Tests',))
    old = ET.parse(batch.out/'previous-full-core.trx').findall('.//t:UnitTestResult', NS)
    current = ET.parse(batch.out/'results/core-regressions.trx').findall('.//t:UnitTestResult', NS)
    before = Counter(row.get('testName') for row in old); names = Counter(row.get('testName') for row in current)
    passed = Counter(row.get('testName') for row in current if row.get('outcome') == 'Passed')
    prior_passed = Counter(row.get('testName') for row in old if row.get('outcome') == 'Passed')
    missing = list((before-names).elements()); lost = list((prior_passed-passed).elements())
    (batch.out/'full-case-inventory.json').write_text(json.dumps({'previous':len(old), 'current':len(current),
        'missing_prior_cases':missing, 'lost_prior_passes':lost, 'expected_new_cases':48}, indent=2))
    assert not missing and not lost and len(current) == len(old)+48
    batch.idle(); (batch.out/'tests-complete.json').write_text(json.dumps(rows, indent=2))

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]; task = Path(task).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'build': build(task)
    elif phase == 'tests': module(task).run_tests(task)
    elif phase == 'inside-private': module(task).private_tests(task)
    elif phase == 'seal': module(task).seal(task)
    elif phase == 'run':
        prepare(task, rest[0]); build(task)
        try: module(task).run_tests(task)
        finally: module(task).seal(task)
    else: raise SystemExit('Unknown phase')
