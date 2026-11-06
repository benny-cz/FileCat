"""Own-parent death probes keep protocol pipes open; observe VM stop and adopter reaping independently."""
import argparse
import json
import os
from pathlib import Path
import platform
import select
import signal
import struct
import subprocess
import sys
import time
import uuid

if len(sys.argv) > 1 and sys.argv[1] == '--gated-exec':
    parent, native, kernel, initrd = sys.argv[2:]
    if os.read(0, 1) != b'X': raise SystemExit(64)
    os.execv(native, [native, kernel, initrd, '1536', parent])
if len(sys.argv) > 1 and sys.argv[1] == '--launcher':
    native, kernel, initrd, control, mode = sys.argv[2:]
    command = [native, kernel, initrd, '1536', str(os.getpid())]
    if mode == 'before-exec':
        command = [sys.executable, __file__, '--gated-exec', str(os.getpid()), native, kernel, initrd]
    child = subprocess.Popen(command, stdin=0, stdout=1, stderr=2, close_fds=True)
    os.write(int(control), (json.dumps(dict(parent=os.getpid(), child=child.pid)) + '\n').encode())
    os.close(int(control))
    while True: signal.pause()

parser = argparse.ArgumentParser()
parser.add_argument('--native', type=Path, required=True)
parser.add_argument('--kernel', type=Path, required=True)
parser.add_argument('--initrd', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
assert platform.system() == 'Darwin' and os.getuid() == os.geteuid() != 0
args.output.mkdir(exist_ok=False)

def idle():
    for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,comm='], text=True).splitlines():
        fields = row.strip().split(None, 1)
        if len(fields) == 2:
            name = Path(fields[1]).name
            if name.startswith(('FileCat', 'testhost')) or name in ('dotnet', 'filecat'):
                raise SystemExit('Another tester is active; postpone.\n' + row)

def read_exact(fd, size, seconds=15):
    data = bytearray()
    deadline = time.monotonic() + seconds
    while len(data) < size:
        remaining = deadline - time.monotonic()
        assert remaining > 0 and select.select([fd], [], [], remaining)[0], 'Owned startup timed out.'
        chunk = os.read(fd, size - len(data))
        assert chunk, 'Owned startup ended early.'
        data.extend(chunk)
    return bytes(data)

def absent(pid):
    row = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'pid='], stdout=subprocess.PIPE, check=False)
    return row.returncode == 1 and not row.stdout

def wait_absent(pid):
    deadline = time.monotonic() + 3
    while time.monotonic() < deadline:
        if absent(pid): return
        time.sleep(0.01)
    raise AssertionError('Owned orphan was not reaped by its adopter.')

def probe(mode, death_signal, index):
    idle()
    path = args.output / f'{index:02}-{mode}-{death_signal}.log'
    control_read, control_write = os.pipe()
    child_pid = None
    watcher = None
    with path.open('xb') as log:
        try:
            parent = subprocess.Popen([sys.executable, __file__, '--launcher', str(args.native), str(args.kernel), str(args.initrd), str(control_write), mode],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log, pass_fds=(control_write,), start_new_session=True)
        finally:
            os.close(control_write)
        try:
            message = bytearray()
            while not message.endswith(b'\n'):
                assert len(message) < 512
                message.extend(read_exact(control_read, 1))
            launched = json.loads(message)
            assert launched['parent'] == parent.pid and launched['child'] > 1
            child_pid = launched['child']
            watcher = select.kqueue()
            watcher.control([select.kevent(child_pid, filter=select.KQ_FILTER_PROC, flags=select.KQ_EV_ADD | select.KQ_EV_ENABLE | select.KQ_EV_ONESHOT,
                fflags=select.KQ_NOTE_EXIT | 0x04000000)], 0, 0)  # Public NOTE_EXITSTATUS, same UID can signal target.
            if mode == 'setup':
                deadline = time.monotonic() + 5
                wanted = f'FILECAT_PARENT_GUARD_READY pid={child_pid} parent={parent.pid}'
                while wanted not in path.read_text():
                    assert time.monotonic() < deadline and parent.poll() is None and not absent(child_pid)
                    time.sleep(0.001)
            elif mode in ('blocked', 'busy'):
                assert read_exact(parent.stdout.fileno(), 8) == b'FCVMRDY1'
                nonce = uuid.uuid4()
                parent.stdin.write(b'FCVMIPC1' + nonce.bytes + bytes([1 if mode == 'blocked' else 4]) + bytes(7))
                parent.stdin.flush()
                if mode == 'busy':
                    assert read_exact(parent.stdout.fileno(), 8) == b'FCVMBUSY'
                else:
                    header = read_exact(parent.stdout.fileno(), 36)
                    magic, version, kind, reserved, received_nonce, sequence, length, tail = struct.unpack('<4sHBB16sIII', header)
                    assert (magic, version, kind, reserved, received_nonce, sequence, tail) == (b'FCAW', 1, 1, 0, nonce.bytes_le, 0, 0)
                    assert 0 < length <= 4096
                    read_exact(parent.stdout.fileno(), length)
            assert parent.poll() is None and not absent(child_pid) and not parent.stdin.closed
            killed = time.monotonic()
            parent.send_signal(death_signal)
            assert parent.wait(timeout=3) == -death_signal
            if mode == 'before-exec':
                # Release native exec only after its expected parent has actually exited.
                parent.stdin.write(b'X'); parent.stdin.flush()
            # Deliberately retain the observer's protocol read/write ends. An idle guest cannot see stdin EOF.
            assert not parent.stdin.closed and not parent.stdout.closed
            events = watcher.control(None, 1, 5)
            elapsed = time.monotonic() - killed
            assert len(events) == 1 and events[0].ident == child_pid and events[0].filter == select.KQ_FILTER_PROC
            assert events[0].fflags & select.KQ_NOTE_EXIT and os.WIFEXITED(events[0].data)
            code = os.WEXITSTATUS(events[0].data)
            assert code == (65 if mode == 'before-exec' else 69), (mode, code, path.read_text())
            assert elapsed < 5
            wait_absent(child_pid)
            text = path.read_text()
            if mode == 'before-exec':
                assert 'FILECAT_PARENT_GUARD_READY' not in text and 'FILECAT_GUEST_READY' not in text and 'FILECAT_HOST_FACTS' not in text
                host = None
            else:
                rows = [json.loads(row.removeprefix('FILECAT_HOST_FACTS ')) for row in text.splitlines() if row.startswith('FILECAT_HOST_FACTS ')]
                assert len(rows) == 1
                host = rows[0]
                assert host['pid'] == child_pid and host['parent'] == parent.pid and host['uid'] == host['euid'] == os.getuid()
                assert host['parentExited'] is True and host['parentMonitorFailed'] is False and host['stopReason'] == 'parent'
                assert host['state'] == 0 and host['stopError'] == host['startError'] == host['validationError'] == host['guestError'] == ''
                assert host['valid'] is True
                if mode in ('blocked', 'busy'):
                    assert host['started'] is True and host['stopRequested'] is True and host['stopCompleted'] is True and host['forcedStop'] is True
                    assert 'FILECAT_WAIT_STATUS=' not in text and 'FILECAT_GUEST_FINISHED' not in text
            return dict(mode=mode, signal=death_signal, parent=parent.pid, child=child_pid, parent_reaped=True, child_absent_after_adopter_reap=True,
                pipes_held_open=True, exit=code, elapsed_seconds=elapsed, host=host)
        finally:
            os.close(control_read)
            if watcher is not None: watcher.close()
            # This process group was freshly created for this case and contains only its fixture parent/helper.
            if parent.poll() is None:
                # The still-live owned session leader prevents process-group identity reuse.
                os.killpg(parent.pid, signal.SIGKILL); parent.wait(timeout=5)
            if child_pid is not None and not absent(child_pid):
                listing = subprocess.check_output(['/bin/ps', '-p', str(child_pid), '-o', 'pgid=,comm='], text=True).strip().split(None, 1)
                if len(listing) == 2 and listing[0] == str(parent.pid) and (listing[1] == str(args.native) or mode == 'before-exec' and Path(listing[1]).name.startswith('Python')):
                    os.kill(child_pid, signal.SIGKILL); wait_absent(child_pid)
                else:
                    raise AssertionError('Unexpected process identity; retained diagnostics and did not signal it.')
            parent.stdin.close(); parent.stdout.close()

idle()
malformed = []
for value, expected in [('1', 64), ('0', 64), ('-2', 64), ('+2', 64), ('02', 64), ('2147483648', 64), ('not-a-pid', 64),
                        (str(os.getppid()), 65)]:
    result = subprocess.run([str(args.native), str(args.kernel), str(args.initrd), '1536', value], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=5)
    assert result.returncode == expected and not result.stdout and not result.stderr
    malformed.append(dict(argument=value, exit=expected))
results = []
for mode, repetitions in [('before-exec', 2), ('setup', 4), ('blocked', 2), ('busy', 2)]:
    for death_signal in (signal.SIGKILL, signal.SIGTERM):
        for _ in range(repetitions):
            results.append(probe(mode, death_signal, len(results)))
            (args.output / 'partial.json').write_text(json.dumps(results, indent=2) + '\n')
idle()
assert len(results) == 20 and len({row['child'] for row in results}) == 20
assert any(row['host'] and row['host']['parentExitEvent'] for row in results)
facts = dict(experiment_passed=True, production_backend_qualified=False, malformed=malformed, parent_death_cases=results)
(args.output / 'facts.json').write_text(json.dumps(facts, indent=2) + '\n')
print(json.dumps(dict(experiment_passed=True, production_backend_qualified=False, parent_death_cases=len(results),
    malformed_cases=len(malformed), maximum_stop_seconds=max(row['elapsed_seconds'] for row in results))))
