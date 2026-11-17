"""Guest runner: unchanged canonical assemblies, shallow generated fixture working directory."""
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys
import xml.etree.ElementTree as E

driver = pathlib.Path(__file__).resolve().parent; root = driver.parent
plan = json.loads((driver / 'plan.json').read_text()); baseline = pathlib.Path(plan['baseline']).resolve(); payload = root / 'payload'


def digest(path):
    with path.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()


def state(value): (root / 'state.txt').write_text(value + '\n')


def idle(phase):
    for pattern in ('FileCat([.].*)?', 'dotnet'):
        result = subprocess.run(['pgrep', '-x', pattern], capture_output=True)
        if result.returncode == 0:
            with (root / ('busy-' + phase + '.txt')).open('wb') as log: subprocess.run(['ps', '-axo', 'pid=,ppid=,args='], stdout=log)
            state('BUSY phase=' + phase); sys.exit(75)
        assert result.returncode == 1


try:
    idle('verify'); assert baseline.is_relative_to(pathlib.Path.home() / 'FileCatValidation') and not payload.exists()
    for row in plan['inputs']:
        relative = pathlib.PurePosixPath(row['path']); assert not relative.is_absolute() and '..' not in relative.parts
        source = (baseline / relative).resolve(); assert source.is_relative_to(baseline) and digest(source) == row['sha256']
    for row in plan['inputs']:
        target = payload / row['path']; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(baseline / row['path'], target)
        assert digest(target) == row['sha256']
    (root / 'cache-proof.json').write_text(json.dumps(dict(verifiedFiles=len(plan['inputs']), changedProductFiles=0, baseline=str(baseline), fixtureWorkingDirectory=str(payload))))
    for phase in ('app-rescan', 'render', 'settings'):
        idle(phase); state('RUNNING phase=' + phase)
        project = 'FileCat.App.Tests' if phase == 'settings' else 'FileCat.Rendering.Tests'
        assembly = payload / f'tests/{project}/bin/Debug/net10.0/{project}.dll'
        args = [str(pathlib.Path.home() / '.dotnet/dotnet'), str(assembly)]
        if phase == 'app-rescan':
            for method in plan['methods']: args += ['-method', method]
        elif phase == 'render': args += ['-class', '*AnalysisRenderingTests']
        args += ['-noLogo', '-noColor', '-maxThreads', '2', '-xml', str(root / (phase + '.xml'))]
        with (root / (phase + '.log')).open('wb') as log: result = subprocess.run(args, cwd=payload, stdout=log, stderr=subprocess.STDOUT).returncode
        a = E.parse(root / (phase + '.xml')).getroot().find('assembly'); counts = tuple(int(a.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))
        assert result == 0 and counts[0] > 0 and counts[2:4] == (0, 0), (phase, counts)
        if phase == 'app-rescan': assert counts == (12, 12, 0, 0, 0), counts
        if phase == 'render': assert counts == (814, 814, 0, 0, 0), counts
        print(json.dumps(dict(phase=phase, counts=counts)), flush=True)
    state('FINISHED exit=0')
except Exception as error:
    (root / 'failure.txt').write_text(str(error)); state('FINISHED exit=1'); raise
