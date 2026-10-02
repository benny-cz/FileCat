# E-ENV-07 — fresh Ubuntu desktop matrix provisioning

Preliminary environment setup for ENV-04, I04 and V19. No candidate exists; creating boot media does not qualify an OS
or a FileCat package. Owner authorizes updates/reinstallation of the lent Ubuntu VM and says its contents need not be
preserved because a rollback snapshot already exists (2026-10-02).

## Controlled target and media

- VMX: `V:\Virtual Machines\Barebit\Ubuntu 64-bit\Ubuntu 64-bit.vmx`; VMware UUID
  `c13a4d56-88aa-1159-57b9-9cea95bb06e9`, vendor `VMware, Inc.`, 8 vCPUs / 8 GiB RAM.
- Sole non-removable hard disk: 214748364800 bytes, VMware SCSI serial
  `6000c29e95aba45c6f0eb8b133afed62`, udev `ID_SERIAL=36000c29e95aba45c6f0eb8b133afed62`.
  `disk.EnableUUID=TRUE` enabled while powered off. Original snapshots retained. An additional pre-setup snapshot
  `filecat-before-os-matrix-20261002` completed successfully; no further preservation of the old guest is required.
- Original guest was Ubuntu 22.04.5. Its bridged network obtained an unreachable 172.24 address and failed DNS.
  Adapter changed to VMware NAT while powered off; guest obtained `192.168.58.129` and HTTPS access works.
- Canonical SHA256SUMS signatures verified in a private evidence keyring against CD-image signing fingerprint
  `843938DF228D22F7B3742BC0D94AA3F0EFE21092`. Both original ISO hashes verified independently on host and guest.

| Input from Canonical | SHA-256 |
|---|---|
| [Ubuntu 24.04.5.1 desktop amd64](https://releases.ubuntu.com/24.04/) | `4da4a0c9035da8e68a59a838674f403f0a54472c78a83b4fb7f78d03588f85a7` |
| [Ubuntu 26.04.1 desktop amd64](https://releases.ubuntu.com/26.04/) | `601e30fbf5d97759367c632e2c33630665039b7e2158fd068403da3ccf1bda1f` |

`eng/validation/make-ubuntu-validation-media.py` uses xorriso 1.5.4 on the original guest to replay the official boot
metadata, add an autoinstall configuration and enable its kernel argument. It checks the original ISO hash and refuses
an existing output directory. Full desktop source `ubuntu-desktop` is read from each ISO's actual source catalog;
24.04 uses a list and 26.04 uses the version-2 catalog. No assumed source ID substitutes for the catalog check.

The installer early command requires the exact VM UUID/vendor, exactly one non-removable hard disk, its raw serial,
size and udev ID_SERIAL. Storage selection uses that exact udev ID_SERIAL. The guard passed on the target and rejected
four negative controls: wrong UUID, serial, size and udev identity. Subiquity's match uses
[udev ID_SERIAL](https://canonical-subiquity.readthedocs-hosted.com/en/latest/reference/autoinstall-reference.html),
which differs from the raw serial reported by lsblk here; the earlier unbooted media was superseded before installation.

The private configuration supplies a test account, GNOME automatic login, OpenSSH and VMware desktop tools, then powers
off after installation so boot media can be disconnected. It contains a password hash and stays in ignored evidence
storage. The explicit owner-authorized disposable OS reinstall is the exception to the system-disk fixture exclusion;
these media must never be used on a different machine.

| Controlled media, attempt r3 | SHA-256 |
|---|---|
| 24.04 remastered ISO | `97ff6bce2c2b627766abfe440e3c40b8cb2bc4755165172b66005cad4bce1c84` |
| 24.04 configuration | `9bb8f74a81f580156a3b4048227c0395ade93665a6d023690435b4485d3d16e7` |
| 26.04 remastered ISO | `499c18b06ae7281ab7ac73960fd2daa2a22b4df58eef608eec889c6d8fafe162` |
| 26.04 configuration | `ba0d0d0d04484a9a7129cde30b4ed3edc24a73594e2b900706c0db9f0c8fb160` |
| Media generator, both r3 builds | `8f33e65d7a3ec4a75bc2a7d424aa46148cecb8a7513ce5ad3dd196b8fbee4e8c` |

Raw signatures, manifests, build logs, identity controls and media are under
`artifacts/release-evidence/linux-os-matrix-20261002/`. Authenticated guest operations supplied the SSH host key before
SSH transfers; host copies must match the remastered hashes before attachment. Failed setup attempts were safe:
Python 3.10 lacked file_digest, extracted grub.cfg was read-only, and 26.04's catalog format differed. All were corrected
in the generator. No install or product test ran during those failed attempts.

## Current execution status

Both corrected media copied to host with matching hashes. Ubuntu 24.04.5 installed, powered off at completion, then
booted with media disconnected. Actual GNOME Wayland session and bound VM/disk verified. Clean powered-off snapshot
filecat-clean-ubuntu2404-20261002 taken before FileCat/testing dependencies; no SDK installed. Preliminary package/native
results and setup changes in E-V19-P2/E-X02. After 24.04 raw evidence was transferred and hash-verified on the host,
the guest was gracefully powered off and identity-checked 26.04 media attached/booted at 11:50:59 UTC. Native Linux
console shows Ubuntu 26.04.1 LTS installer copying files at 12:01 UTC. Installation completed and powered off;
boot media disconnected and installed OS booted. Actual Ubuntu 26.04.1, kernel 7.0.0-38-generic, GNOME 50.1 Wayland
session and exact VM/disk identities verified. Powered-off snapshot `filecat-clean-ubuntu2604-20261002` taken at
13:43:32 UTC before FileCat/SDK/test dependencies; backing disk then `Ubuntu 64-bit-000005.vmdk`. Private installer
archive retained on host with SHA-256 `8f20d561edf2ecd8d0f56dbbce318ad8a95bdb6eb62b68d9e0c7d588048ccb5e`.
The unmodified dev.526 Debian install fails on ICU alternatives (E-I04); remediation and package/native matrix continue.
Read-only authenticated RFB capture through the previously configured loopback-only VM console avoids unavailable
live-installer guest operations. Screenshot SHA-256 `784f3a2754e1afb6ad49fdf2250f8f5804627d7e0522efc8b55a3b44e8ba31c7`;
private capture helper SHA-256 `0749050319a5faa094aea66506509c36c54d0926557d6676386b239e713b2ecf`. No final OS qualification.

Manual CI [36994087185](https://github.com/benny-cz/FileCat/actions/runs/36994087185), source
`d14199b8b2bc1f6170ceab2b91c3212e80909d0c`, passed all four test lanes and Linux/macOS packaging. Windows tag-only
packaging skipped. This run and successor 36999624175 at ecf5349 produced development artifacts without a release tag
or publication; exact hashes and fresh-guest results in E-V19-P2. Signing, final candidate reruns and human support
decisions remain open.
