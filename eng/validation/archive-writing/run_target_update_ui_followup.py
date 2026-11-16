"""Guest-only fixture follow-up with explicit reuse of byte-identical compiled regression lanes."""
from pathlib import Path
import hashlib, importlib.util, json, shutil, subprocess, sys

SOURCE = 'archive-workflow-source-v1'
BATCH = 'awt-v4'
PREVIOUS = 'awt-v3'
FIXTURE = 'tests/FileCat.App.Tests/MutationPreparationTests.cs'


def component(task):
    spec = importlib.util.spec_from_file_location('target_update', task / SOURCE / 'eng/validation/archive-writing/run_target_update_ui_batch.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.BATCH_FOLDER = BATCH
    original = module.native
    def native(root):
        result = original(root)
        result.PREVIOUS_FOLDER = PREVIOUS
        result.PACKET_NAME = 'archive-target-update-ui-delta-v4.tar.gz'
        return result
    module.native = native
    return module


def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def runtime(batch):
    rows = {}
    for project in ('tests/FileCat.Core.Tests', 'tests/FileCat.WorkerProbe', 'src/FileCat.ArchiveWorker',
                    'src/FileCat.Archives.Writing', 'tests/FileCat.App.Tests', 'tests/FileCat.Platform.Windows.Tests'):
        folder = batch.root / project / 'bin/Release/net10.0'
        if not folder.exists(): continue
        for path in folder.iterdir():
            if path.is_file() and path.name != 'FileCat.App.Tests.dll' and path.name != 'FileCat.App.Tests.pdb' and (
                path.suffix in ('.dll', '.pdb', '.exe') or path.name.endswith(('.deps.json', '.runtimeconfig.json'))):
                rows[path.relative_to(batch.root).as_posix()] = sha(path)
    return rows


def prepare(task, expected):
    c = component(task); c.prepare(task, expected); batch = c.batch_for(task)
    delta = json.loads((batch.out / 'delta.json').read_text())['files']
    assert [row['path'] for row in delta if row['path'].endswith('.cs')] == [FIXTURE]
    assert all(row['path'] == FIXTURE or row['path'].startswith('eng/validation/') and row['path'].endswith('.py') for row in delta)
    before = runtime(batch)
    ledger = json.loads((task / PREVIOUS / 'files.json').read_text())['files']
    binaries = {Path(row['path']).name: row for row in ledger if row['path'].startswith(('binaries/', 'failed-binaries/'))}
    bound = []
    for name, digest in before.items():
        leaf = Path(name).name
        if leaf in binaries:
            assert digest == binaries[leaf]['sha256'], name
            bound.append(name)
    assert bound
    (batch.out / 'runtime-before.json').write_text(json.dumps(before, indent=2))
    (batch.out / 'previous-binary-binding.json').write_text(json.dumps({'previous': PREVIOUS, 'files': bound}, indent=2))
    shutil.copyfile(Path(__file__), batch.out / 'executed-fixture-followup.py')


def build(task):
    c = component(task); batch = c.batch_for(task)
    c.build(task)
    before = json.loads((batch.out / 'runtime-before.json').read_text())
    after = runtime(batch)
    assert before == after, 'Fixture follow-up changed a production or Core/native test runtime.'
    (batch.out / 'unchanged-runtime.json').write_text(json.dumps({'byte_identical': True, 'files': after}, indent=2))


def tests(batch):
    c = component(batch.task); previous = batch.task / PREVIOUS
    assert json.loads((batch.out / 'unchanged-runtime.json').read_text())['byte_identical']
    reuse = {}
    prior_core = previous / 'results/archive-creation-core.trx'
    if prior_core.exists():
        c.cases(prior_core)
        reuse['tests'] = 'archive-creation-core'
        for name in ('catalog', 'workers', 'native-vectors', 'workflows', 'jobs', 'moves', 'source-case', 'cleanup'):
            if (previous / name).exists(): shutil.copytree(previous / name, batch.out / name)
    if sys.platform == 'win32':
        for name, result in [('native', 'native'), ('creation-cleanup-crashes', 'creation-cleanup-crashes'), ('app-entry', 'app-entry')]:
            c.cases(previous / 'results' / (result + '.trx')); reuse[name] = result
    execute = batch.execute
    reused = []
    def checked(name, args, *rest, **kwargs):
        if name not in reuse: return execute(name, args, *rest, **kwargs)
        result = 'results/' + reuse[name] + '.trx'
        destination = batch.out / result; destination.parent.mkdir(exist_ok=True)
        shutil.copyfile(previous / result, destination)
        source_log = previous / (name + '.log')
        shutil.copyfile(source_log, batch.out / (name + '.log'))
        reused.append({'lane': name, 'result': result, 'sha256': sha(destination), 'log_sha256': sha(source_log),
                       'executed_in_previous_batch': True, 'rerun_in_this_batch': False})
    batch.execute = checked
    # Production and Core/native runtimes are unchanged; the corrected fixture and all focused UI lanes rerun.
    (batch.out / 'proceed-with-regressions').write_text('Unchanged-runtime fixture follow-up.\n')
    c.tests(batch)
    (batch.out / 'reused-regressions.json').write_text(json.dumps({'previous': PREVIOUS,
        'previous_manifest_sha256': sha(previous / 'files.json'), 'lanes': reused}, indent=2))


def private_tests(task):
    c = component(task); native = c.native(task); native.tests = tests
    native.private_tests(task)


def run_tests(task):
    if sys.platform.startswith('linux'):
        subprocess.run(['dbus-run-session', '--', sys.executable, str(Path(__file__).resolve()), 'inside-private', str(task)], check=True)
        return
    c = component(task); c.tests = tests
    c.run_tests(task)


if __name__ == '__main__':
    phase, root, *rest = sys.argv[1:]; task = Path(root).resolve()
    if phase == 'prepare': prepare(task, rest[0])
    elif phase == 'build': build(task)
    elif phase == 'tests': run_tests(task)
    elif phase == 'inside-private': private_tests(task)
    elif phase == 'oracle': component(task).oracles(task)
    elif phase == 'finish': component(task).finish(task)
    elif phase == 'run':
        prepare(task, rest[0]); build(task); run_tests(task)
        component(task).oracles(task); component(task).finish(task)
    else: raise SystemExit('Unknown phase')
