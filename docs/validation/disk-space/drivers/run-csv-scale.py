"""One immutable guest bundle, short warmups, then grouped production CSV scale cases."""
import argparse
import hashlib
import json
import os
import pathlib
import platform
import subprocess
import tarfile
import time

p = argparse.ArgumentParser()
p.add_argument('base')
p.add_argument('sha256')
p.add_argument('--counts', default='1000,1000000,10000000')
p.add_argument('--shapes', default='wide,deep')
a = p.parse_args()
base = pathlib.Path(a.base).resolve()
app = base / 'app'


def write(name, value):
    (base / name).write_text(json.dumps(value, indent=2) + '\n')


def idle():
    return (subprocess.run(['pgrep', '-x', 'FileCat([.].*)?'], stdout=subprocess.DEVNULL).returncode == 1
            and subprocess.run(['pgrep', '-f', r'^[^ ]*dotnet +.*FileCat'], stdout=subprocess.DEVNULL).returncode == 1)


def wait_idle(case):
    deadline = time.monotonic() + 1800
    while not idle():
        write('state.json', {'state': 'waiting-for-idle', 'case': case})
        if time.monotonic() >= deadline:
            raise RuntimeError('Shared guest stayed busy; no additional test started')
        time.sleep(30)


assert hashlib.sha256((base / 'app.tgz').read_bytes()).hexdigest() == a.sha256
assert not app.exists(), 'A fresh extraction is required'
wait_idle('integrity')
app.mkdir(mode=0o700)
with tarfile.open(base / 'app.tgz') as archive:
    members = archive.getmembers()
    assert len(members) <= 100 and sum(m.size for m in members) <= 64 * 1024 * 1024
    for member in members:
        name = pathlib.PurePosixPath(member.name)
        assert not name.is_absolute() and '..' not in name.parts and ':' not in member.name
        assert member.isfile() or member.isdir()
    archive.extractall(app, filter='data')
manifest = json.loads((app / 'inputs.json').read_text(encoding='utf-8-sig'))
for item in manifest:
    assert pathlib.PurePosixPath(item['name']).name == item['name']
    assert hashlib.sha256((app / item['name']).read_bytes()).hexdigest() == item['sha256']
result = {'purpose': 'Production CSV store/export scale, not filesystem source traversal or full-tool qualification',
          'archiveSha256': a.sha256, 'platform': platform.platform(), 'inputs': manifest, 'runs': []}
try:
    counts = [int(value) for value in a.counts.split(',')]
    shapes = a.shapes.split(',')
    assert counts and len(counts) == len(set(counts)) and all(count in (1000, 1_000_000, 10_000_000) for count in counts)
    assert shapes and len(shapes) == len(set(shapes)) and all(shape in ('wide', 'deep') for shape in shapes)
    for count in counts:
        for shape in shapes:
            case = f'{count}-{shape}'
            wait_idle(case)
            parent = base / ('records-' + case)
            parent.mkdir(mode=0o700)
            record = {'records': count, 'shape': shape, 'warmup': count == 1000}
            write('state.json', {'state': 'running', 'case': case})
            process = None
            try:
                with (base / (case + '.out')).open('wb') as out, (base / (case + '.err')).open('wb') as err:
                    process = subprocess.Popen([str(pathlib.Path.home() / '.dotnet/dotnet'), str(app / 'FileCat.CoreCrashHarness.dll'),
                        '--analysis-csv-scale', str(count), shape, str(parent)], cwd=app, stdin=subprocess.DEVNULL, stdout=out, stderr=err)
                    record['ownedPid'] = process.pid
                    write('state.json', {'state': 'running', 'case': case, 'ownedPid': process.pid})
                    try:
                        process.wait(timeout=3600)
                    except subprocess.TimeoutExpired:
                        process.terminate()
                        try:
                            process.wait(timeout=30)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait()
                        raise RuntimeError('Owned scale child exceeded its deadline; do not claim completed scale evidence')
                record.update(exitCode=process.returncode, ownedProcessExited=process.poll() is not None)
                if record['exitCode'] != 0:
                    raise RuntimeError('Native scale child exited ' + str(record['exitCode']) + ': ' + (base / (case + '.err')).read_text(errors='replace')[-1500:])
                lines = (base / (case + '.out')).read_text().splitlines()
                if lines:
                    record['measurement'] = json.loads(lines[-1])
                assert record['exitCode'] == 0 and record['measurement']['passed'] is True
                assert record['measurement']['records'] == count and record['measurement']['shape'] == shape
                if record['measurement'].get('limitRefusal'):
                    m = record['measurement']
                    assert count == 10_000_000 and m['fullCsvExportQualified'] is False and m['retainedSourceUnchanged'] and m['destinationWasNeverOpened']
                    assert m['remainingQuotaBytes'] == m['remainingProfileQuotaBytes'] == 0
                else:
                    assert len(record['measurement']['reports']) == 2
                    assert all(r['verifiedEveryDeclaredRow'] and r['lines'] == count + 2 for r in record['measurement']['reports'])
                print('PASS ' + case + ' actual child exited', flush=True)
            except BaseException as error:
                record['error'] = str(error)
                raise
            finally:
                if process is not None and process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=30)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                    record['forcedOwnedTermination'] = True
                result['runs'].append(record)
                write('result.json', result)
    result['passed'] = True
    write('result.json', result)
    write('state.json', {'state': 'finished', 'passed': True})
except BaseException as error:
    write('state.json', {'state': 'finished', 'passed': False, 'error': str(error)})
    raise
