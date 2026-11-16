"""Build and run the pinned, owned Mac VM experiment. No production registration or downloads."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--kernel', type=Path, required=True)
parser.add_argument('--module', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if platform.system() != 'Darwin' or os.getuid() == 0 or os.geteuid() == 0:
    raise SystemExit('Run this experiment as the ordinary user on the authorized Mac.')
if int(subprocess.check_output(['sysctl', '-n', 'hw.memsize'])) < 8 << 30:
    raise SystemExit('The large diagnostic experiment requires an idle Mac with at least 8 GiB RAM.')

def idle():
    busy = []
    for line in subprocess.check_output(['/bin/ps', '-axo', 'pid=,comm='], text=True).splitlines():
        fields = line.strip().split(None, 1)
        if len(fields) == 2:
            name = Path(fields[1]).name
            if name.startswith(('FileCat', 'testhost')) or name in ('dotnet', 'filecat'):
                busy.append(line.strip())
    if busy:
        raise SystemExit('Another tester is active; postpone.\n' + '\n'.join(busy))

def read_input(path, maximum, digest):
    with path.open('rb') as stream:
        data = stream.read(maximum + 1)
    if len(data) > maximum or hashlib.sha256(data).hexdigest() != digest:
        raise SystemExit('This experiment requires the exact reviewed prototype fixture inputs.')
    return data

idle()
kernel = read_input(args.kernel, 128 << 20, 'a2cecb9b6fdbd83e87bd43c8040de67300d5b8dde1fc50c34714faa272888f17')
module = read_input(args.module, 2 << 20, '97c9144f7c1c4e8d38a4cef9b83fec13214b4eb90bb49b438f546a1711279b26')
root = args.output.absolute()
root.mkdir(parents=True, exist_ok=False)
source = root / 'sources'
source.mkdir()
for path in Path(__file__).resolve().parent.iterdir():
    if path.is_file() and path.suffix in ('.py', '.S', '.m', '.plist'):
        shutil.copyfile(path, source / path.name)
inputs = root / 'input'
inputs.mkdir()
(inputs / 'kernel.Image').write_bytes(kernel)
(inputs / 'virtio_mmio.module').write_bytes(module)
compiler = '/Library/Developer/CommandLineTools/usr/bin/clang'
sdk = '/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
(root / 'compiler.txt').write_bytes(subprocess.check_output([compiler, '--version']))
lifetime = []

def run(name, command, seconds=45):
    with (root / (name + '.log')).open('xb') as output, (root / (name + '.stderr.txt')).open('xb') as error:
        process = subprocess.Popen(command, stdout=output, stderr=error, start_new_session=True)
        killed = False
        try:
            result = process.wait(timeout=seconds)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
                killed = True
            except ProcessLookupError:
                pass
            result = process.wait(timeout=5)
        lifetime.append({'name': name, 'pid': process.pid, 'exit': result, 'supervisor_killed': killed, 'reaped': True})
        (root / 'lifetime.json').write_text(json.dumps(lifetime, indent=2) + '\n')
        if result or killed:
            raise SystemExit('The owned experiment failed; retained diagnostics: ' + name)

for case, chunks, limit in (('control', 4, 64), ('budget1g', 144, 1024)):
    run(case + '-build', [compiler, '--target=aarch64-unknown-linux-gnu', f'-DFILECAT_ALLOCATION_CHUNKS={chunks}',
                        f'-DFILECAT_LIMIT_MIB={limit}', '-c', str(source / 'guest-memory-init.S'), '-o', str(inputs / f'init-{case}.o')])
    run(case + '-pack', [sys.executable, str(source / 'pack-initrd.py'), '--root', str(root), '--case', case])
binary = root / 'FileCat.ArchiveLinuxVmProbe'
run('host-build', [compiler, '-isysroot', sdk, '-mmacosx-version-min=13.0', '-fobjc-arc', '-Wall', '-Wextra', '-Werror',
                  '-framework', 'Foundation', '-framework', 'Virtualization', str(source / 'linux-vm-probe.m'), '-o', str(binary)])
run('signing', ['codesign', '--force', '--sign', '-', '--identifier', 'io.filecat.archive-linux-vm-probe',
                '--entitlements', str(source / 'virtualization-entitlements.plist'), str(binary)])
for case, ram in (('control', '512'), ('budget1g', '1536')):
    idle()
    run(case, [str(binary), str(inputs / 'kernel.Image'), str(inputs / f'initrd-{case}.cpio'), ram])
idle()
spec = importlib.util.spec_from_file_location('verify_filecat_vm', source / 'verify-probes.py')
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)
facts = verify.verify(root)
for result in facts['runs']:
    launch = next(entry for entry in lifetime if entry['name'] == result['case'])
    if launch['pid'] != result['host']['pid']:
        raise SystemExit('The observed host is not the exact owned child.')
(root / 'facts.json').write_text(json.dumps(facts, indent=2) + '\n')
inventory = [{'path': str(path.relative_to(root)), 'length': path.stat().st_size,
              'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in sorted(root.rglob('*')) if path.is_file()]
(root / 'files.json').write_text(json.dumps(inventory, indent=2) + '\n')
print(json.dumps({'experiment_passed': facts['experiment_passed'], 'production_backend_qualified': False,
                  'cases': len(facts['runs']), 'all_children_reaped': True, 'output': str(root)}))
