"""Guest-only shared native-job batch; no application profile registration or unconfined production fallback."""
from pathlib import Path
import importlib.util, json, os, re, shutil, subprocess, sys, time, xml.etree.ElementTree as ET

SOURCE_FOLDER = 'archive-workflow-source-v1'
BATCH_FOLDER = 'archive-native-jobs-v5'
PREVIOUS_FOLDER = 'archive-native-jobs-v4'
PACKET_NAME = 'archive-native-jobs-delta-v5.tar.gz'

def base(task):
    path = task / SOURCE_FOLDER / 'eng/validation/archive-writing/run_job_backend_batch.py'
    spec = importlib.util.spec_from_file_location('archive_native_job_base', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.SOURCE_FOLDER, module.BATCH_FOLDER = SOURCE_FOLDER, BATCH_FOLDER
    module.PREVIOUS_FOLDER, module.PACKET_NAME = PREVIOUS_FOLDER, PACKET_NAME
    return module

def batch_for(task):
    return base(task).batch_for(task)

def prepare(task, expected):
    module = base(task)
    # Refuse before the base applies any source delta if the previous phase was not durably sealed.
    previous = task / PREVIOUS_FOLDER
    ledger = json.loads((previous / 'files.json').read_text())['files']
    source = next(row for row in ledger if row['path'] == 'source-files.json')
    assert (previous / 'source-files.json').stat().st_size == source['bytes']
    assert module.sha(previous / 'source-files.json') == source['sha256']
    module.prepare(task, expected)
    out = task / BATCH_FOLDER
    shutil.copyfile(Path(__file__), out / 'executed-native-job-batch.py')
    (out / 'prepare-configuration.json').write_text(json.dumps({'source': SOURCE_FOLDER, 'batch': BATCH_FOLDER,
        'previous': PREVIOUS_FOLDER, 'packet': PACKET_NAME}, indent=2))

def build(batch):
    delta = json.loads((batch.out / 'delta.json').read_text())
    if not all(row['path'].startswith(('tests/', 'eng/validation/')) for row in delta['files']):
        batch.build()
        return
    # A fixture-only correction changes the test assembly. Preserve the exact already-built workers/native assets.
    tests_changed = any(row['path'].startswith('tests/') for row in delta['files'])
    if tests_changed:
        batch.execute('FileCat.Core.Tests-build', [batch.dotnet, 'build', str(batch.root / 'tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj'),
            '-c', 'Release', '--disable-build-servers', '-p:NuGetAudit=false', '-m:1', '-v', 'minimal'])
    if sys.platform == 'win32':
        if any(row['path'].startswith('tests/FileCat.Platform.Windows.Tests/') or row['path'].startswith('tests/Archive') for row in delta['files']):
            batch.execute('FileCat.Platform.Windows.Tests-build', [batch.dotnet, 'build', str(batch.root / 'tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj'),
                '-c', 'Release', '--disable-build-servers', '-p:NuGetAudit=false', '-m:1', '-v', 'minimal'])
        previous = batch.task / PREVIOUS_FOLDER
        ledger = json.loads((previous / 'files.json').read_text())['files']
        if not tests_changed:
            # A driver-only fix must still prove the test/compiler outputs are the sealed prior binaries.
            frozen = {row['path'].split('/')[-1]: row for row in ledger if row['path'].startswith('failed-binaries/')}
            assert frozen
            for project in ('tests/FileCat.Core.Tests', 'tests/FileCat.Platform.Windows.Tests', 'tests/FileCat.App.Tests'):
                for path in (batch.root / project / 'bin/Release/net10.0').iterdir():
                    if path.suffix in ('.dll', '.pdb') and path.name.startswith('FileCat.'):
                        row = frozen[path.name]
                        assert path.stat().st_size == row['bytes'] and base(batch.task).sha(path) == row['sha256'], str(path)
        for name in ('app', 'probe'):
            rows = [row for row in ledger if row['path'].startswith(name + '/')]
            assert rows
            for row in rows:
                path = previous / row['path']
                assert path.stat().st_size == row['bytes'] and base(batch.task).sha(path) == row['sha256'], row['path']
            shutil.copytree(previous / name, batch.out / name)
        (batch.out / 'runtime-reuse.json').write_text(json.dumps({'source': str(previous),
            'manifest_sha256': base(batch.task).sha(previous / 'files.json'), 'validated_previous_native_assets': True}, indent=2))

def tests(batch):
    batch.idle()
    out = batch.out
    batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out / 'catalog'), FILECAT_7Z_WORKER_EXPORT_ROOT=str(out / 'workers'),
        FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out / 'native-vectors'), FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out / 'workflows'),
        FILECAT_ARCHIVE_JOB_EXPORT_ROOT=str(out / 'jobs'))
    if sys.platform == 'win32':
        batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1', FILECAT_ARCHIVE_TXF_NATIVE='1', FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',
            FILECAT_ARCHIVE_PROBE_ASSETS=str(out / 'probe'), FILECAT_ARCHIVE_APP_ASSETS=str(out / 'app'))
    batch.execute('environment-preflight', batch.test_args('FileCat.Core.Tests',
        'FullyQualifiedName~SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret', 'environment-preflight'))
    environment = out / 'results/environment-preflight.trx'
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    environment_tree = ET.parse(environment)
    counters = environment_tree.find('.//t:Counters', ns).attrib
    if counters['executed'] == '0':
        cases = environment_tree.findall('.//t:UnitTestResult', ns)
        assert sys.platform in ('darwin', 'win32') and len(cases) == 1 and cases[0].get('outcome') == 'NotExecuted'
        assert cases[0].get('testName').endswith('SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret')
        folder = out / 'environment-fixture'; folder.mkdir()
        shutil.move(environment, folder / environment.name)
        (folder / 'skip.json').write_text(json.dumps({'expected_optional_system_store_skip': True,
            'platform': sys.platform, 'counters': counters}, indent=2))
    selected = '|'.join('FullyQualifiedName~' + name for name in ('ArchiveJobBackendTests', 'ArchiveJobReviewTextTests',
        'ArchiveCapturedInputsTests.Job_', 'ArchiveCapturedInputsTests.Forest_', 'ArchiveCapturedInputsTests.Native_baseline_'))
    batch.execute('job-preflight', batch.test_args('FileCat.Core.Tests', selected, 'job-preflight'))
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters = ET.parse(out / 'results/job-preflight.trx').find('.//t:Counters', ns).attrib
    assert counters['total'] == '114' and counters['passed'] == '114', counters
    if sys.platform == 'win32':
        native = '|'.join('FullyQualifiedName~' + name for name in ('Actual_application_native_jobs_', 'Actual_native_forest_', 'Actual_native_jobs_refuse_'))
        batch.execute('native-job-preflight', batch.test_args('FileCat.Platform.Windows.Tests', native, 'native-job-preflight'))
    else:
        native = '|'.join('FullyQualifiedName~UnixArchiveStorageTests.' + name for name in
            ('Native_jobs_', 'Native_forest_', 'Native_job_cancellation_'))
        batch.execute('native-job-preflight', batch.test_args('FileCat.Core.Tests', native, 'native-job-preflight'))
    counters = ET.parse(out / 'results/native-job-preflight.trx').find('.//t:Counters', ns).attrib
    wanted = '14' if sys.platform == 'win32' else '12'
    assert counters['total'] == wanted and counters['passed'] == wanted, counters
    batch.execute('tests', batch.test_args('FileCat.Core.Tests', 'FullyQualifiedName~FileCat.Core.Tests', 'archive-native-jobs-core'))
    if sys.platform == 'win32':
        batch.execute('native', batch.test_args('FileCat.Platform.Windows.Tests',
            'FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent', 'native'))
        batch.execute('app-entry', batch.test_args('FileCat.App.Tests', 'FullyQualifiedName~ArchiveWorkerEntryTests', 'app-entry'))
    (out / 'tests-complete.txt').write_text('PASS\n')

def private_tests(task):
    batch = batch_for(task)
    out = batch.out
    runtime, data = out / 'private-keyring-runtime', out / 'private-keyring-data'
    runtime.mkdir(mode=0o700)
    data.mkdir(mode=0o700)
    os.environ.pop('GNOME_KEYRING_CONTROL', None)
    os.environ.update(XDG_RUNTIME_DIR=str(runtime), XDG_DATA_HOME=str(data), FILECAT_REQUIRE_SECRET_STORE='1')
    with (out / 'private-keyring.log').open('xb') as log:
        daemon = subprocess.Popen(['gnome-keyring-daemon', '--foreground', '--unlock', '--components=secrets',
            '--control-directory', str(runtime / 'control')], stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT)
        try:
            daemon.stdin.write(b'a')
            daemon.stdin.close()
            for attempt in range(100):
                assert daemon.poll() is None, 'Private keyring exited before readiness.'
                # Asking the bus about ownership does not auto-activate a desktop Secret Service.
                owner = subprocess.run(['gdbus', 'call', '--session', '--dest', 'org.freedesktop.DBus',
                    '--object-path', '/org/freedesktop/DBus', '--method', 'org.freedesktop.DBus.GetConnectionUnixProcessID',
                    'org.freedesktop.secrets'], capture_output=True, text=True)
                if owner.returncode == 0:
                    assert re.fullmatch(r'\(uint32 ' + str(daemon.pid) + r',\)\s*', owner.stdout), owner.stdout
                    ready = subprocess.run(['gdbus', 'call', '--session', '--dest', 'org.freedesktop.secrets',
                        '--object-path', '/org/freedesktop/secrets', '--method', 'org.freedesktop.Secret.Service.ReadAlias', 'default'], capture_output=True, text=True)
                    if ready.returncode == 0 and '/collection/' in ready.stdout:
                        break
                time.sleep(0.1)
            else:
                raise RuntimeError('Private test keyring did not become ready.')
            (out / 'private-keyring.json').write_text(json.dumps({'pid': daemon.pid, 'service_owner_pid_matches': True,
                'isolated_bus': True, 'private_runtime': str(runtime), 'private_data_home': str(data), 'collection': ready.stdout.strip()}, indent=2))
            tests(batch_for(task))
        finally:
            daemon.terminate()
            try: daemon.wait(timeout=10)
            except subprocess.TimeoutExpired:
                daemon.kill()
                daemon.wait()
            (out / 'private-keyring-stopped.json').write_text(json.dumps({'pid': daemon.pid, 'exit_code': daemon.returncode}))

def run_tests(task):
    if sys.platform.startswith('linux'):
        subprocess.run(['dbus-run-session', '--', sys.executable, str(Path(__file__).resolve()), 'inside-private', str(task)], check=True)
    else:
        tests(batch_for(task))

def oracles(batch):
    workflow = base(batch.task).workflow(batch)
    workflow['oracles'](batch)
    task, helpers = batch.task, batch.root / 'eng/validation/archive-writing/sevenzip-feasibility'
    tool = task / ('password-oracle-tools-v1/7zip/7z.exe' if sys.platform == 'win32' else
        'sevenzip-ppmd-filter-v2/tools/' + ('mac' if sys.platform == 'darwin' else 'linux') + '/7zz')
    modules = task / 'password-oracle-tools-v1/modules'
    batch.execute('jobs-sevenzip', [sys.executable, '-B', str(helpers / 'verify_pipeline.py'), str(batch.out / 'jobs'), str(tool), str(modules), str(tool)])
    batch.execute('jobs-rar', [sys.executable, '-B', str(batch.root / 'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),
        str(batch.out / 'jobs'), str(tool), str(modules), '3' if sys.platform == 'win32' else '4', '3' if sys.platform == 'win32' else '0', '--jobs'])
    batch.execute('jobs-tar', [sys.executable, '-B', str(batch.root / 'eng/validation/archive-writing/verify-workflow-tar.py'),
        str(batch.out / 'jobs'), str(tool), str(modules), str(helpers / 'verify_pipeline.py'), '--jobs'])
    (batch.out / 'job-oracles-complete.txt').write_text('PASS\n')

def finish(batch):
    assert (batch.out / 'job-oracles-complete.txt').read_text() == 'PASS\n'
    base(batch.task).workflow(batch)['finish'](batch)

if __name__ == '__main__':
    phase, task, *rest = sys.argv[1:]
    task = Path(task).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'inside-private': private_tests(task)
    elif phase == 'run':
        prepare(task, rest[0])
        batch = batch_for(task)
        build(batch)
        run_tests(task)
        oracles(batch)
        finish(batch)
    else:
        batch = batch_for(task)
        if phase == 'build': build(batch)
        elif phase == 'tests': run_tests(task)
        elif phase == 'oracle': oracles(batch)
        elif phase == 'finish': finish(batch)
        else: raise SystemExit('Unknown phase')
