"""Package only our small relocation-free AArch64 object and owned fixed module fixture."""
from pathlib import Path
import stat
import struct
import argparse
parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
parser.add_argument('--case', choices=('control', 'budget1g'), required=True)
args = parser.parse_args()
root, case = args.root.resolve(), args.case
obj = (root / f'input/init-{case}.o').read_bytes()
if len(obj) > 1024 * 1024 or obj[:6] != b'\x7fELF\x02\x01':
    raise SystemExit('Unexpected owned object size/encoding.')
header = struct.unpack_from('<16sHHIQQQIHHHHHH', obj)
if header[1:4] != (1, 183, 1) or header[11] != 64 or not 1 <= header[12] <= 32:
    raise SystemExit('Expected a small AArch64 relocatable object.')
sections = [struct.unpack_from('<IIQQQQIIQQ', obj, header[6] + i * 64) for i in range(header[12])]
names_section = sections[header[13]]
names = obj[names_section[4]:names_section[4]+names_section[5]]
text = None
for section in sections:
    if section[4] + section[5] > len(obj):
        raise SystemExit('Owned section bounds are invalid.')
    if section[1] in (4, 9) and section[5]:
        raise SystemExit('The owned object must have no unresolved relocations.')
    name = names[section[0]:].split(b'\0', 1)[0]
    if name == b'.text':
        if text is not None or not 0 < section[5] <= 65536:
            raise SystemExit('Unexpected owned text section.')
        text = obj[section[4]:section[4]+section[5]]
if text is None:
    raise SystemExit('The owned object has no text.')
size = 4096 + len(text)
ident = b'\x7fELF\x02\x01\x01' + b'\0' * 9
elf_header = struct.pack('<16sHHIQQQIHHHHHH', ident, 2, 183, 1, 0x401000, 64, 0, 0, 64, 56, 1, 0, 0, 0)
program_header = struct.pack('<IIQQQQQQ', 1, 5, 0, 0x400000, 0x400000, size, size, 4096)
elf = elf_header + program_header
elf += b'\0' * (4096 - len(elf)) + text
with (root / f'input/init-{case}').open('xb') as stream:
    stream.write(elf)
archive = bytearray()
inode = 0
def add(name, mode, payload=b'', major=0, minor=0):
    global inode
    inode += 1
    encoded = name.encode() + b'\0'
    fields = (inode, mode, 0, 0, 1, 0, len(payload), 0, 0, major, minor, len(encoded), 0)
    archive.extend(b'070701' + ''.join(f'{field:08x}' for field in fields).encode() + encoded)
    archive.extend(b'\0' * (-len(archive) % 4))
    archive.extend(payload)
    archive.extend(b'\0' * (-len(archive) % 4))
for directory in ('dev', 'proc', 'sys', 'sys/fs', 'sys/fs/cgroup'):
    add(directory, stat.S_IFDIR | 0o755)
add('dev/console', stat.S_IFCHR | 0o600, major=5, minor=1)
add('init', stat.S_IFREG | 0o755, elf)
add('virtio_mmio.module', stat.S_IFREG | 0o400, (root / 'input/virtio_mmio.module').read_bytes())
add('TRAILER!!!', 0)
with (root / f'input/initrd-{case}.cpio').open('xb') as stream:
    stream.write(archive)
print({'elf_bytes': len(elf), 'initrd_bytes': len(archive), 'text_bytes': len(text)})
