#!/usr/bin/env python3
import json
import os
from pathlib import Path
import runpy
import shutil
import signal
import struct
import subprocess
import sys
import time
from uuid import uuid4
import zlib

base = runpy.run_path(str(Path(__file__).with_name('mac-ax-dialog-pointer-dismiss.py')))
osa, wait_for, app_pids, window_bounds, click = (base[name] for name in
    ('osa', 'wait_for', 'app_pids', 'window_bounds', 'click'))
if len(sys.argv) != 3:
    raise SystemExit('Usage: mac-visual-dialog-pointer-dismiss.py BUNDLE.app REPORT.json')
root = (Path.home() / 'FileCatValidation').resolve(strict=True)
bundle = Path(sys.argv[1]).resolve(strict=True)
report_path = Path(sys.argv[2]).resolve()
exe = bundle / 'Contents' / 'MacOS' / 'FileCat'
if bundle.parent != root or bundle.suffix != '.app' or not exe.is_file() or report_path.parent != root:
    raise SystemExit('Use an isolated signed FileCat bundle and report under ~/FileCatValidation')
fixture = root / 'DialogPointerMacVisual' / uuid4().hex
source, target, data = (fixture / part for part in ('source', 'target', 'data'))
for folder in (source, target, data):
    folder.mkdir(parents=True)
report = {'passed': False, 'bundle': str(bundle), 'input': 'cliclick-CGEvent-mouse',
          'points': {'close': [966, 140], 'backdrop': [100, 700]}, 'screens': {}}
pid = None

def analyze(path):
    payload = path.read_bytes()
    if len(payload) > 32_000_000 or payload[:8] != b'\x89PNG\r\n\x1a\n':
        raise AssertionError('Unexpected capture PNG')
    chunks = []
    width = height = None
    offset = 8
    while offset + 12 <= len(payload):
        length = struct.unpack_from('>I', payload, offset)[0]
        kind = payload[offset + 4:offset + 8]
        body = payload[offset + 8:offset + 8 + length]
        offset += 12 + length
        if kind == b'IHDR':
            width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', body)
            if (width, height, depth, color, compression, filtering, interlace) != (2880, 1800, 8, 6, 0, 0, 0):
                raise AssertionError('Mac capture is not the expected 2880x1800 Retina RGBA display')
        elif kind == b'IDAT':
            chunks.append(body)
        elif kind == b'IEND':
            break
    stride = width * 4
    raw = zlib.decompress(b''.join(chunks))
    if len(raw) != height * (stride + 1):
        raise AssertionError('Capture PNG has an unexpected decoded size')
    previous = bytearray(stride)
    accent = None
    bright = []
    for y in range(313):
        start = y * (stride + 1)
        mode = raw[start]
        row = bytearray(raw[start + 1:start + 1 + stride])
        if mode not in range(5):
            raise AssertionError('Unsupported capture PNG filter')
        for i in range(stride):
            left = row[i - 4] if i >= 4 else 0
            up = previous[i]
            upper_left = previous[i - 4] if i >= 4 else 0
            if mode == 1:
                predictor = left
            elif mode == 2:
                predictor = up
            elif mode == 3:
                predictor = (left + up) // 2
            elif mode == 4:
                p = left + up - upper_left
                distances = (abs(p - left), abs(p - up), abs(p - upper_left))
                predictor = (left, up, upper_left)[distances.index(min(distances))]
            else:
                predictor = 0
            row[i] = (row[i] + predictor) & 255
        if y == 280:
            accent = list(row[920 * 4:920 * 4 + 3])
        if 250 <= y < 312:
            for x in range(1900, 1964):
                if min(row[x * 4:x * 4 + 3]) > 215:
                    bright.append((x, y))
        previous = row
    center = [sum(x for x, _ in bright) / len(bright),
              sum(y for _, y in bright) / len(bright)] if bright else None
    return {'accentRgb': accent, 'whitePixels': len(bright), 'iconCentroid': center}

def capture(label):
    before = set(root.glob('native-screen-*.png'))
    subprocess.run(['/usr/bin/open', '-a', str(root / 'FileCatCapture.app')], check=True,
                   capture_output=True, timeout=15)
    path = wait_for(lambda: next(iter(set(root.glob('native-screen-*.png')) - before), None),
                    'screenshot', seconds=10)
    report[label] = str(path)
    report['screens'][label] = analyze(path)

try:
    if app_pids():
        raise AssertionError('An existing FileCat process makes the probe ambiguous')
    subprocess.run(['/usr/bin/open', '-na', str(bundle), '--args', '--new-instance', '--data',
                    str(data), '--left', str(source), '--right', str(target)],
                   check=True, capture_output=True, timeout=15)
    pid = wait_for(lambda: next(iter(app_pids()), None), 'owned process')
    command = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'command='], check=True,
                             capture_output=True, text=True, timeout=5).stdout.strip()
    if not command.startswith(str(exe)):
        raise AssertionError('Wrong process executable')
    wait_for(lambda: int(osa('tell application "System Events" to get count of windows of process "FileCat"')) > 0,
             'main window')
    osa('''tell application "System Events" to tell process "FileCat"
        set position of window 1 to {80, 30}
        set size of window 1 to {1280, 800}
        set frontmost to true
    end tell''')
    bounds = window_bounds()
    report['windowBounds'] = bounds
    if any(abs(a-b)>3 for a,b in zip(bounds, (80,30,1280,800))):
        raise AssertionError('Unexpected window geometry')
    osa('tell application "System Events" to key code 98')
    time.sleep(0.5)
    capture('openDialog')
    click(966, 140)
    time.sleep(0.5)
    capture('afterCloseX')
    osa('tell application "System Events" to key code 98')
    time.sleep(0.5)
    capture('reopenedDialog')
    click(100, 700)
    time.sleep(0.5)
    capture('afterBackdrop')
    report['fixtureUnchanged'] = not list(source.iterdir()) and not list(target.iterdir())
    screens = report['screens']
    def open_marker(item):
        r, g, b = screens[item]['accentRgb']
        return b > 200 and b > r + 100 and b > g + 50
    def closed_marker(item):
        return max(screens[item]['accentRgb']) < 90
    centroid = screens['openDialog']['iconCentroid']
    report['passed'] = (open_marker('openDialog') and closed_marker('afterCloseX')
                        and open_marker('reopenedDialog') and closed_marker('afterBackdrop')
                        and screens['openDialog']['whitePixels'] >= 100
                        and centroid is not None and abs(centroid[0] - 1932) <= 3
                        and abs(centroid[1] - 280) <= 3 and report['fixtureUnchanged'])
    if not report['passed']:
        raise AssertionError('Mac dialog pointer/centering screenshot check failed')
except Exception as exc:
    report['error'] = f'{type(exc).__name__}: {exc}'
finally:
    if pid is not None and pid in app_pids():
        process = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'command='],
                                 capture_output=True, text=True, timeout=5)
        if process.stdout.strip().startswith(str(exe)):
            os.kill(pid, signal.SIGTERM)
    shutil.rmtree(fixture)
report_path.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report))
raise SystemExit(0 if report['passed'] else 1)
