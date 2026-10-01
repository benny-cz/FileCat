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
| `V:\Virtual Machines\Jamf\Windows 10 x64` (encrypted VM) | Windows 11 Pro **Insider Preview** 10.0.26300.8068 (25H2), x64, 92 GB free; .NET runtimes incl. 10.0.6; WebView2 154.0.4258.37; UAC on, admins elevate without prompting; SAC off; Defender on | `vmrun` guest operations as local administrator (commands run at High integrity) | **None was taken** (found 2026-10-01: the VM lists only the owner's three snapshots, and its snapshot database was last written in June). The owner had started the VM by reverting it to its own newest snapshot, `updated #38` (VMware log, 2026-09-30 18:03 UTC), so that snapshot is the state it was lent in; the VM was reverted to it on 2026-10-01 (E-ENV-05) |
| `V:\Virtual Machines\Barebit\Ubuntu 64-bit` | Ubuntu **22.04.5 LTS** (jammy), kernel 6.8.0-138, GNOME (gdm) with the user logged in, 162 GB free; `libicu70`, `libwebkit2gtk-4.0-37`, `libsecret-1-0`, `fuse3` (no `libfuse2`); sshd active; no network interface up when lent (the campaign connected its adapter to VMware NAT, host-private: `192.168.58.129/24`) | `vmrun` guest operations as a sudo user | `filecat-before` (2026-09-30), to be reverted |

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

## E-ENV-05 — later additions (2026-09-30)

- **Windows VM, unelevated runs:** `vmrun runProgramInGuest -interactive` starts a program in the logged-on session as
  the signed-in administrator **without elevation** (the test logs record `elevated False`); plain guest operations run
  elevated. Elevated work in the session (the I17 consent harness) uses a one-shot scheduled task with highest rights.
- **The owner's Mac (ENV-01):** MacBook Pro, Apple M1 (8 cores), macOS 26.6.2 (build 25G83), Gatekeeper assessments
  enabled; reached over SSH on the LAN with a key the owner designated for this campaign. It is the owner's personal
  machine, not a clean install. Campaign files stay under `~/fc-campaign`; a user-local .NET 10.0.12 runtime was put in
  `~/.dotnet` (dotnet-install) and pyftpdlib/pyOpenSSL in the user's Python site packages for the FTP test server. The
  login keychain cannot be unlocked over SSH without a prompt (OSStatus -25293).
- **Test servers on the Ubuntu VM (ENV-06, partial):** OpenSSH (SFTP subsystem) on 22; vsftpd with FTP and explicit
  FTPS on 21 and a second vsftpd instance with implicit FTPS on 990; Samba share `fcshare` on 445. Throwaway account
  `fctest`, password authentication; one self-signed TLS certificate for both FTPS services (SAN `192.168.58.129`,
  SHA-256 fingerprint `46:1E:A1:91:61:F7:67:EF:61:A8:EF:6F:86:DC:A2:9C:B6:F9:F5:E8:63:90:57:3D:F3:2B:F6:84:76:E6:C3:E9`).
  Reachable only on the host-private NAT network; the VM's firewall is inactive. A first vsftpd configuration with
  `ssl_tlsv1_1`/`ssl_tlsv1_2` keys did not start (not investigated further); TLS is enabled with `ssl_tlsv1=YES` and
  SSLv2/SSLv3 off, and the TLS versions actually negotiated are still to be recorded when the remote harness runs. One implementation per protocol so far;
  the plan asks for two where applicable.
- **Parallel use:** the owner asked that the VMs and the Mac be kept busy in parallel; runs on different machines may
  overlap and share the host's CPUs (relevant to timing-sensitive results, see E-I23).
- **Ubuntu VM out of memory (2026-10-01 01:54, I37):** a fuzz process grew to 4.4 GiB; the kernel killed it and systemd
  stopped VMware Tools' service group, where runs started through guest operations live — every other run and VMware
  Tools itself, which then answered intermittently. Since then: long runs on that VM are user services (`systemd-run
  --user`, lowest priority, .NET heap capped at 1 GiB, lingering on), and guest commands go over SSH as the VM's user
  (SSH.NET from the host's package cache; the VM's host key pinned on first use, SHA-256
  `YOGYoStN1b+viZGymPI7ipmmzjXcg9sAZOWOYs/cOp4`). The lab's server helpers (connection drop, SMB listing and drop,
  names, latency) use the same route (`artifacts/vm/ubu-lab-ssh.ps1`, `ubu-names-ssh.ps1`).
- **Second implementations on the Ubuntu VM (2026-10-01, E-V08-L2):** ProFTPD 1.3.7c with explicit FTPS on 2121,
  implicit FTPS on 2990 and SFTP (`mod_sftp`, its own ed25519 host key, SHA-256
  `ztBPAN9/s2JbZMLMHnonJEHgLbwXX1ckgSVdl3T3UZg`) on 2222. Installing it removed the vsftpd package (both provide
  `ftp-server`); vsftpd 3.0.5's own package was unpacked under `/opt/fc-vsftpd` and runs from two units with the lab's
  configurations. After a reboot `/var/run/vsftpd/empty` was missing and vsftpd refused logins ("OOPS: … secure_chroot_dir");
  the units now create it before starting.
- **Ubuntu VM network adapter hang (2026-10-01 06:00:49–06:11):** under the lab's load from the host and the Windows VM
  at once (with seven fuzz processes on the VM), the emulated Intel e1000 adapter reported "Detected Tx Unit Hang" 304
  times and the VM stopped answering on the network, VMware Tools included. It was reset (power cycle); its fuzz runs
  were lost and its lab services came back. Since then the adapter's segmentation and receive offloads are off
  (`ethtool -K ens33 tso off gso off gro off`, re-applied at boot by a unit), the usual remedy for that e1000 hang. Lab
  results taken from 06:00:49 until the reset are void: the Windows VM's first client run and two host runs of one case.
- **The host's V: drive full; the Windows VM frozen (2026-10-01, 06:48–07:35 local time):** both VMs keep their
  snapshot disks on V: (1.86 TB, the owner's). Since it was lent, the Windows VM's snapshot disk had grown to 87.7 GB
  (fuzz runs, test runs, the recovery images, and the guest's own activity over about 11 hours; the fuzz rounds
  themselves write nothing). That filled V:, and the VM stopped on VMware's "disk full: Retry / Cancel" question; its
  guest operations hung. The question was answered Cancel and the VM reverted to `updated #38` (above). The revert
  itself needs some free space, so one file not made by the campaign — `V:\rtr4D81.tmp`, hidden, 1 GiB, untouched
  since 2024-11-07 — was moved to E: for the revert and put back afterwards. Its content (the same SHA-256 before and
  after), attributes and three times are as they were. V: then had 87.7 GB free. The Windows VM's unfinished fuzz runs
  were lost (E-I28-C1). The Ubuntu VM's fuzz processes were paused meanwhile (SIGSTOP) and resumed afterwards. Right
  after the revert the VM's .NET 10 runtime is 10.0.5 (the snapshot's); the 10.0.6 recorded in E-ENV-02 was seen on
  2026-09-30, some time after the VM had started. A watchdog (`artifacts/vm/v-space-watch.ps1`) now
  pauses them if V: has less than 25 GB free. Long runs no longer go to the Windows VM.
- **V: low again; the Windows VM reverted again (2026-10-01, 11:36–12:45 local time):** a fuzz run had gone to the
  Windows VM again (in memory: the rounds write nothing), and V: fell from 56.6 GB free at 09:46 to 10.4 GB at 12:30. The
  VM's snapshot disk had reached 74.7 GB, and was still growing by about 40 MB a minute after the watchdog
  (`v-space-watch2.ps1`: below 15 GB the VMs' fuzz is stopped, below 8 GB the Windows VM is paused) had stopped its fuzz.
  The writer was the guest itself: Windows Update was installing a Visual Studio update (`VisualStudioUpdate-17.0.0To17.14.40`,
  `msiexec`, `setup.exe`), with a shadow copy (`VSSVC`), and restarted the guest at 12:30. The VM was reverted to
  `updated #38` (V: back to 87 GB free), then the owner reverted it once more and started it. In the guest, the Windows
  Update services were stopped and disabled; within minutes Windows Update was running again (its Medic service sets
  the services back), and a service process started at 12:50 had written 986 MB by 13:01 (most likely Windows Update's;
  not confirmed). So Windows Update now points at an update server that does not exist (policy), and the VM's network
  adapter is disconnected (`vmrun disconnectNamedDevice ethernet0`; `connectNamedDevice` brings it back). Both are undone
  by the final revert. The Windows VM's unfinished fuzz results were lost again (E-I28-C1).
- **The Windows VM as a client (V08):** reaches the Ubuntu VM directly on the NAT network (192.168.58.128 →
  .129); .NET 10.0.6 runtime installed. The lab helpers there run in Windows PowerShell 5.1, which drops quotes inside
  arguments to native programs; the VM's helpers send their commands base64-encoded (`artifacts/vm/vm-lab-ssh.ps1`,
  `vm-names-ssh.ps1`).
