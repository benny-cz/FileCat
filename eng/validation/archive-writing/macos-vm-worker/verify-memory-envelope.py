"""Measure the whole guest's fixed RAM separately from same-UID native helper/provider accounting."""
import argparse
import json
import os
from pathlib import Path
import re
import select
import signal
import struct
import subprocess
import time
import uuid
from macos_rusage import Sampler

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
args = parser.parse_args()
root = args.root.resolve(strict=True)
output = root / 'memory-envelope'
output.mkdir(exist_ok=False)
sampler = Sampler(root / 'rusage-abi.json')
image = '/System/Library/Frameworks/Virtualization.framework/Versions/A/XPCServices/com.apple.Virtualization.VirtualMachine.xpc/Contents/MacOS/com.apple.Virtualization.VirtualMachine'

def idle():
    for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,comm='], text=True).splitlines():
        fields = row.strip().split(None, 1)
        if len(fields) == 2 and (Path(fields[1]).name.startswith(('FileCat', 'testhost')) or Path(fields[1]).name in ('dotnet', 'filecat')):
            raise SystemExit('Another tester is active; postpone.\n' + row)

def providers():
    rows = []
    for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,ppid=,uid=,comm='], text=True).splitlines():
        fields = row.strip().split(None, 3)
        if len(fields) == 4 and fields[3] == image:
            rows.append(dict(pid=int(fields[0]), parent=int(fields[1]), uid=int(fields[2]), image=fields[3]))
    return rows

def read_exact(fd, count):
    data = bytearray(); deadline = time.monotonic() + 15
    while len(data) < count:
        left = deadline - time.monotonic()
        assert left > 0 and select.select([fd], [], [], left)[0]
        part = os.read(fd, count - len(data)); assert part; data.extend(part)
    return bytes(data)

def absent(pid):
    row = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'pid='], stdout=subprocess.PIPE)
    return row.returncode == 1 and not row.stdout

rows = []
for name, role in [('control-256m', 6), ('overrun-1280m', 7), ('entropy-draining-10s', 5), ('entropy-blocked-10s', 5)]:
    idle(); baseline = providers()
    cancel_read, cancel_write = os.pipe()
    samples = []; values = []; decoded = bytearray(); received = 0; complete = False
    path = output / (name + '.log')
    with path.open('xb') as log:
        try:
            worker = subprocess.Popen([str(root / 'FileCat.ArchiveLinuxVmWorker'), str(root / 'input/kernel.Image'), str(root / 'probe-image/input/initrd-worker.cpio'),
                '1024', str(os.getpid()), str(cancel_read)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log, pass_fds=(cancel_read,), start_new_session=True)
        finally: os.close(cancel_read)
        try:
            assert read_exact(worker.stdout.fileno(), 8) == b'FCVMRDY1'
            created = [row for row in providers() if row['pid'] not in {item['pid'] for item in baseline}]
            assert len(created) == 1 and created[0]['uid'] == os.getuid() and created[0]['parent'] == 1
            provider = created[0]['pid']
            worker.stdin.write(b'FCVMIPC1' + uuid.uuid4().bytes + bytes([role]) + bytes(7)); worker.stdin.flush()
            marker = read_exact(worker.stdout.fileno(), 8)
            assert marker == (b'FCVMMEM1' if role in (6, 7) else b'FCVMFLOD')
            os.set_blocking(worker.stdout.fileno(), False)
            began = time.monotonic(); canceled_at = None
            while True:
                now = time.monotonic()
                assert now - began < 20, 'Owned diagnostic exceeded its deadline.'
                for kind, pid in [('helper', worker.pid), ('provider', provider), ('observer', os.getpid())]:
                    try:
                        value = sampler.read(pid); value.update(kind=kind, seconds=now-began); samples.append(value)
                        # Diagnostic abort watermarks protect the disposable Mac; these are not production limits.
                        assert kind != 'helper' or value['footprint'] < 128 << 20, 'Native helper growth exceeded diagnostic watermark.'
                        assert kind != 'provider' or value['footprint'] < 2 << 30, 'Provider growth exceeded diagnostic watermark.'
                    except ProcessLookupError:
                        assert worker.poll() is not None or kind == 'provider'
                should_read = role in (6, 7) or name == 'entropy-draining-10s'
                if should_read and select.select([worker.stdout.fileno()], [], [], 0)[0]:
                    data = os.read(worker.stdout.fileno(), 65536); received += len(data)
                    if role in (6, 7):
                        decoded.extend(data)
                        while len(decoded) >= 8:
                            if decoded[:8] == b'FCVMDONE':
                                complete = True; del decoded[:8]; continue
                            assert decoded[:8] == b'FCVMPAGE'
                            if len(decoded) < 16: break
                            count = struct.unpack_from('<Q', decoded, 8)[0]
                            assert count == (len(values) + 1) * (8 << 20)
                            values.append(count); del decoded[:16]
                if role == 5 and now - began >= 10 and canceled_at is None:
                    os.close(cancel_write); cancel_write = None; canceled_at = time.monotonic()
                if worker.poll() is not None:
                    # Drain only the small allocation records/footer. Flood-output cases intentionally retain unread bytes.
                    if role in (6, 7):
                        tail = worker.stdout.read() or b''; decoded.extend(tail); received += len(tail)
                        while len(decoded) >= 8:
                            if decoded[:8] == b'FCVMDONE': complete = True; del decoded[:8]; continue
                            assert len(decoded) >= 16 and decoded[:8] == b'FCVMPAGE'
                            count = struct.unpack_from('<Q', decoded, 8)[0]
                            assert count == (len(values) + 1) * (8 << 20)
                            values.append(count); del decoded[:16]
                        assert not decoded
                    break
                time.sleep(0.01)
            assert worker.wait(timeout=3) == 0
            text = path.read_text()
            host_rows = [json.loads(line.removeprefix('FILECAT_HOST_FACTS ')) for line in text.splitlines() if line.startswith('FILECAT_HOST_FACTS ')]
            assert len(host_rows) == 1
            host = host_rows[0]
            assert host['pid'] == worker.pid and host['memoryBytes'] == 1 << 30 and host['balloonDevices'] == 0 and host['state'] == 0
            assert host['valid'] is True and host['started'] is True and host['parentExited'] is False and host['parentMonitorFailed'] is False
            assert all(host[key] == '' for key in ('guestError', 'stopError', 'startError', 'validationError'))
            wait_status = re.findall(r'^FILECAT_WAIT_STATUS=([0-9a-f]{16})$', text, re.M)
            if role == 6:
                assert complete and values[-1] == 256 << 20 and wait_status == ['0000000000000000'] and host['forcedStop'] is False
            elif role == 7:
                assert not complete and 512 << 20 <= values[-1] < 1 << 30 and wait_status == ['0000000000000009'] and host['forcedStop'] is False
            else:
                assert canceled_at is not None and time.monotonic() - canceled_at < 2
                assert host['cancellationRequested'] is True and host['cancellationSent'] is True
                if name == 'entropy-draining-10s': assert received >= 1 << 20
            deadline = time.monotonic() + 3
            while not absent(provider) and time.monotonic() < deadline: time.sleep(0.01)
            assert absent(provider) and absent(worker.pid)
            maximum = {kind: {field: max(row[field] for row in samples if row['kind'] == kind) for field in ('resident', 'footprint', 'lifetime_max_footprint')}
                       for kind in ('helper', 'provider', 'observer')}
            row = dict(case=name, helper=worker.pid, provider=provider, guest_ram_bytes=1 << 30, received_bytes=received, touched_bytes=values[-1] if values else 0,
                allocation_complete=complete, helper_reaped=True, provider_absent=True, maxima=maximum, native=host, samples=samples)
            rows.append(row)
            (output / (name + '.json')).write_text(json.dumps(row, indent=2) + '\n')
            print(json.dumps({key:value for key,value in row.items() if key not in ('samples','native')}), flush=True)
        finally:
            if worker.poll() is None: worker.kill(); worker.wait(timeout=5)
            worker.stdin.close(); worker.stdout.close()
            if cancel_write is not None: os.close(cancel_write)
idle()
summary = dict(experiment_passed=True, production_backend_qualified=False, cases=[{key:value for key,value in row.items() if key != 'samples'} for row in rows])
(output / 'facts.json').write_text(json.dumps(summary, indent=2) + '\n')
