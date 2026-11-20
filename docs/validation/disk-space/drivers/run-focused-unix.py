"""Run a declared xUnit method selection from an immutable, hash-verified guest payload."""
import datetime, hashlib, json, os, pathlib, subprocess, sys, tarfile

here = pathlib.Path(__file__).resolve().parent
plan = json.loads((here / 'plan.json').read_text())
def state(status, **extra):
    (here / 'state.json').write_text(json.dumps({'status': status, 'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), **extra}, indent=2))
def busy():
    for command in (['pgrep', '-x', 'FileCat([.].*)?'], ['pgrep', '-f', '^[^ ]*dotnet +.*FileCat']):
        result = subprocess.run(command, capture_output=True)
        if result.returncode == 0: return True
        if result.returncode != 1: raise RuntimeError('Process inventory failed')
    return False
result = 1
try:
    if busy():
        state('BUSY'); sys.exit(75)
    root = pathlib.Path(plan['payload']).resolve()
    allowed = pathlib.Path.home() / 'FileCatValidation'
    if not root.is_relative_to(allowed): raise RuntimeError('Cached payload is outside the test root')
    if (here / 'results.xml').exists(): raise RuntimeError('A fresh result directory is required')
    for item in plan['files']:
        relative = pathlib.PurePosixPath(item['path'])
        if relative.is_absolute() or '..' in relative.parts: raise RuntimeError('Invalid cached payload path')
        path = (root / relative).resolve()
        if not path.is_relative_to(root) or hashlib.sha256(path.read_bytes()).hexdigest() != item['sha256']:
            raise RuntimeError('Cached test input changed: ' + item['path'])
    if busy():
        state('BUSY'); sys.exit(75)
    arguments = [str(pathlib.Path.home() / '.dotnet/dotnet'), str(root / plan['assembly'])]
    for method in plan['methods']: arguments += ['-method', method]
    arguments += ['-noLogo', '-noColor', '-maxThreads', '2', '-xml', str(here / 'results.xml')]
    state('RUNNING', verifiedFiles=len(plan['files']), baseRevision=plan['baseRevision'], methods=plan['methods'])
    with (here / 'run.log').open('wb') as log:
        result = subprocess.run(arguments, cwd=here, stdout=log, stderr=subprocess.STDOUT).returncode
    state('FINISHED', exitCode=result, verifiedFiles=len(plan['files']), baseRevision=plan['baseRevision'], methods=plan['methods'])
except Exception as error:
    state('FAILED', error=str(error)); raise
finally:
    with tarfile.open(here / 'results.tgz', 'w:gz') as archive:
        for name in ('plan.json', 'state.json', 'results.xml', 'run.log'):
            path = here / name
            if path.exists(): archive.add(path, arcname=name)
sys.exit(result)
