# E-ENV — execution environments and preflight facts (2026-09-30)

## E-ENV-00 — execution host

Physical PC: AMD Ryzen 9 5900X (12 cores / 24 threads), 64 GiB RAM; Windows 11 Pro **Insider Preview** build 26220.9568
(DisplayVersion 25H2), x64; C: and E: NTFS (not Dev Drives). The campaign shell runs **elevated**. Tools: .NET SDKs up to
10.0.401 (runtime 10.0.12), Python 3.13, gpg (Gpg4win), git 2.54, gh (repo, workflow scopes), Docker Desktop installed
(daemon not running), WSL Ubuntu-24.04 (stopped), Hyper-V (VMs "DevQA-Benny" and "DockerDesktopVM", off; not used),
VMware Workstation 26.0.0 (build 25388281) with `vmrun`. No Inno Setup on the host. Consequence: the host is a physical Windows 11 x64
machine but an Insider build, so it gives preliminary W64 evidence only (plan §4.2 asks for a serviced release).

## E-ENV-01 — Windows Sandbox unavailable

`WindowsSandbox.exe` failed three times with "Windows Sandbox failed to initialize. The file cannot be accessed by the
system. (0x80070780)": with host folders mapped from `C:\WINDOWS\Temp`, with host folders on `E:`, and with a plain
configuration (networking off, nothing mapped). The feature `Containers-DisposableClientVM` is enabled; `CmService` and
`vmcompute` run. Not pursued further; disposable Windows work uses snapshotted VMware VMs instead.

## E-ENV-02 — VMware VMs lent by the owner (snapshotted before use)

| VM (`.vmx`) | Guest | Access | Snapshot |
|---|---|---|---|
| `V:\Virtual Machines\Jamf\Windows 10 x64` (encrypted VM) | Windows 11 Pro **Insider Preview** 10.0.26300.8068 (25H2), x64, 92 GB free; .NET runtimes incl. 10.0.6; WebView2 154.0.4258.37; UAC on, admins elevate without prompting; SAC off; Defender on | `vmrun` guest operations as local administrator (commands run at High integrity) | `filecat-before` (2026-09-30), to be reverted after the campaign's use |
| `V:\Virtual Machines\Barebit\Ubuntu 64-bit` | Ubuntu **22.04.5 LTS** (jammy), kernel 6.8.0-138, GNOME (gdm) with the user logged in, 162 GB free; `libicu70`, `libwebkit2gtk-4.0-37`, `libsecret-1-0`, `fuse3` (no `libfuse2`); sshd active; **no network interface up** | `vmrun` guest operations as a sudo user | `filecat-before` (2026-09-30), to be reverted |

Neither guest is a GA release of the plan's target versions (GA Windows 11; Ubuntu 24.04 and 26.04), and neither is
"fresh". Evidence from them is preliminary.

## E-ENV-03 — CI installer compiler provenance

A01 (run 36722039034), Windows ARM64 job 109909331877, step "The ARM64 installer compiles": `choco install innosetup`
printed "InnoSetup v6.7.1 already installed", and `ISCC` reported "Compiler engine version: Inno Setup 6.7.1" from
`C:\Program Files (x86)\Inno Setup 6`. The version is therefore set by the hosted runner image, not by the repository.
Upstream's current stable release is Inno Setup 7.1.0 (2026-08-12, jrsoftware.org/isdl.php, read 2026-09-30); the
workflow hard-codes the `Inno Setup 6` path.

## E-ENV-04 — GitHub state (read-only checks)

HEAD `4f6b062` equal to `origin/main` at the start; no tags, releases, issues or pull requests; private vulnerability
reporting `{"enabled": false}`; repository public; secret scanning and push protection enabled, Dependabot security
updates disabled.
