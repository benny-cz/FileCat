# TAR.GZ catalog reuse and worker-session diagnostics

Development checkpoint over `d19b41e7`. This does not complete the
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) or enable new production profiles.

## Behavior

The gzip update adapter reuses its completely validated raw TAR catalog inside the worker. The shared TAR engine
checks its revision and decoded length, independently compares every member with the runtime decoder, admits the
whole namespace, and checks copied source bytes. No parent DTO or wire record supplies this catalog. The initial
strict gzip scan still validates DEFLATE termination, CRC, size and physical EOF. No payload spool is introduced.

Removing the redundant raw reparse also removes a simultaneously retained complete catalog. Decoded replay now allows
two forward passes; with the initial physical hash and raw-catalog scan, updates require at most four physical
compressed-source passes instead of five. Parent IO ceilings are tightened accordingly. A 64-member regression counts
actual compressed-source reads and verifies the complete output. Complete combined heap accounting and maximum-scale
qualification across catalogs, manifests, snapshots and parent/worker state remain unfinished.

Worker-session refusals retain the original monitor/conversation exception and a diagnostic phase. Terminal rejection
also retains the exact exit code and diagnostic-limit fact. The monitor's exit observation is inside its guarded block.
These exception diagnostics provide no authority or new UI copy. Tests distinguish monitor, conversation and terminal
failures, verify the original cause/facts, and check reaping and subsequent queue use. Resource, exit, termination and
reaping checks remain enforced.

## Reproduced controlled-child defect

A real Linux regression waits for positive ordinary-child exit, then reads CPU time before disposal/reaping. Before
the fix it failed on .NET 10.0.14 with `InvalidOperationException: Cannot process request because the process (...) has
exited`, through `Process.ThrowIfExited`, `EnsureState`, `GetStat`, `get_TotalProcessorTime`, and the controlled test
child's getter. Its existing filter handled only `Win32Exception` at this exit boundary.

The test wrapper now returns the cached CPU observation for either exception only after confirming exit. Live-process
observation failures still propagate. The production Linux worker uses native cgroup CPU accounting and the Windows
worker retained native process/job handles; neither was weakened. The real exit-boundary regression passes on all
three platforms. Five additional diagnostic/refusal cases exercise exact causes, facts and reaping.

This fixes one directly reproduced defect. The two older intermittent Ubuntu refusals in
[the preceding checkpoint](TAR_GZIP_WORKER_PIPELINE.md) did not retain their original cause, so their precise historical
cause cannot be established retrospectively. Passing replays do not prove that no other lifecycle bug exists. New
monitor failures now retain the evidence needed to distinguish them.

## Validation

| Environment | Initial broad Core replay | Final Core replay including exit-boundary regression | Additional replay |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,197 passed, 172 skipped | 1,198 passed, 172 skipped | UI 219 passed; storage 103 passed, 30 skipped; actual LPAC 42 passed |
| macOS ARM64 / APFS | 1,315 passed, 54 skipped | 1,316 passed, 54 skipped | UI 219 passed |
| Ubuntu x64 VM / ext4 | 1,328 passed, 41 skipped | 1,329 passed, 41 skipped | UI 219 passed; actual Linux confinement 51 passed, one skipped |

All listed replays have zero failures. The deliberately failing Linux regression is retained separately as red
evidence. Final Core totals are 1,370 on each platform. The filter includes Archive, EditSession, BackingArchiveTarget
and PaxTimestamp; native lanes use tracked runners. The final follow-up changes only test infrastructure/regressions;
production code is identical to the initial broad/native replay. SDK 10.0.101; actual runtimes Windows/Ubuntu 10.0.14,
Mac 10.0.12. Windows launch facts report integrity 12288 before / 8192 after and exit 0, with admin membership retained;
this is not filtered-user installation qualification. Host builds completed without warnings/errors; the initial
focused host replay passed 47 cases.

Fresh per-run oracle directories avoid prior-output contamination. Independent Python readers validate eleven gzip/
TAR outputs and eight exact-time TAR outputs per replay, including updates of independent Python-generated USTAR/PAX
duplicate-member inputs. Native Unix C metadata/xattr/capacity observers remain enabled. The completed Mac follow-up
marker initially had a trailing carriage return from the local script writer. Its exact `PASS` contents were checked
and only the owned marker's name was corrected; tests were not restarted. The Unix runner was LF-normalized before
the accepted Ubuntu follow-up.

Ubuntu attempts postponed while another FileCat instance was active; no other tester's process was killed. Final
runners reap this task's children. Mac keep-awake assertions remain until reboot. Shared `E:\FileCat` stays on `main`,
tracked-clean. GUI, installed-package/RID/filesystem, metadata/publication/recovery, fuzz/scale and SBOM qualification
remain incomplete. No third-party source or dependency was added/upgraded. The finishing order remains implementation
and validation, RAR licensing analysis, then independent UX/UI/functional/security review and fixes/retests.

## Evidence

Raw logs/TRX, deliberate failure, launch/child facts and independent fixtures are retained in ignored
`.tmp/test-results/archive-writing/catalog-session-v1` and the task-owned guest roots. The final ten-file source overlay
is over `d19b41e7`. The two-file green follow-up changes only test infrastructure/regressions.

| Archive | SHA-256 |
|---|---|
| Initial nine-file source overlay | `67E129AA94441C3BD92D60F008325263047D954C5457AB4FAB2EB13B8C7B21A3` |
| Final ten-file source overlay | `D649846B35FDCA44B237C61B9009CDDB2E1AED2FB3791752EF5E2E6320061D18` |
| Initial Windows 84-file managed overlay | `49CAE35F872C10F6B979D3A6435D832B825BC6EACF03AC93EE7D8EA01AA8DA1B` |
| Linux red-regression source overlay | `4EBC5CD7FB0CEC735E86479E1D9D5E8EB89EB0A5292F7ED501DBD0DE62F54A4A` |
| Corrected two-file test-source overlay | `78D461885DF091C14373345CE11B6AB6FFC71DB43542DF63C9CCA64E602CAB3C` |
| Windows corrected test DLL/PDB overlay | `A67BDD38602049B92E292C17E66AE6FCEAAC818A9FA1C126E7ABB91984320EDA` |
| Initial Mac evidence | `721EBEE374B6042B73D17DCAB482CA1360BBCF817EB45E0CC7AE9538C0D36C27` |
| Initial Ubuntu evidence, including red regression | `780C31DC2B60D3FC2EF4CE180B6FA938044B867FE5280D7F10D130EE27EF98D3` |
| Initial Windows evidence | `557F04F80CA7803B3CDC51C8D70DA8433B8A03DB106757C235C8C1B1A95FCC3C` |
| Mac final evidence | `E64E7F3F689DC27046D94C34C7A988F477D0C7A4BD6BD952E0F1B71C1D24E3D6` |
| Ubuntu final evidence | `70FC665CA63EB86330F5A90613A26EBFD903631734878C580B2B98464041061F` |
| Windows final evidence | `9DE0FA14C429078E5B7C89C301BE400A3DB086B9041CAA708DEBD230C7D62040` |
