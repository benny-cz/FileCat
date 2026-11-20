"""Decode the documented EFI zboot container of this fixed, owned distro fixture."""
import gzip
import hashlib
import io
from pathlib import Path
import struct
import argparse
parser = argparse.ArgumentParser()
parser.add_argument('--input', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
raw = args.input.read_bytes()
if len(raw) > 32 * 1024 * 1024 or raw[:8] != b'MZ\0\0zimg' or raw[24:29] != b'gzip\0':
    raise SystemExit('Unexpected owned EFI zboot header.')
offset, size = struct.unpack_from('<II', raw, 8)
if offset < 64 or offset > len(raw) or size > len(raw) - offset:
    raise SystemExit('Invalid owned zboot payload bound.')
with gzip.GzipFile(fileobj=io.BytesIO(raw[offset:offset+size])) as stream:
    image = stream.read(128 * 1024 * 1024 + 1)
if len(image) > 128 * 1024 * 1024 or len(image) < 64 or image[56:60] != b'ARM\x64':
    raise SystemExit('Expected a bounded uncompressed ARM64 Linux Image.')
with args.output.open('xb') as stream:
    stream.write(image)
print({'bytes': len(image), 'sha256': hashlib.sha256(image).hexdigest(), 'payload_offset': offset, 'payload_bytes': size})
