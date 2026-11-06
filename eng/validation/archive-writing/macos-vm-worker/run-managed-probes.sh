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
        if source.is_file() and source.suffix in ('.py', '.S', '.m', '.c', '.plist', '.cs', '.csproj', '.inc', '.sh', '.md'):
            shutil.copyfile(source, target / source.name)
PY
mkdir "$results/sources/production"
cp src/FileCat.Core/Archives/ArchiveWorkerTransport.cs "$results/sources/production/ArchiveWorkerTransport.cs"
cp src/FileCat.Core/Archives/Linux/LinuxArchiveGuestIsolation.cs src/FileCat.Core/Archives/Linux/LinuxArchiveGuestTransport.cs src/FileCat.Core/Archives/Linux/LinuxArchiveProcessPolicy.cs src/FileCat.Core/Archives/Linux/LinuxArchiveIsolation.cs src/FileCat.Core/Archives/Linux/LinuxArchiveNative.cs "$results/sources/production/"
cp src/FileCat.Core/FileSystem/LinuxOpenFlags.cs src/FileCat.Core/FileSystem/LinuxPosixAcl.cs src/FileCat.Core/Archives/Unix/UnixArchiveNative.cs src/FileCat.Core/Archives/Unix/UnixArchiveVolume.cs "$results/sources/production/"
cp src/FileCat.Core/Archives/ArchiveCapturedInputs.cs src/FileCat.Core/Archives/ArchiveWriteContracts.cs src/FileCat.Core/Archives/ArchiveTarTransformPlan.cs src/FileCat.Core/Archives/ArchiveProxyStreams.cs src/FileCat.Core/Archives/ArchiveRetainedTarPipeline.cs src/FileCat.Core/Archives/ArchiveJournaledTarPreparation.cs "$results/sources/production/"
cp src/FileCat.Core/Archives/ArchiveCapturedDirectory.cs src/FileCat.Core/Archives/ArchiveTransformInputs.cs src/FileCat.Core/Archives/ArchiveTransformWire.cs src/FileCat.Core/Archives/ArchiveCreationOrigin.cs src/FileCat.Core/Archives/ArchiveUpdateOrigin.cs src/FileCat.Core/Archives/ArchiveNewFilePublication.cs src/FileCat.Core/Archives/ArchiveContentUpdate.cs "$results/sources/production/"
cp src/FileCat.Core/Archives/ArchiveCapturedSelection.cs src/FileCat.Core/Archives/ArchiveOwnedSourceFile.cs src/FileCat.Core/Archives/Unix/UnixArchiveSelectionTree.cs src/FileCat.Core/Archives/Unix/UnixArchiveNative.Enumeration.cs src/FileCat.Core/Archives/Unix/UnixArchiveDirectory.Capture.cs src/FileCat.Core/FileSystem/UnixFiles.Child.cs "$results/sources/production/"
cp eng/validation/archive-writing/verify-recursive-selection-oracles.py "$results/sources/"
cp src/FileCat.ArchiveWorker/Program.cs "$results/sources/production/ArchiveWorker.Program.cs"
cp src/FileCat.ArchiveWorker/FileCat.ArchiveWorker.csproj src/FileCat.ArchiveWorker/packages.lock.json "$results/sources/production/"
cp src/FileCat.Core/FileCat.Core.csproj src/ThirdParty/SharpCompress/FileCat.SharpCompress.csproj "$results/sources/production/"
cp src/FileCat.Core/packages.lock.json "$results/sources/production/Core.packages.lock.json"
cp src/ThirdParty/SharpCompress/packages.lock.json "$results/sources/production/SharpCompress.packages.lock.json"
mkdir "$results/sources/writing"
cp src/FileCat.Archives/Writing/*.cs src/FileCat.Archives/TarHeaderGuard.cs src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj src/FileCat.Archives.Writing/packages.lock.json "$results/sources/writing/"
project=eng/validation/archive-writing/macos-vm-worker/FileCat.ArchiveVmProbe.csproj
"$dotnet" --info > "$results/dotnet.txt"
"$dotnet" publish src/FileCat.ArchiveWorker/FileCat.ArchiveWorker.csproj -c Release -r linux-musl-arm64 --self-contained true -o "$results/guest-publish" -p:FileCatUpdateDependencyLocks=true -p:RuntimeIdentifiers=linux-musl-arm64 -p:RuntimeFrameworkVersion=10.0.12 -p:NuGetAudit=false --disable-build-servers -v minimal > "$results/guest-build.log" 2>&1
"$dotnet" publish "$project" -c Release -r linux-musl-arm64 --self-contained true -o "$results/guest-probe-publish" -p:FileCatUpdateDependencyLocks=true -p:RuntimeIdentifiers=linux-musl-arm64 -p:RuntimeFrameworkVersion=10.0.12 -p:NuGetAudit=false --disable-build-servers -v minimal > "$results/guest-probe-build.log" 2>&1
"$dotnet" publish "$project" -c Release -r osx-arm64 --self-contained false -o "$results/host-publish" -p:FileCatUpdateDependencyLocks=true -p:RuntimeIdentifiers=osx-arm64 -p:NuGetAudit=false --disable-build-servers -v minimal > "$results/host-build.log" 2>&1
compiler=/Library/Developer/CommandLineTools/usr/bin/clang
"$compiler" --version > "$results/compiler.txt"
"$compiler" -isysroot /Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk -Wall -Wextra -Werror eng/validation/archive-writing/macos-vm-worker/rusage-abi.c -lproc -o "$results/rusage-abi"
"$results/rusage-abi" > "$results/rusage-abi.json"
mkdir "$results/probe-image" "$results/probe-image/input"
cp "$module" "$results/probe-image/input/virtio_mmio.module"
"$compiler" --target=aarch64-unknown-linux-gnu -DFILECAT_MANAGED_WORKER=1 -DFILECAT_LIMIT_MIB=1024 -c eng/validation/archive-writing/macos-vm/guest-memory-init.S -o "$results/probe-image/input/init-control.o"
python3 eng/validation/archive-writing/macos-vm/pack-initrd.py --root "$results/probe-image" --case control > "$results/pack-probe-init.log"
python3 eng/validation/archive-writing/macos-vm-worker/pack-worker-initrd.py --root "$results/probe-image" --publish "$results/guest-probe-publish" --libraries "$libraries" > "$results/pack-probe-worker.log"
"$compiler" --target=aarch64-unknown-linux-gnu -DFILECAT_MANAGED_WORKER=1 -DFILECAT_LIMIT_MIB=1024 '-DFILECAT_GUEST_EXECUTABLE="/worker/FileCat.ArchiveWorker"' '-DFILECAT_GUEST_ARGUMENT="--archive-linux-vm-worker"' -c eng/validation/archive-writing/macos-vm/guest-memory-init.S -o "$results/input/init-control.o"
python3 eng/validation/archive-writing/macos-vm/pack-initrd.py --root "$results" --case control > "$results/pack-init.log"
python3 eng/validation/archive-writing/macos-vm-worker/pack-worker-initrd.py --root "$results" --publish "$results/guest-publish" --libraries "$libraries" --entry FileCat.ArchiveWorker > "$results/pack-worker.log"
"$compiler" -isysroot /Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk -mmacosx-version-min=13.0 -fobjc-arc -Wall -Wextra -Werror -framework Foundation -framework CoreFoundation -framework Virtualization eng/validation/archive-writing/macos-vm-worker/linux-vm-worker.m -o "$results/FileCat.ArchiveLinuxVmWorker"
codesign --force --sign - --identifier io.filecat.archive-linux-vm-worker --entitlements eng/validation/archive-writing/macos-vm/virtualization-entitlements.plist "$results/FileCat.ArchiveLinuxVmWorker" > "$results/signing.log" 2>&1
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/results" > "$results/host-run.log" 2>&1
python3 eng/validation/archive-writing/macos-vm-worker/verify-worker-run.py --root "$results" > "$results/oracle.log"
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" captured-host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/native-capture" > "$results/native-capture.log" 2>&1
python3 eng/validation/archive-writing/verify-captured-input-oracles.py "$results/native-capture" > "$results/native-capture-oracle.log"
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" directory-host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/directory-workflow" > "$results/directory-workflow.log" 2>&1
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" recursive-host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/recursive-selection" > "$results/recursive-selection.log" 2>&1
python3 eng/validation/archive-writing/verify-recursive-selection-oracles.py "$results/recursive-selection" > "$results/recursive-selection-oracle.log"
python3 eng/validation/archive-writing/verify-directory-workflow-oracles.py "$results/directory-workflow" > "$results/directory-workflow-oracle.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-parent-death.py --native "$results/FileCat.ArchiveLinuxVmWorker" --kernel "$results/input/kernel.Image" --initrd "$results/probe-image/input/initrd-worker.cpio" --output "$results/parent-death" > "$results/parent-death.log"
"$dotnet" "$results/host-publish/FileCat.ArchiveVmProbe.dll" cancel-host "$results/FileCat.ArchiveLinuxVmWorker" "$results/input/kernel.Image" "$results/input/initrd-worker.cpio" "$results/probe-image/input/initrd-worker.cpio" "$results/cancellation" > "$results/cancellation.log" 2>&1
python3 eng/validation/archive-writing/macos-vm-worker/verify-cancellation.py --root "$results" > "$results/cancellation-oracle.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-helper-death.py --native "$results/FileCat.ArchiveLinuxVmWorker" --kernel "$results/input/kernel.Image" --initrd "$results/probe-image/input/initrd-worker.cpio" --output "$results/helper-death" > "$results/helper-death.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-memory-envelope.py --root "$results" > "$results/memory-envelope.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-memory-results.py --root "$results" > "$results/memory-results.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-production-worker.py --root "$results" > "$results/production-policy.log"
python3 eng/validation/archive-writing/macos-vm-worker/verify-guest-process-inventory.py --root "$results" --repo "$repo" > "$results/process-inventory.log"
