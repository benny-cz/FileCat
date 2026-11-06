# Unregistered Mac VM memory experiment

These FileCat-owned tools test a candidate way to host the existing Linux archive worker. They currently run only
an owned freestanding Linux allocation probe. They do not parse archives, register a backend or qualify production
memory/confinement. The [validation report](../../../../docs/validation/archive-writing/MACOS_VIRTUAL_MACHINE_BOUNDARY_SPIKE.md)
records the precise evidence and open gates.

The ordinary Mac user runs a headless Virtualization.framework helper with its public virtualization entitlement.
The guest has no network, storage, memory balloon or host directory share. Guest PID 1 configures a cgroup and drops
the allocation child to UID/GID 1000, clears groups and sets no-new-privileges. That child cannot write the limit file.
The control completes 32 MiB below a 64 MiB limit. The larger case attempts 1152 MiB under a 1 GiB limit; the independent
verifier requires SIGKILL, one kernel OOM kill and an exact 1 GiB peak. Both guests must power off without forced stop.

The guest process ceiling and VM physical RAM are distinct. The control VM has 512 MiB RAM, and the larger VM has
1536 MiB. No whole-host/helper RSS bound, complete host/broker memory envelope or archive workload is qualified here.
Kernel execution is confined to the disposable guest; the parent Mac UID remains ordinary.

## Exact prototype inputs

This private experiment uses the official Alpine 3.24 aarch64 netboot kernel `6.18.60-0-virt` and its matching transport
module. No kernel/module is added to a FileCat application package. Production admission requires complete exact-source,
build, license/notice and corresponding-source review for the guest system as well as the FileCat worker. Do not treat
this prototype or a subprocess/VM boundary as a license waiver.

| Input | SHA-256 |
| --- | --- |
| Original `vmlinuz-virt` | `cd05fed9e617b5df93dc62ece9b78c422fd94e9b432a2f7c092714b139d5eef4` |
| Original `initramfs-virt` | `189402937646965374ef2df5d37f76467135179ae168c88175df2fe3e264a0b0` |
| Uncompressed ARM64 Image | `a2cecb9b6fdbd83e87bd43c8040de67300d5b8dde1fc50c34714faa272888f17` |
| Matching `virtio_mmio.ko` | `97c9144f7c1c4e8d38a4cef9b83fec13214b4eb90bb49b438f546a1711279b26` |

The official inputs are listed at [Alpine's netboot directory](https://dl-cdn.alpinelinux.org/alpine/v3.24/releases/aarch64/netboot/).
This directory is mutable: preserve the actual downloaded bytes and hashes; the runner rejects different prepared inputs.

## Reproduce on the authorized idle Mac

Use fresh owned paths. These preparation tools process only the fixed prototype distro fixture and write exclusively
created outputs; they are not general archive extractors or production decoders:

```sh
python3 eng/validation/archive-writing/macos-vm/unwrap-kernel.py \
  --input /absolute/vmlinuz-virt --output /absolute/new/kernel.raw.Image
python3 eng/validation/archive-writing/macos-vm/extract-transport-module.py \
  --input /absolute/initramfs-virt --output /absolute/new/virtio_mmio.module
python3 eng/validation/archive-writing/macos-vm/run-probes.py \
  --kernel /absolute/new/kernel.raw.Image --module /absolute/new/virtio_mmio.module \
  --output /absolute/new/evidence-directory
```

The runner uses CLT clang and MacOSX27.0 SDK paths explicitly; it changes no global developer setting. Its large test
requires at least 8 GiB machine RAM and an idle FileCat/dotnet/test-host census, including between cases. It snapshots
all eight source files, builds the relocation-free AArch64 ELF/initramfs itself, ad-hoc signs only its own helper,
records child lifetimes, reaps children and checks kernel/host outcomes. Logs remain available on failure. A successful
report explicitly has `production_backend_qualified: false`.

The two preparers bound expanded fixture bytes and select only the transport module. The ELF packer accepts only a
small owned AArch64 object without unresolved relocations. The supervisor deadline is a backstop; normal successful
VMs must shut down themselves. Full adversarial startup, cancellation, parent-death and all resource/lifetime behavior
remain required before a real backend can use this mechanism.
