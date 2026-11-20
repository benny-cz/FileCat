"""Guest-only native scale runner; clone verified current inputs without rebuilding FileCat."""
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys

driver = pathlib.Path(__file__).resolve().parent; root = driver.parent
plan = json.loads((driver / 'plan.json').read_text(encoding='utf-8-sig'))


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def state(value): (root / 'state.txt').write_text(value + '\n')


def idle(phase):
    for pattern in ('FileCat([.].*)?', 'dotnet'):
        probe = subprocess.run(['pgrep', '-x', pattern], capture_output=True)
        if probe.returncode == 0:
            with (root / ('busy-' + phase + '.txt')).open('wb') as f: subprocess.run(['ps', '-axo', 'pid=,ppid=,args='], stdout=f)
            state('BUSY phase=' + phase); sys.exit(75)
        if probe.returncode != 1: raise RuntimeError('Process inventory failed')


try:
    idle('verify'); state('RUNNING phase=verify')
    baseline = pathlib.Path(plan['baseline']).resolve()
    assert baseline.is_relative_to(pathlib.Path.home() / 'FileCatValidation')
    payload = root / 'payload'; assert not payload.exists()
    for row in plan['inputs']:
        relative = pathlib.PurePosixPath(row['path']); assert not relative.is_absolute() and '..' not in relative.parts
        source = (baseline / relative).resolve(); assert source.is_relative_to(baseline) and sha(source) == row['sha256']
    for row in plan['inputs']:
        source = baseline / row['path']; target = payload / row['path']; target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target); assert sha(target) == row['sha256']
    (root / 'cache-proof.json').write_text(json.dumps(dict(baseline=str(baseline), verifiedFiles=len(plan['inputs']), changedProductFiles=0)))
    for shape in ('wide', 'deep'):
        phase = '1000000-' + shape; idle(phase); state('RUNNING phase=' + phase)
        case = root / ('case-' + phase); case.mkdir()
        source = pathlib.Path(plan['sources'][shape]); marker_path = source.parent / 'fixture.json'
        assert marker_path.stat().st_size <= 32768
        marker = json.loads(marker_path.read_text(encoding='utf-8-sig'))
        assert marker['kind'] == 'FileCat retained native scale fixture v1' and marker['count'] == 1000000 and marker['shape'] == shape
        assert source.resolve() == pathlib.Path(marker['source']).resolve()
        assert marker.get('sourceCreationComplete') is True or marker.get('sourceClosureConfirmedByDatasetReady') is True
        (case / 'source-marker-before.json').write_text(json.dumps(dict(markerSha256=sha(marker_path), marker=marker), indent=2))
        env = os.environ.copy(); env.update(FILECAT_TEST_NATIVE_SCALE_COUNT='1000000', FILECAT_TEST_NATIVE_SCALE_SHAPE=shape,
            FILECAT_TEST_NATIVE_SCALE_ROOT=str(case), FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE='1', FILECAT_TEST_NATIVE_SCALE_SOURCE=str(source))
        if sys.platform == 'darwin': env['FILECAT_TEST_MAC_RUSAGE_CONTROL'] = str(driver / 'native-counter-layout.json')
        command = [str(pathlib.Path.home() / '.dotnet/dotnet'), 'FileCat.Rendering.Tests.dll', '-class', '*AnalysisAcquisitionScaleTests',
                   '-noLogo', '-noColor', '-maxThreads', '1', '-xml', str(case / 'tests.xml')]
        with (case / 'test.log').open('wb') as log: result = subprocess.run(command, cwd=payload, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
        if result != 0 or subprocess.run([sys.executable, str(driver / 'validate.py'), 'scale', str(case)]).returncode != 0:
            state('FINISHED exit=1 phase=' + phase); sys.exit(1)
        assert sha(marker_path) == json.loads((case / 'source-marker-before.json').read_text())['markerSha256']
    state('FINISHED exit=0')
except Exception as error:
    (root / 'failure.txt').write_text(str(error)); state('FAILED'); raise
