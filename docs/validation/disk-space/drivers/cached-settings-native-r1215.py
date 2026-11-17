"""Guest-only continuation over hash-verified, unchanged r1214 rendering inputs."""
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

driver = pathlib.Path(__file__).resolve().parent
base = driver.parent
plan = json.loads((driver / 'plan.json').read_text())
source = pathlib.Path(plan['source'])


def state(value):
    (base / 'state.txt').write_text(value + '\n')


def idle():
    for _ in range(3):
        for name in ('FileCat([.].*)?', 'dotnet'):
            p = subprocess.run(['pgrep', '-x', name], capture_output=True, text=True)
            if p.returncode not in (0, 1):
                raise RuntimeError('Process inventory failed: ' + p.stderr)
            if p.returncode == 0:
                state('BUSY')
                (base / 'busy-verify.txt').write_text(subprocess.check_output(['ps', '-axo', 'pid=,args='], text=True))
                sys.exit(75)
        time.sleep(1)


def verify():
    for row in plan['inputs']:
        name = pathlib.PurePosixPath(row['path'])
        if name.is_absolute() or '..' in name.parts:
            raise RuntimeError('Invalid cached input path')
        p = source / name
        if p.is_symlink() or not p.is_file():
            raise RuntimeError('Cached input is not a regular file: ' + row['path'])
        with p.open('rb') as stream:
            if hashlib.file_digest(stream, 'sha256').hexdigest() != row['sha256']:
                raise RuntimeError('Cached input changed: ' + row['path'])


try:
    idle()
    state('RUNNING phase=verify')
    verify()
    (base / 'cache-proof.json').write_text(json.dumps(dict(source=str(source), verifiedFiles=len(plan['inputs']),
        baseline='r1214', productionChanged=False, planSha256=hashlib.sha256((driver / 'plan.json').read_bytes()).hexdigest())))
    dotnet = pathlib.Path.home() / '.dotnet/dotnet'
    for phase in plan.get('phases', ('settings', '1000-wide', '1000-deep')):
        idle()
        state('RUNNING phase=' + phase)
        env = os.environ.copy()
        if phase == 'settings':
            output = base / 'settings.xml'
            command = [str(dotnet), 'FileCat.App.Tests.dll']
            for name in plan['classes']:
                command += ['-class', name]
        else:
            shape = phase.split('-')[1]
            case = base / ('case-' + phase)
            case.mkdir()
            output = case / 'tests.xml'
            env.update(FILECAT_TEST_NATIVE_SCALE_COUNT='1000', FILECAT_TEST_NATIVE_SCALE_SHAPE=shape,
                FILECAT_TEST_NATIVE_SCALE_ROOT=str(case), FILECAT_TEST_MAC_RUSAGE_CONTROL=str(source / 'native-counter-layout.json'))
            env.pop('FILECAT_TEST_NATIVE_SCALE_SOURCE', None)
            env.pop('FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE', None)
            command = [str(dotnet), 'FileCat.Rendering.Tests.dll', '-class', '*AnalysisAcquisitionScaleTests']
        command += ['-noLogo', '-noColor', '-maxThreads', '2' if phase == 'settings' else '1', '-xml', str(output)]
        with (output.parent / ('settings.log' if phase == 'settings' else 'test.log')).open('w') as log:
            result = subprocess.run(command, cwd=source, stdout=log, stderr=subprocess.STDOUT, env=env)
        assembly = ET.parse(output).getroot().find('assembly')
        counts = {k: int(assembly.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped')}
        if result.returncode != 0 or counts['total'] <= 0 or counts['failed'] != 0 or counts['errors'] != 0:
            raise RuntimeError('Actual test phase failed: ' + phase + ' ' + str(counts))
        if phase != 'settings':
            subprocess.run([sys.executable, str(source / 'validate-native-optimization.py'), 'scale', str(output.parent)], check=True)
        print(json.dumps(dict(phase=phase, counts=counts)), flush=True)
    verify()
    state('FINISHED exit=0')
except Exception:
    import traceback
    (base / 'failure.txt').write_text(traceback.format_exc())
    state('FINISHED exit=1')
    raise
