"""Assemble only task-owned app files and selected private Alpine library fixtures; no extraction to the host."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import tarfile

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
parser.add_argument('--publish', type=Path, required=True)
parser.add_argument('--libraries', type=Path, required=True)
parser.add_argument('--entry', choices=('FileCat.ArchiveVmProbe', 'FileCat.ArchiveWorker'), default='FileCat.ArchiveVmProbe')
args = parser.parse_args()
root = args.root.resolve()
entries = {}
inventory = []
pins = {
    'alpine-minirootfs-3.24.3-aarch64.tar.gz': '4d3f80afaebd447c5591a63a73bde380a149fe8b2d85488cafabb2ff97551f50',
    'libgcc-15.2.0-r5.apk': '369aaa6e9d099a737bad6dd3e6c2fe7bb1547ca26d22b94ee0411228f709b403',
    'libstdc++-15.2.0-r5.apk': '2302e766d4e4926038ec166ecb85837ee884576115236ddb565e3a5fca4a11d7',
}

def add(name, mode, data=b'', major=0, minor=0):
    if len(data) > 128 << 20 or name in entries or len(name) > 4096:
        raise SystemExit('Duplicate/oversized fixed guest fixture.')
    entries[name] = (mode, data, major, minor)

for directory in ('.', 'dev', 'proc', 'sys', 'sys/fs', 'sys/fs/cgroup', 'lib', 'usr', 'usr/lib', 'worker', 'tmp', 'sealed'):
    add(directory, stat.S_IFDIR | 0o755)
add('sealed/not-runtime', stat.S_IFREG | 0o400, b'Owned read-denial fixture.\n')
add('sealed/linked-runtime', stat.S_IFLNK | 0o777, b'../worker')
add('sealed/control-link', stat.S_IFLNK | 0o777, b'../sys/fs/cgroup/filecat/memory.max')
add('dev/console', stat.S_IFCHR | 0o600, major=5, minor=1)
add('dev/hvc1', stat.S_IFCHR | 0o666, major=229, minor=1)
add('dev/hvc2', stat.S_IFCHR | 0o444, major=229, minor=2)
add('dev/null', stat.S_IFCHR | 0o666, major=1, minor=3)
add('dev/urandom', stat.S_IFCHR | 0o444, major=1, minor=9)
add('dev/random', stat.S_IFCHR | 0o444, major=1, minor=8)
add('init', stat.S_IFREG | 0o555, (root / 'input/init-control').read_bytes())
add('virtio_mmio.module', stat.S_IFREG | 0o400, (root / 'input/virtio_mmio.module').read_bytes())
archives = sorted(args.libraries.glob('*.tar.gz')) + sorted(args.libraries.glob('*.apk'))
if {archive.name for archive in archives} != set(pins):
    raise SystemExit('Use exactly the three pinned private library fixtures.')
for archive in archives:
    if archive.is_symlink() or archive.stat().st_size > 32 << 20:
        raise SystemExit('Oversized fixed library fixture.')
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != pins[archive.name]:
        raise SystemExit('Private library fixture differs from its reviewed experiment identity.')
    inventory.append({'name': archive.name, 'bytes': archive.stat().st_size, 'sha256': digest})
    expanded = 0
    with tarfile.open(archive, 'r:gz', ignore_zeros=True) as tar:
        for count, member in enumerate(tar):
            if count >= 10000 or member.size < 0 or member.size > 64 << 20:
                raise SystemExit('Library fixture bounds exceeded.')
            expanded += member.size
            if expanded > 256 << 20:
                raise SystemExit('Library fixture expansion exceeded.')
            name = member.name.removeprefix('./')
            path = PurePosixPath(name)
            if path.is_absolute() or '..' in path.parts:
                raise SystemExit('Unexpected library fixture path.')
            if not (name.startswith('lib/') or name.startswith('usr/lib/')) or '.so' not in path.name:
                continue
            if archive.name.startswith('alpine-minirootfs-') and name not in (
                'lib/ld-musl-aarch64.so.1', 'lib/libc.musl-aarch64.so.1', 'usr/lib/libz.so.1',
                'usr/lib/libz.so.1.3.2', 'usr/lib/libssl.so.3', 'usr/lib/libcrypto.so.3'):
                continue
            if member.isfile():
                add(name, stat.S_IFREG | 0o555, tar.extractfile(member).read())
            elif member.issym():
                target = PurePosixPath(member.linkname)
                if target.is_absolute() or '..' in target.parts or len(member.linkname) > 4096:
                    raise SystemExit('Unexpected library fixture link.')
                add(name, stat.S_IFLNK | 0o777, member.linkname.encode())
            else:
                raise SystemExit('Unexpected library fixture type.')
for source in sorted(args.publish.iterdir()):
    if source.is_symlink() or not source.is_file() or source.stat().st_size > 128 << 20:
        raise SystemExit('Unexpected published worker fixture.')
    add('worker/' + source.name, stat.S_IFREG | (0o555 if source.name == args.entry or source.suffix == '.so' else 0o444), source.read_bytes())
add('TRAILER!!!', 0)
archive = bytearray()
for inode, (name, (mode, payload, major, minor)) in enumerate(entries.items(), 1):
    encoded = name.encode() + b'\0'
    fields = (inode, mode, 0, 0, 1, 0, len(payload), 0, 0, major, minor, len(encoded), 0)
    archive.extend(b'070701' + ''.join(f'{field:08x}' for field in fields).encode() + encoded)
    archive.extend(b'\0' * (-len(archive) % 4)); archive.extend(payload); archive.extend(b'\0' * (-len(archive) % 4))
    if len(archive) > 384 << 20:
        raise SystemExit('Fixed guest initramfs exceeds the experiment bound.')
with (root / 'input/initrd-worker.cpio').open('xb') as stream:
    stream.write(archive)
(root / 'library-inputs.json').write_text(json.dumps(inventory, indent=2) + '\n')
print(json.dumps({'initrd_bytes': len(archive), 'entries': len(entries)}))
