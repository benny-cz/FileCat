from pathlib import Path
import hashlib, json, runpy, shutil, sys

task, batch_name, rar_pin, tar_pin = sys.argv[1:]
task = Path(task).resolve()
driver = runpy.run_path(str(task / 'archive-workflow-source-v1/eng/validation/archive-writing/run_native_job_batch.py'))
batch = driver['batch_for'](task)
assert batch.out.name == batch_name and not (batch.out / 'files.json').exists()
batch.idle()
out, root = batch.out, batch.root
assert (out / 'tests-complete.txt').read_text() == 'PASS\n'
assert (out / 'workflow-oracles-complete.txt').read_text() == 'PASS\n'
windows = sys.platform == 'win32'
assert json.loads((out / 'jobs/independent-pipeline.json').read_text())['archives'] == (6 if windows else 3)
repair = out / 'harness-oracle-repair'; repair.mkdir()
helpers = ((task / 'verify-native-job-rar-v1.py', rar_pin, root / 'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),
           (task / 'verify-native-job-tar-v1.py', tar_pin, root / 'eng/validation/archive-writing/verify-workflow-tar.py'))
def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()
for path, pin, original in helpers:
    assert sha(path) == pin
    shutil.copyfile(path, repair / path.name)
    shutil.copyfile(original, repair / ('original-' + path.name))
shutil.copyfile(Path(__file__), repair / Path(__file__).name)
for name in ('jobs-rar.log', 'jobs-tar.log'):
    if (out / name).exists(): shutil.copyfile(out / name, repair / ('original-' + name))
archives = []
for document in sorted((out / 'jobs').glob('format-*/*.json')):
    facts = json.loads(document.read_text())
    # Extension mapping is derived from the declared format, never the misleading user target suffix.
    format_name = document.parent.name.split('-')[1]
    extension = {'Tar': '.tar', 'TarGzip': '.tar.gz', 'TarBzip2': '.tar.bz2', 'TarZstd': '.tar.zst', 'SevenZip': '.7z', 'Rar5': '.rar'}[format_name]
    archive = document.with_name(document.stem + extension)
    assert sha(archive).upper() == facts['revision']
    archives.append({'path': archive.relative_to(out).as_posix(), 'bytes': archive.stat().st_size, 'sha256': sha(archive)})
assert len(archives) == (20 if windows else 11)
tool = task / ('password-oracle-tools-v1/7zip/7z.exe' if windows else
    'sevenzip-ppmd-filter-v2/tools/' + ('mac' if sys.platform == 'darwin' else 'linux') + '/7zz')
modules = task / 'password-oracle-tools-v1/modules'
batch.execute('jobs-rar-repaired', [sys.executable, '-B', str(helpers[0][0]), str(out / 'jobs'), str(tool), str(modules),
    '3' if windows else '4', '3' if windows else '0', '--jobs'])
batch.execute('jobs-tar-repaired', [sys.executable, '-B', str(helpers[1][0]), str(out / 'jobs'), str(tool), str(modules),
    str(root / 'eng/validation/archive-writing/sevenzip-feasibility/verify_pipeline.py'), '--jobs'])
for row in archives:
    archive = out / row['path']
    assert archive.stat().st_size == row['bytes'] and sha(archive) == row['sha256']
(repair / 'facts.json').write_text(json.dumps({'archive_bytes_unchanged': True, 'archives': archives,
    'repaired_helpers': [{'path': path.name, 'sha256': pin} for path, pin, _ in helpers],
    'scope': 'validation helpers only; previously completed C# suites and binaries unchanged'}, indent=2))
(out / 'job-oracles-complete.txt').write_text('PASS\n')
driver['finish'](batch)
