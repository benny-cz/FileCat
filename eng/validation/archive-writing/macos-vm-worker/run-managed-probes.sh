#!/bin/bash
# Development only: use a task-owned source snapshot, never a shared checkout. No downloads/install/global changes.
set -euo pipefail
[ "$#" -eq 6 ] || { echo 'usage: run-managed-probes.sh ABS_PRIVATE_REPO ABS_KERNEL ABS_MODULE ABS_LIBRARIES ABS_DOTNET ABS_NEW_OUTPUT' >&2; exit 64; }
[ "$(uname -s)" = Darwin ] && [ "$(id -u)" -ne 0 ] || exit 65
repo=$1; kernel=$2; module=$3; libraries=$4; dotnet=$5; results=$6
for path in "$repo" "$kernel" "$module" "$libraries" "$dotnet" "$results"; do
    case "$path" in /*) ;; *) echo 'Absolute fixture paths are required.' >&2; exit 64 ;; esac
done
[ ! -e "$results" ] && [ -x "$dotnet" ] && [ -n "${FILECAT_ARCHIVE_ZSTD_ORACLE_LIBRARY:-}" ] || exit 65
export DOTNET_ROOT="$(dirname "$dotnet")" PATH="$(dirname "$dotnet"):/usr/bin:/bin:/usr/sbin:/sbin"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1
python3 - "$kernel" "$module" <<'PY'
import hashlib, os, subprocess, sys
from pathlib import Path
for row in subprocess.check_output(['/bin/ps', '-axo', 'pid=,comm='], text=True).splitlines():
    fields = row.strip().split(None, 1)
    if len(fields) == 2:
        name = Path(fields[1]).name
        if name.startswith(('FileCat', 'testhost')) or name in ('dotnet', 'filecat'):
            raise SystemExit('Another tester is active; postpone.\n' + row)
for name, maximum, expected in [(sys.argv[1], 128 << 20, 'a2cecb9b6fdbd83e87bd43c8040de67300d5b8dde1fc50c34714faa272888f17'),
                                (sys.argv[2], 2 << 20, '97c9144f7c1c4e8d38a4cef9b83fec13214b4eb90bb49b438f546a1711279b26')]:
    path = Path(name)
    assert not path.is_symlink() and path.stat().st_size <= maximum
    assert hashlib.sha256(path.read_bytes()).hexdigest() == expected
assert int(subprocess.check_output(['/usr/sbin/sysctl', '-n', 'hw.memsize'])) >= 8 << 30
PY
mkdir "$results" "$results/input" "$results/sources"
cd "$repo"
cp "$kernel" "$results/input/kernel.Image"
cp "$module" "$results/input/virtio_mmio.module"
python3 - "$repo" "$results/sources" <<'PY'
from pathlib import Path
import shutil, sys
root, output = map(Path, sys.argv[1:])
for name in ('macos-vm', 'macos-vm-worker'):
    folder = root / 'eng/validation/archive-writing' / name
    target = output / name
    target.mkdir()
    for source in folder.iterdir():
        if source.is_file() and source.suffix in ('.py', '.S', '.m', '.plist', '.cs', '.csproj', '.inc', '.sh', '.md'):
            shutil.copyfile(source, target / source.name)
PY
mkdir "$results/sources/production"
cp src/FileCat.Core/Archives/ArchiveWorkerTransport.cs "$results/sources/production/ArchiveWorkerTransport.cs"
project=eng/validation/archive-writing/macos-vm-worker/FileCat.ArchiveVmProbe.csproj
"$dotnet" --info > "$results/dotnet.txt"
"$dotnet" publish "$project" -c Release -r linux-musl-arm64 --self-contained true -o "$results/guest-publish" -p:FileCatUpdateDependencyLocks=true -p:RuntimeIdentifiers=linux-musl-arm64 -p:RuntimeFrameworkVersion=10.0.12 -p:NuGetAudit=false --disable-build-servers -v minimal > "$results/guest-build.log" 2>&1
"$dotnet" publish "$project" -c Release -r osx-arm64 --self-contained false -o "$results/host-publish" -p:FileCatUpdateDependencyLocks=true -p:RuntimeIdentifiers=osx-arm64 -p:NuGetAudit=false --disable-build-servers -v minimal > "$results/host-build.log" 2>&1
compiler=/Library/Developer/CommandLineTools/usr/bin/clang
"$compiler" --version > "$results/compiler.txt"
"$compiler" --target=aarch64-unknown-linux-gnu -DFILECAT_MANAGED_WORKER=1 -DFILECAT_LIMIT_MIB=1024 -c eng/validation/archive-writing/macos-vm/guest-memory-init.S -o "$results/input/init-control.o"
python3 eng/validation/archive-writing/macos-vm/pack-initrd.py --root "$results" --case control > "$results/pack-init.log"
python3 eng/validation/archive-writing/macos-vm-worker/pack-worker-initrd.py --root "$results" --publish "$results/guest-publish" --libraries "$libraries" > "$results/pack-worker.log"
"$compiler" -isysroot /Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk -mmacosx-version-min=13.0 -fobjc-arc -Wall -Wextra -Werror -framework Foundation -framework CoreFoundation -framework Virtualization eng/validation/archive-writing/macos-vm-worker/linux-vm-worker.m -o "$results/FileCat.ArchiveLinuxVmWorker"
codesign --force --sign - --identifier io.filecat.archive-linux-vm-worker --entitlements eng/validation/archive-writing/macos-vm/virtualization-entitlements.plist "$results/FileCat.ArchiveLinuxVmWorker" > "$results/signing.log" 2>&1
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/results" > "$results/host-run.log" 2>&1
python3 eng/validation/archive-writing/macos-vm-worker/verify-worker-run.py --root "$results" > "$results/oracle.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-parent-death.py --native "$results/FileCat.ArchiveLinuxVmWorker" --kernel "$results/input/kernel.Image" --initrd "$results/input/initrd-worker.cpio" --output "$results/parent-death" > "$results/parent-death.log"
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" cancel-host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/cancellation" > "$results/cancellation.log" 2>&1
python3 eng/validation/archive-writing/macos-vm-worker/verify-cancellation.py --root "$results" > "$results/cancellation-oracle.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-helper-death.py --native "$results/FileCat.ArchiveLinuxVmWorker" --kernel "$results/input/kernel.Image" --initrd "$results/input/initrd-worker.cpio" --output "$results/helper-death" > "$results/helper-death.log"
