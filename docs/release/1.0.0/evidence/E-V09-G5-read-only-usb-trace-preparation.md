# E-V09-G5 — read-only USB source-hash/trace preparation

Links: V09/REC-002/TV-09, I09, I106, ENV-07. **Prepared, not executed.** No physical source-write pass or candidate
qualification is claimed. The completed tracer controls are separate [E-V09-G4](E-V09-G4-tracer-controls.md) evidence.

A clean Release/win-x64 self-contained native Windows test payload is published at exact
`f2f0141b34a2c693951019d724774ca0a0a6fc01`, with ContinuousIntegrationBuild enabled. All 300 staged files are
independently size/hash verified against the retained manifest, with exact member-set equality. Core, Recovery,
Windows platform and test assembly ProductVersion each contain that source SHA. The initial manifest collector
expected a file that PowerShell's empty clean-status pipeline had not created; it stopped before selecting a test.
The empty before-status output, unchanged source and explicit subsequent clean-status record are retained. No source
or payload replacement is attributed to a different build.

The native method selector is checked without physical opt-ins. It selects exactly one existing case,
`LiveDriveRecoveryTests.An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it`, which explicitly
skips because the USB prerequisites are unset. This is selector evidence, not a physical test pass. The live case
compares direct and in-process raw-read-protocol bytes at four intervals and their deleted-item scan counts.
It does not exercise the installed administrator broker, consent, GUI admission or production process census.

The concrete private launcher is
`artifacts/release-evidence/v09-usb-source-trace-20261003/LaunchReadOnlyUsbTrace.cmd`. It pins the runner, which pins
the manifest, independent hasher/helper, campaign lease helper and installed tracer. Metadata preparation finds the
same authorized serial `2F2000129618`, disk 5, 7,796,162,560 bytes, USB/nonboot/nonsystem, existing NTFS volume G:.
Live execution must repeat identity and backing-disk checks; this preparation does not authorize a replacement disk.

The live procedure will:

1. Acquire the same non-waiting global per-serial campaign mutex and refuse active legacy/direct test processes.
   Require the native identity/protected-disk preflight to pass exactly once before source reads.
2. Hold the fixed per-serial file lease and independently hash every byte of the **physical disk**, not only its
   partition. A separate bounded helper uses GENERIC_READ, native length verification and sequential ReadFile;
   native length and hashed-byte count must both equal the pinned capacity. Recheck full device identity afterward.
3. Start the controlled tracer with `/NoFilter`, retain full native PML and include off-source read/write and
   untouched-path controls. Capture the native test from launch through exit. The native LiveUsbGuard holds the
   same fixed serial file lease during its scope; controller-to-native and return handovers are explicitly recorded.
   The global campaign mutex covers the full procedure; no atomic shared-file-lease claim spans the handover gaps.
4. Stop only the owned capture, reacquire the serial file lease, recheck identity and hash the full physical disk
   again. Preserve hash differences for investigation. Export the full CSV and restore/verify the original tracer
   configuration. Retain output hashes, process records, XML and logs for independent analysis.

Before/after hashing occurs outside the trace window to bound trace storage. Each hash helper has a fifteen-minute
process bound. The native session has a ninety-second bound; capture has a 180-second runtime bound and a 2 GiB
PML stop threshold. At least 8 GiB free evidence space is required. The procedure neither formats the USB nor creates
fixtures on it. It records component capture/hash outcomes separately from subsequent independent trace analysis.

Windows PowerShell 5.1 preparation controls pass: three scripts parse; a known 4,096-byte input has the independently
expected SHA-256; a truncated source is refused; a separate process holding the campaign mutex causes exit-one
refusal, while available validation exits zero. No raw USB access or capture occurs in these controls. Initial
control inputs/results are preserved before tightening preflight XML count checks; final controls bind the final
runner. Independent preparation checks verify all inputs, selector counts, controls and embedded source identities.

The final command-file check catches a generated newline inside the launcher hash literal before any owner handoff.
After fixing it, an actual cmd.exe control exposes an inherited PowerShell 7 module path preceding Windows
PowerShell 5.1's system modules: Get-FileHash is unavailable in that command context. The launcher now uses setlocal
and puts Windows PowerShell's system module directory first for its process and children. The exact command with
ValidatePreparationOnly then exits zero and prints the expected preparation success, without raw source access or
capture. Earlier launcher bytes, failed command output and module discovery diagnostic are preserved. The initial
inventory verifies staging/control data but misses executable launcher validity; its launcher-readiness conclusion
is superseded by the final line-structure, command-execution and independent inventory checks below.

The hasher's native API choices are grounded in Microsoft's
[disk length query](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_disk_get_length_info)
and [ReadFile documentation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-readfile).
That documentation is not evidence of a successful physical run.

Both documentation-source CI runs pass all four lanes, with three package jobs skipped:
[37122049396 at 087917d](https://github.com/benny-cz/FileCat/actions/runs/37122049396) and
[37122560698 at f2f0141](https://github.com/benny-cz/FileCat/actions/runs/37122560698).
Complete metadata/logs are retained; no direct artifact test inventory is inferred from these metadata checks.

| Private preparation evidence | SHA-256 |
|---|---|
| 300-file native input manifest | `97d595c25c0022f9b6e2099274eb03fc4f78e65357bea5709e6791336a7aab24` |
| final runner / launcher | `75a3052ce029270992cbe2a6f97b55133d4c3e91467cf1eeb1a0eaeb9a811af2` / `96b9cd3bd002e18d2a4a0d4c619d66633fcb4eafcdbbed22072c59cfe3331318` |
| independent native hasher / bounded hash helper | `7b9d47a3bfa09057ef32b27f017e83000999e0f239af2a12ed0741e5776c903f` / `e8f1fd58383e54b7e571e24dbfb1deae1bfdfaf90964760b489c495db63af270` |
| final preparation controls | `668cfcbaf09e1e566da6adefeea7f609d317d857ae1c5c70d5be0518a5e1e3d9` |
| final cmd.exe command control | `8e5f31a4a7e5cb172cb436022fcb051b4ce1484c092114a4b7a32063b88c7fa7` |
| final independent preparation inventory, 300 native inputs and 45 preparation files | `163e2b8a4a89d0f5280c524acd9a5d395bdae38989627364f8c0a35dc3ddf0c8` |
| initial inventory, superseded for launcher readiness | `d3729e1e011652730f301654b5aeafebab9cb0222c6022c282082ca09d1ec80c` |
| 087917d CI metadata / complete log | `b3f849b9c3667e08332deb830af4fc20622abb2e4e2acfec9605a1baf8e65b19` / `b78688f1add80dbc55663f2cab295b28b7d1f347426c91b03b07cb7205da966f` |
| f2f0141 CI metadata / complete log | `20f87065fe77aace4bea726eaf10c2a6c4bb7be1608c97b63a9ecb4e90cd891c` / `4379713f6d40a76c3392addf153a701d4e029d06c31f93c9ea8711ebba0a8a64` |

The agent process is unelevated. The next genuine setup gate is an owner launch from the elevated host shell (or
approval of its UAC request). Raw-device visibility, source/descendant write analysis, source-hash equality, trace
coverage/loss limitations and every remaining installed-helper/GUI/candidate case require actual subsequent evidence.
I09/I106 remain open and the release recommendation remains NO-GO.
