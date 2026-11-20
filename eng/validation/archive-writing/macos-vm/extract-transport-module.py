import gzip
import hashlib
from pathlib import Path, PurePosixPath
import argparse
parser = argparse.ArgumentParser()
parser.add_argument('--input', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
with gzip.open(args.input, 'rb') as stream:
    data = stream.read(128 * 1024 * 1024 + 1)
if len(data) > 128 * 1024 * 1024:
    raise SystemExit('Owned distro initramfs exceeds the inspection bound.')
offset = 0
selected = []
for _ in range(10000):
    if offset + 110 > len(data) or data[offset:offset+6] not in (b'070701', b'070702'):
        raise SystemExit('Unsupported distro cpio framing.')
    fields = [int(data[offset+6+i*8:offset+14+i*8], 16) for i in range(13)]
    size, name_size = fields[6], fields[11]
    offset += 110
    if not 1 <= name_size <= 4096 or offset + name_size > len(data):
        raise SystemExit('Invalid distro cpio name bound.')
    name = data[offset:offset+name_size-1].decode('utf-8', 'strict')
    offset = (offset + name_size + 3) & ~3
    if size > len(data) - offset:
        raise SystemExit('Invalid distro cpio file bound.')
    body = data[offset:offset+size]
    if name == 'TRAILER!!!':
        break
    if PurePosixPath(name).name in ('virtio_mmio.ko', 'virtio_mmio.ko.zst', 'virtio_mmio.ko.xz', 'virtio_mmio.ko.gz'):
        if size > 2 * 1024 * 1024 or selected:
            raise SystemExit('Unexpected transport module count or size.')
        with args.output.open('xb') as stream:
            stream.write(body)
        selected.append({'name': name, 'bytes': size, 'sha256': hashlib.sha256(body).hexdigest(), 'compressed': not body.startswith(b'\x7fELF')})
    offset = (offset + size + 3) & ~3
else:
    raise SystemExit('Distro cpio entry bound exceeded.')
if len(selected) != 1:
    raise SystemExit('The owned transport module was not found.')
print(selected)
