"""One native window, both storage modes, batched frames/results; no OS input injection."""
import hashlib
import json
import os
import pathlib
import re
import shlex
import shutil
import subprocess
import sys
import tarfile
import time

base = pathlib.Path(sys.argv[1]).resolve()
expected = sys.argv[2]
assert re.fullmatch('dsa-native-r[0-9]+', base.name)
assert base.parent == pathlib.Path.home() / 'FileCatValidation'
assert re.fullmatch('[0-9a-f]{64}', expected)

def observed():
    rows = []
    for args in (['pgrep', '-x', 'FileCat([.].*)?'], ['pgrep', '-f', r'^[^ ]*dotnet +.*FileCat']):
        p = subprocess.run(args, text=True, capture_output=True)
        assert p.returncode in (0, 1), p.stderr
        rows += p.stdout.split()
    return sorted(set(rows))

assert not observed(), 'BUSY: postpone native desktop testing'
assert hashlib.sha256((base / 'app.tgz').read_bytes()).hexdigest() == expected
app = base / 'app'
app.mkdir(mode=0o700, exist_ok=False)
incoming = base / 'input'
incoming.mkdir(mode=0o700, exist_ok=False)
with tarfile.open(base / 'app.tgz') as archive:
    members = archive.getmembers()
    assert len(members) < 3000 and sum(m.size for m in members) < 1024**3
    assert all(not pathlib.PurePosixPath(m.name).is_absolute() and '..' not in pathlib.PurePosixPath(m.name).parts and (m.isfile() or m.isdir()) for m in members)
    archive.extractall(incoming, filter='data')
if (incoming / 'plan.json').is_file():
    plan = json.loads((incoming / 'plan.json').read_text())
    baseline = pathlib.Path(plan['baseline']).resolve()
    assert baseline.name == 'app' and re.fullmatch('dsa-native-r[0-9]+', baseline.parent.name)
    assert baseline.parent.parent == pathlib.Path.home() / 'FileCatValidation'
    names = set()
    for entry in plan['files']:
        name = pathlib.PurePosixPath(entry['path'])
        assert not name.is_absolute() and '..' not in name.parts and ':' not in str(name) and str(name) not in names
        names.add(str(name))
        original = baseline / str(name)
        assert original.is_file() and not any(p.is_symlink() for p in (original, *original.parents))
        assert hashlib.sha256(original.read_bytes()).hexdigest() == entry['baselineSha256'], 'Cached native input changed'
        if entry['patch']:
            assert str(name) in ('DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb'), 'Product patch refused'
    assert 'FileCat' in names and 'DiskSpaceHarness.dll' in names
    for entry in plan['files']:
        source = incoming / 'patch' / entry['path'] if entry['patch'] else baseline / entry['path']
        assert hashlib.sha256(source.read_bytes()).hexdigest() == entry['sha256']
        target = app / entry['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        assert hashlib.sha256(target.read_bytes()).hexdigest() == entry['sha256']
    (base / 'cache-proof.json').write_text(json.dumps({'baseline': str(baseline), 'verifiedInputs': len(names), 'patched': [e['path'] for e in plan['files'] if e['patch']]}, indent=2) + '\n')
else:
    for source in incoming.iterdir():
        shutil.move(str(source), app / source.name)
(app / 'FileCat').chmod(0o700)
env = dict(os.environ, DOTNET_ROOT=str(pathlib.Path.home() / '.dotnet'))
if sys.platform != 'darwin':
    rows = subprocess.check_output(['ps', '-u', str(os.getuid()), '-o', 'args='], text=True).splitlines()
    servers = [shlex.split(row) for row in rows if re.search(r'(^|/)Xwayland\s', row)]
    assert len(servers) == 1, 'Expected one observed Xwayland session'
    server = servers[0]
    auth = server[server.index('-auth') + 1]
    displays = [arg for arg in server if re.fullmatch(r':[0-9]+', arg)]
    assert len(displays) == 1 and pathlib.Path(auth).is_file()
    env.update(DISPLAY=displays[0], XAUTHORITY=auth, XDG_RUNTIME_DIR='/run/user/' + str(os.getuid()), DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/' + str(os.getuid()) + '/bus')
assert not observed(), 'BUSY: postpone before launch'
with (base / 'desktop.log').open('wb') as log:
    arguments = ['./FileCat', '--replacement-ui-probe']
    if sys.platform == 'darwin':
        arguments.append('--replacement-compositor-capture')
    child = subprocess.Popen(arguments, cwd=app, env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
(base / 'owned.pid').write_text(str(child.pid) + '\n')
result = {'purpose': 'Native window control-event and filesystem subset; OS input/a11y/compositor/package coverage remains open', 'pid': child.pid, 'bundleSha256': expected, 'passed': False}
run = None
captured = []
linux_restore = None

def restore_linux_window():
    assert observed() == [str(child.pid)]
    found = subprocess.run(['xdotool', 'search', '--onlyvisible', '--pid', str(child.pid)], env=env, capture_output=True, text=True, timeout=5)
    assert found.returncode in (0, 1)
    matching = []
    for wid in found.stdout.split():
        name = subprocess.check_output(['xdotool', 'getwindowname', wid], env=env, text=True, timeout=5).strip()
        if name == 'Disk space — FileCat':
            matching.append(wid)
    if not matching:
        return None
    assert len(matching) == 1
    wid = matching[0]
    assert subprocess.check_output(['xdotool', 'getwindowpid', wid], env=env, text=True, timeout=5).strip() == str(child.pid)
    before = subprocess.check_output(['xprop', '-id', wid, '_NET_WM_STATE', 'WM_NORMAL_HINTS', '_NET_FRAME_EXTENTS'], env=env, text=True, timeout=5)
    initial_geometry = subprocess.check_output(['xdotool', 'getwindowgeometry', '--shell', wid], env=env, text=True, timeout=5)
    (base / 'linux-window-before.json').write_text(json.dumps({'pid': child.pid, 'window': wid, 'properties': before, 'geometry': initial_geometry}, indent=2) + '\n')
    restored = '_NET_WM_STATE_MAXIMIZED_VERT' in before or '_NET_WM_STATE_MAXIMIZED_HORZ' in before
    if restored:
        assert '_NET_WM_STATE_MAXIMIZED_VERT' in before and '_NET_WM_STATE_MAXIMIZED_HORZ' in before
        assert observed() == [str(child.pid)]
        subprocess.run(['xdotool', 'windowactivate', '--sync', wid], env=env, check=True, timeout=5)
        assert subprocess.check_output(['xdotool', 'getactivewindow'], env=env, text=True, timeout=5).strip() == wid
        subprocess.run(['xdotool', 'key', '--clearmodifiers', 'alt+F10'], env=env, check=True, timeout=5)
        until = time.monotonic() + 5
        while time.monotonic() < until:
            after = subprocess.check_output(['xprop', '-id', wid, '_NET_WM_STATE'], env=env, text=True, timeout=5)
            if 'MAXIMIZED' not in after:
                break
            time.sleep(.05)
        assert 'MAXIMIZED' not in after, 'The owned native window did not restore'
    else:
        after = before
    assert observed() == [str(child.pid)]
    subprocess.run(['xdotool', 'windowsize', '--sync', wid, '1100', '620'], env=env, check=True, timeout=5)
    geometry = subprocess.check_output(['xdotool', 'getwindowgeometry', '--shell', wid], env=env, text=True, timeout=5)
    return {'window': wid, 'pid': child.pid, 'before': before, 'after': after, 'initialGeometry': initial_geometry, 'resizedGeometry': geometry, 'nativeRestoreKeyUsed': restored, 'nativeResizeRequested': True}

def capture_mac(frame):
    assert re.fullmatch(r'replacement-(memory|indexed)-(keep|replace)-(ClassicDark|Classic|HighContrast)[.]png', frame)
    assert observed() == [str(child.pid)], 'Other tester appeared before capture'
    windows = subprocess.run(['/Users/benny/FileCatValidation/mac-owned-window-r34', str(child.pid)], capture_output=True, text=True, timeout=10)
    assert windows.returncode == 0, windows.stderr
    owned = [w for w in json.loads(windows.stdout) if w.get('kCGWindowIsOnscreen') is True and w.get('kCGWindowLayer') == 0 and w.get('kCGWindowBounds', {}).get('Width', 0) > 300 and w.get('kCGWindowBounds', {}).get('Height', 0) > 300]
    assert len(owned) == 1
    code = 'tell application "System Events"\nset p to first application process whose unix id is ' + str(child.pid) + '\nset frontmost of p to true\nend tell'
    activated = subprocess.run(['/usr/bin/osascript', '-e', code], capture_output=True, text=True, timeout=15)
    assert activated.returncode == 0, activated.stderr
    stdout = base / ('capture-' + frame + '.txt')
    stderr = base / ('capture-' + frame + '.err')
    helper = subprocess.run(['/usr/bin/open', '-g', '-n', '-W', '--stdout', str(stdout), '--stderr', str(stderr), '-a', '/Users/benny/FileCatValidation/FileCatCapture.app'], capture_output=True, text=True, timeout=20)
    assert helper.returncode == 0, helper.stderr
    receipt = stdout.read_text()
    match = re.search(r'capture=(/Users/benny/FileCatValidation/native-screen-[0-9A-Fa-f-]+[.]png) width=([0-9]+) height=([0-9]+)', receipt)
    assert match, receipt + stderr.read_text()
    assert observed() == [str(child.pid)]
    saved = base / ('compositor-' + frame)
    shutil.copy2(match.group(1), saved)
    captured.append({'frame': frame, 'ownedWindow': owned[0], 'width': int(match[2]), 'height': int(match[3]), 'sha256': hashlib.sha256(saved.read_bytes()).hexdigest(), 'scope': 'Main display while owned app is frontmost; existing authorized FileCatCapture helper'})
    (run / 'capture.response').write_text(frame)

def report():
    for _ in range(20):
        try:
            return json.loads((run / 'report.json').read_text())
        except (FileNotFoundError, json.JSONDecodeError):
            time.sleep(.025)
    raise RuntimeError('The owned report remained unreadable')

try:
    until = time.monotonic() + 360
    while time.monotonic() < until:
        assert observed() == [str(child.pid)], 'Owned child ended or another tester appeared'
        runs = list((app / 'native-validation').glob('dsa-*'))
        if len(runs) == 1 and (runs[0] / 'report.json').is_file():
            run = runs[0]
            if sys.platform != 'darwin' and linux_restore is None:
                linux_restore = restore_linux_window()
            request = run / 'capture.request'
            if sys.platform == 'darwin' and request.is_file():
                frame = request.read_text()
                if frame not in {c['frame'] for c in captured}:
                    capture_mac(frame)
            data = report()
            probe = data.get('replacementUiProbe')
            if probe and probe.get('finished'):
                result['finished'] = data
                assert probe['passed'], probe.get('error')
                assert len(probe['cases']) == 4 and probe['sessions'] == 8
                result['passed'] = True
                break
        time.sleep(.1)
    else:
        raise TimeoutError('The native replacement batch did not finish')
except BaseException as error:
    result['error'] = type(error).__name__ + ': ' + str(error)
finally:
    if run is not None and child.poll() is None:
        (run / 'close.request').write_text('Close only this task-owned native fixture\n')
    try:
        child.wait(timeout=30)
    except subprocess.TimeoutExpired:
        child.kill()
        child.wait(timeout=15)
        result['forcedOwnedTermination'] = True
        result['passed'] = False
    result['exitCode'] = child.returncode
    if run is not None:
        data = report()
        result['closed'] = data
        if data['state'] != 'closed' or data['sourceUnchanged'] is not True or data['deadlineClosed'] is not False or data.get('indexedProbeError'):
            result['passed'] = False
    result['postProcesses'] = observed()
    result['compositorCaptures'] = captured
    result['linuxRestore'] = linux_restore
    if result['postProcesses']:
        result['passed'] = False
    (base / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
    with tarfile.open(base / 'results.tgz', 'w:gz') as archive:
        for name in ('result.json', 'desktop.log', 'owned.pid'):
            archive.add(base / name, arcname=name)
        if (base / 'cache-proof.json').is_file():
            archive.add(base / 'cache-proof.json', arcname='cache-proof.json')
        if (base / 'linux-window-before.json').is_file():
            archive.add(base / 'linux-window-before.json', arcname='linux-window-before.json')
        for frame in base.glob('compositor-*.png'):
            archive.add(frame, arcname=frame.name)
        if run is not None:
            archive.add(run / 'report.json', arcname='report.json')
            for frame in run.glob('replacement-*.png'):
                archive.add(frame, arcname=frame.name)
print(json.dumps({k: result.get(k) for k in ('passed', 'error', 'exitCode', 'postProcesses')}))
sys.exit(0 if result['passed'] and child.returncode == 0 else 1)
