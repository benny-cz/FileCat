"""Direct helper death must remove its newly observed dedicated VM provider, even with all pipes held open."""
import argparse
from array import array
import fcntl
import json
import os
from pathlib import Path
import platform
import select
import signal
import struct
import subprocess
import termios
import time
import uuid

parser = argparse.ArgumentParser()
for name in ('native', 'kernel', 'initrd', 'output'): parser.add_argument('--' + name, type=Path, required=True)
args = parser.parse_args()
assert platform.system() == 'Darwin' and os.getuid() == os.geteuid() != 0
args.output.mkdir(exist_ok=False)
image = '/System/Library/Frameworks/Virtualization.framework/Versions/A/XPCServices/com.apple.Virtualization.VirtualMachine.xpc/Contents/MacOS/com.apple.Virtualization.VirtualMachine'

def providers():
    result = []
    for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,ppid=,uid=,comm='], text=True).splitlines():
        fields = row.strip().split(None, 3)
        if len(fields) == 4 and 'Virtualization' in fields[3]:
            result.append(dict(pid=int(fields[0]), parent=int(fields[1]), uid=int(fields[2]), image=fields[3]))
    return result

def idle():
    for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,comm='], text=True).splitlines():
        fields = row.strip().split(None, 1)
        if len(fields) == 2 and (Path(fields[1]).name.startswith(('FileCat', 'testhost')) or Path(fields[1]).name in ('dotnet', 'filecat')):
            raise SystemExit('Another tester is active; postpone.\n' + row)

def read_exact(fd, count):
    data = bytearray(); deadline = time.monotonic() + 15
    while len(data) < count:
        left = deadline - time.monotonic()
        assert left > 0 and select.select([fd], [], [], left)[0]
        part = os.read(fd, count - len(data)); assert part; data.extend(part)
    return bytes(data)

def absent(pid):
    result = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'pid='], stdout=subprocess.PIPE)
    return result.returncode == 1 and not result.stdout

rows = []
for mode, role, marker in [('blocked', 1, None), ('busy', 4, b'FCVMBUSY'), ('full-output', 5, b'FCVMFLOD')]:
    for death_signal in (signal.SIGTERM, signal.SIGKILL):
        for repetition in range(2):
            idle(); baseline = providers()
            control_read, control_write = os.pipe()
            log_path = args.output / f'{len(rows):02}-{mode}-{death_signal}.log'
            with log_path.open('xb') as log:
                try:
                    process = subprocess.Popen([str(args.native), str(args.kernel), str(args.initrd), '1024', str(os.getpid()), str(control_read)],
                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log, pass_fds=(control_read,), start_new_session=True)
                finally: os.close(control_read)
                watcher = select.kqueue()
                try:
                    assert read_exact(process.stdout.fileno(), 8) == b'FCVMRDY1'
                    nonce = uuid.uuid4()
                    process.stdin.write(b'FCVMIPC1' + nonce.bytes + bytes([role]) + bytes(7)); process.stdin.flush()
                    if marker is not None:
                        assert read_exact(process.stdout.fileno(), 8) == marker
                    else:
                        frame = read_exact(process.stdout.fileno(), 36)
                        assert frame[:4] == b'FCAW' and frame[6] == 1 and frame[8:24] == nonce.bytes_le
                        size = struct.unpack_from('<I', frame, 28)[0]; assert 0 < size <= 4096
                        read_exact(process.stdout.fileno(), size)
                    time.sleep(0.2)
                    pending = array('i', [0]); fcntl.ioctl(process.stdout.fileno(), termios.FIONREAD, pending, True)
                    if mode == 'full-output': assert pending[0] >= 8192, 'The stdout backpressure fixture did not fill.'
                    created = [row for row in providers() if row['pid'] not in {item['pid'] for item in baseline}]
                    assert len(created) == 1 and created[0]['image'] == image and created[0]['uid'] == os.getuid() and created[0]['parent'] == 1
                    provider = created[0]['pid']
                    watcher.control([select.kevent(provider, filter=select.KQ_FILTER_PROC, flags=select.KQ_EV_ADD | select.KQ_EV_ENABLE | select.KQ_EV_ONESHOT,
                        fflags=select.KQ_NOTE_EXIT | 0x04000000)], 0, 0)
                    began = time.monotonic(); process.send_signal(death_signal)
                    assert process.wait(timeout=3) == -death_signal
                    assert not process.stdin.closed and not process.stdout.closed  # Also retain dedicated control writer.
                    events = watcher.control(None, 1, 3)
                    assert len(events) == 1 and events[0].ident == provider and events[0].fflags & select.KQ_NOTE_EXIT
                    deadline = time.monotonic() + 2
                    while not absent(provider) and time.monotonic() < deadline: time.sleep(0.01)
                    assert absent(provider) and absent(process.pid)
                    remaining = providers()
                    assert all(row in remaining for row in baseline), 'An unrelated baseline provider changed; stop for review.'
                    assert not any(row['pid'] == provider for row in remaining)
                    rows.append(dict(mode=mode, signal=death_signal, helper=process.pid, provider=created[0], helper_reaped=True,
                        provider_exit_observed=True, provider_wait_status=events[0].data, provider_absent_after_adopter_reap=True,
                        protocol_and_control_pipes_held_open=True, stdout_bytes_pending=pending[0], seconds=time.monotonic() - began))
                    (args.output / 'partial.json').write_text(json.dumps(rows, indent=2) + '\n')
                finally:
                    if process.poll() is None: process.kill(); process.wait(timeout=5)
                    process.stdin.close(); process.stdout.close(); os.close(control_write); watcher.close()
idle()
assert len(rows) == 12 and len({row['provider']['pid'] for row in rows}) == 12
facts = dict(experiment_passed=True, production_backend_qualified=False, cases=rows)
(args.output / 'facts.json').write_text(json.dumps(facts, indent=2) + '\n')
print(json.dumps(dict(experiment_passed=True, production_backend_qualified=False, cases=12, maximum_exit_seconds=max(row['seconds'] for row in rows))))
