"""Inject an extra owned userspace process and require refusal before protocol readiness.

Development images only. Both injected processes are removed by the guest's normal poweroff.
The actual production worker/runtime bytes remain unchanged.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
parser.add_argument('--repo', type=Path, required=True)
args = parser.parse_args()
root, repo = args.root.resolve(strict=True), args.repo.resolve(strict=True)
output = root / 'process-inventory'
output.mkdir(exist_ok=False)
source = (repo / 'eng/validation/archive-writing/macos-vm/guest-memory-init.S').read_text()
marker = '    mov x0, #17\n    mov x1, #0\n    mov x2, #0\n    mov x3, #0\n    mov x4, #0\n    mov x8, #220\n'
assert source.count(marker) == 1

def idle():
    for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,comm='], text=True).splitlines():
        fields = row.strip().split(None, 1)
        if len(fields) == 2 and (Path(fields[1]).name.startswith(('FileCat', 'testhost')) or Path(fields[1]).name in ('dotnet', 'filecat')):
            raise SystemExit('Another tester is active; postpone.\n' + row)

rows = []
for uid in (0, 1000):
    idle()
    case = output / ('peer-uid-' + str(uid))
    (case / 'input').mkdir(parents=True)
    # Synchronize through an init-owned pipe, then close both ends before the real worker is forked. No extra
    # inherited descriptor may become the reason for refusal. The peer remains outside the worker cgroup.
    injection = '''    add x0, x19, #384
    mov x1, #0
    mov x8, #59
    svc #0
    cmp x0, #0
    b.lt fatal
''' + marker + '''    svc #0
    cmp x0, #0
    b.lt fatal
    b.eq injected_peer
    ldr w0, [x19, #388]
    mov x8, #57
    svc #0
    ldr w0, [x19, #384]
    add x1, x19, #400
    mov x2, #1
    mov x8, #63
    svc #0
    cmp x0, #1
    b.ne fatal
    ldr w0, [x19, #384]
    mov x8, #57
    svc #0
'''
    tail = '''
.p2align 2
injected_peer:
    ldr w0, [x19, #384]
    mov x8, #57
    svc #0
    mov x0, #0
    mov x1, #0
    mov x8, #159
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, #PEER_UID
    mov x8, #144
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, #PEER_UID
    mov x8, #146
    svc #0
    cmp x0, #0
    b.lt fatal
    ldr w0, [x19, #388]
    add x1, x19, #400
    mov x2, #1
    mov x8, #64
    svc #0
    cmp x0, #1
    b.ne fatal
    ldr w0, [x19, #388]
    mov x8, #57
    svc #0
    mov x9, #1
    str x9, [x19, #416]
    str xzr, [x19, #424]
injected_peer_wait:
    add x0, x19, #416
    mov x1, #0
    mov x8, #101
    svc #0
    b injected_peer_wait
'''.replace('PEER_UID', str(uid))
    injected = case / 'init-peer.S'
    injected.write_text(source.replace(marker, injection + marker) + tail)
    (case / 'input/virtio_mmio.module').write_bytes((root / 'input/virtio_mmio.module').read_bytes())
    subprocess.run(['/Library/Developer/CommandLineTools/usr/bin/clang', '--target=aarch64-unknown-linux-gnu',
        '-I' + str(repo / 'eng/validation/archive-writing/macos-vm'), '-DFILECAT_MANAGED_WORKER=1', '-DFILECAT_LIMIT_MIB=1024',
        '-DFILECAT_GUEST_EXECUTABLE="/worker/FileCat.ArchiveWorker"', '-DFILECAT_GUEST_ARGUMENT="--archive-linux-vm-worker"',
        '-c', str(injected), '-o', str(case / 'input/init-control.o')], check=True)
    subprocess.run(['python3', str(repo / 'eng/validation/archive-writing/macos-vm/pack-initrd.py'), '--root', str(case), '--case', 'control'], check=True)
    subprocess.run(['python3', str(repo / 'eng/validation/archive-writing/macos-vm-worker/pack-worker-initrd.py'), '--root', str(case),
        '--publish', str(root / 'guest-publish'), '--libraries', str(repo / 'libraries'), '--entry', 'FileCat.ArchiveWorker'], check=True)
    reader, writer = os.pipe()
    try:
        with (case / 'guest.log').open('xb') as diagnostics:
            process = subprocess.Popen([str(root / 'FileCat.ArchiveLinuxVmWorker'), str(root / 'input/kernel.Image'),
                str(case / 'input/initrd-worker.cpio'), '1024', str(os.getpid()), str(reader)], pass_fds=(reader,),
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=diagnostics)
            try:
                protocol, _ = process.communicate(timeout=15)
                assert process.returncode == 0 and protocol == b''
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=5)
    finally:
        os.close(reader)
        os.close(writer)
    log = (case / 'guest.log').read_text()
    assert 'The private guest contains an unexpected userspace process.' in log
    assert 'FILECAT_WAIT_STATUS=0000000000004100' in log and '"guestWorker":' not in log
    facts = [json.loads(line.removeprefix('FILECAT_HOST_FACTS ')) for line in log.splitlines() if line.startswith('FILECAT_HOST_FACTS ')]
    assert len(facts) == 1 and facts[0]['state'] == 0 and facts[0]['guestStopped'] is True and facts[0]['forcedStop'] is False
    rows.append(dict(peer_uid=uid, helper_pid=process.pid, helper_reaped=True, protocol_bytes=0, worker_exit=65))
idle()
facts = dict(component_checks_passed=True, production_backend_qualified=False, cases=rows)
(output / 'facts.json').write_text(json.dumps(facts, indent=2) + '\n')
print(json.dumps(facts))
