# E-I04-WIN — Windows 11 diagnostic installer lifecycle

Preliminary V19/I04 execution on 2026-10-12. The exact guest source is
`685c78a417d7f9110860ef06c61366e8773bf09b`; the source archive SHA-256 is
`b1f9110fb5637f10943a9638780e56ed7a4314d74e409d3fcd2d36969b13895d`.
The VMware guest is Windows 11 Pro **Insider Preview** build 26300 x64, logged in as Admin.
It had no FileCat installation, uninstall entry or process at preflight, but it has an SDK and Visual Studio;
this is an absent-app lifecycle check, not a clean-machine/no-developer-tool qualification.

## Producer and adverse setup observations

`dotnet restore FileCat.slnx --locked-mode` passed on a corrected direct invocation. The initial
wrapper incorrectly treated an unset `Start-Process.ExitCode` as failure even though its raw
restore log showed restored projects; both attempts are retained. The pinned Inno Setup 6.7.1
acquisition succeeded in the guest. The compiler receipt pins executable SHA-256
`eb6f4410c8db367a5f74127e8025ad2ccacc0afabbe783959d237df3050f97fb` and frozen
manifest SHA-256 `36773634eb09c0a2cd29e2bb26f4e2aad7a0c1ed505e887b7c73bdcc56517477`.

The first publish failed when `cl.exe` could not start. Guest SideBySide events identified
`cl.exe.config`; it and `link.exe.config` were each 409 NUL bytes, SHA-256
`9323eb9e4e6ee98e9844baaad2d73ca87f7dbe7f2967f95d493af47dcbd6fa09`.
A VM-only wrapper checked their hashes, moved them into the owned test root during publish and
restored both original paths and exact hashes in `finally`. With this workaround the native
broker bootstrap built, but the first wrapper was launched by Windows PowerShell 5.1 and
stopped at the package-inventory script's PowerShell 7 requirement. The official PowerShell
7.6.6.0 winget package then installed silently in the disposable guest with installer hash
verification. A second, separately retained wrapper run under `pwsh` passed publish; both
compiler configs were again restored to their original hashes. These are guest setup faults,
not FileCat product defects or permission to modify the host toolchain.

The successful publish produces a 100,907,039-byte portable ZIP SHA-256
`fb7a2a51af01a3528947202f72949ff2daa7d2fdd8a05a1efc22acbbe83b51e2`,
a 64,379,155-byte FDD ZIP SHA-256
`acca72be99deaa0b9054ceb4964a188bae145e23e2f96bafe39957d05e0974eb`,
and the inventory in the private receipt. `Compile-WindowsInstaller.ps1` reports unchanged inputs, exit zero,
no timeout and diagnostic `FileCat-0.0.0-win-x64-setup.exe` at **71,406,054 bytes**, SHA-256
`913795680ed2d0c0c6de6af7e9bee4dec5e0641f2f176e7b457f5c8b28de8663`.

## Installed behavior and ownership

The silent per-machine setup exits zero and installs under `C:\Program Files\FileCat`.
All **303** published files exist there with matching SHA-256; the three additional files are
the Inno license and two uninstaller files. A direct 64-bit HKLM registry read finds the
`FileCat version 0.0.0` uninstall entry, and the Start Menu shortcut exists. The first
verifier falsely reported no uninstall entry because its registry-provider query used the
wrong shape; the direct read is retained separately. The setup log flag on that invocation
did not create an app-install log; the raw exit, installed-file comparison and registry
observations remain. FileCat launched in the guest Admin session, stayed alive for ten
seconds with title `FileCat — Admin — Admin (elevated)`, accepted close and exited within
15 seconds. This was an elevated launch; unelevated interactive launch remains unqualified.

The first silent uninstall exits zero, removes the HKLM entry, shortcut and installed payload,
and preserves an owned unrelated file placed in `{app}` plus all **four** pre-uninstall user
state files byte-for-byte. The uninstaller executable disappears shortly after its process
exits; a bounded follow-up observes only the sentinel. Silent reinstall into that nonempty
directory exits zero, preserves the sentinel and all four state files before launch, reproduces
every payload hash, and launches/closes FileCat again. The final uninstall exits zero, removes
the registration and payload, and preserves all **seven** then-existing state files. Its
verifier returned failure solely because a one-element PowerShell result was treated as a
scalar when deciding whether to remove the owned sentinel. A separate exact-hash and
sole-remaining-file check removed that sentinel and the now-empty install directory; both
the false-negative verifier result and corrected cleanup are retained.

The diagnostic artifact is unsigned and built in a development guest, not downloaded from a
public release. This case does not qualify a clean Windows image, standard-user first run,
SAC/WebView2 and other native integrations, portable/FDD absence/presence, ARM64 transition,
signed uninstaller/setup, frozen support tiers or the final chosen candidate. I04 and V19 stay open.

## Standard-user limit and VM recovery

A later standard-user diagnostic installed the same exact setup and started FileCat as the owned
`FileCatStdI04` account. The process ran for ten seconds under that account, but it was in
session 0, had no window handle or title, and could not accept a normal window-close request;
the harness terminated it. This is a noninteractive process-start observation, **not**
standard-user desktop acceptance. The original 68 case inputs rehash correctly. The three
additional raw files have a separate addendum manifest SHA-256
`c3c371c73797bfbfdc2ff863a8335016e78ad05c6c1ce966d068defa905752eb`.

A temporary VM-only automatic-login attempt then stalled during a guest soft reboot. One hard
reset reached firmware but still did not restore guest operations. After the case data had been
copied off the VM, the sole snapshot `updated #38` was restored and started without a host
console. VMware Tools and the Admin guest login responded. A read-only post-restore audit found
no test account, test root, installed FileCat or `DefaultPassword` registry value. The snapshot
has its own baseline `AutoAdminLogon=0`, `DefaultUserName=Admin` and
`DefaultDomainName=.` values; none equals the temporary automatic-login setting. The full
stalled/restored VMware logs, audit script/result and file hashes are in the separate private
`i04-windows11-vm-recovery-20261012-v1` receipt SHA-256
`7a011fc317ea1b80cd2c468fc2ff2123f55e41dc55ddabb6e3b8ef8147421b3e`.
No standard-user interactive or clean-machine conclusion follows from the snapshot recovery.

At the owner's request, the restored disposable VM was temporarily quieted for later tests.
The read-only baseline found Defender real-time protection already off, with behavior, IOAV
and script scanning preferences still enabled. An exact baseline was retained before changing
those three preferences, disabling the four enabled Defender scheduled tasks and setting the
Windows Update `NoAutoUpdate=1` policy. The Update Orchestrator stopped and was disabled.
Windows rejected disabling protected Delivery Optimization and Update Medic startup settings,
and restarted `wuauserv`; the latter still reported `Manual/Running` in a later audit. Thus
automatic updates are policy-blocked and Defender real-time/on-access scanning reports off,
but the Windows Update and Defender services themselves are **not** fully disabled. An
eight-second worker sample still showed `MsMpEng` using 3.484 CPU seconds after the task
changes, while `TiWorker` and `TrustedInstaller` used zero. All action successes/failures,
before/after states and CPU samples are retained in private
`i04-windows11-vm-quiet-20261012-v1`, receipt SHA-256
`a113371d08cb4784c663b921271ab29544f92e6d1835e93a9746b22d31b2a574`.
Its baseline-specific restoration script is staged for use when VM testing finishes;
restoration has not yet been claimed.

## Raw record

Private case: `artifacts/release-evidence/i04-windows11-install-lifecycle-20261012-v1/`
(currently on `V:\FileCat\artifacts`). Its 68-input manifest SHA-256 is
`b2bcb15e48c2fbde7f6bc6cdafd9a770f826d708e1e4f52f52457cf40c7efeec`.
It retains the exact installer, source ZIP, publish and compiler receipts, raw stdout/stderr,
guest scripts, adverse attempt results, registry reads, install/reinstall/uninstall JSON,
payload checks and owned cleanup. The standard-user addendum and VM-recovery receipt are
separate from that 68-input manifest. No release candidate was formed or published.
