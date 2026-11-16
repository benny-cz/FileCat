# Shared Linux native-open flags

Follow-up to [the dedicated worker checkpoint](DEDICATED_VM_ARCHIVE_WORKER.md), over `0100d846`.
This fixes a native ABI defect; it enables no archive writer or release-qualified platform route.

## Change

The worker's directory/no-follow flags had been corrected for ARM64, but Unix archive storage, mount-info reads and
POSIX ACL directory opens still used x64 constants. ARM64 uses `O_DIRECTORY=0x4000`, `O_NOFOLLOW=0x8000`, whereas x64
uses `0x10000`, `0x20000`. The previous directory open could fail on ARM64; the no-follow value also failed to express
the intended link refusal. The values are specified by the
[Linux ARM64 UAPI](https://github.com/torvalds/linux/blob/v6.18/arch/arm64/include/uapi/asm/fcntl.h).

`LinuxOpenFlags` now supplies the supported ABI mapping. Worker control-file opens, Unix archive directory/file
opens, mount-info reads and Linux POSIX ACL directory opens use it. Unknown process architectures refuse instead
of guessing. Darwin constants and x64 values are unchanged. The internal ACL opener is available to the confined
validation probe, allowing its actual native handle behavior to be checked without granting filesystem mutation.

The new Linux-native regression holds the real directory identity, refuses directory symlinks and regular files,
and checks the complete ACL-probe path. Existing directory/file alias, metadata, permission, publication and real
child-process crash cases were replayed alongside it. The mathematical ABI guard checks complement actual kernel
operations; they do not substitute for an ARM64 execution result.

## Executed evidence

| Machine | Passed | Skipped | Total | Scope |
| --- | ---: | ---: | ---: | --- |
| Windows 11 x64 VM | 101 | 110 | 211 | Medium integrity; Unix native guards skip |
| macOS 27.0.1 ARM64 | 203 | 8 | 211 | Native archive storage/metadata and unchanged Darwin opens |
| Ubuntu x64 VM | 206 | 5 | 211 | Actual directory/ACL, archive storage and permission operations |

No test failures in these completed runs. Platform skips remain explicit. Windows before/after integrity is recorded
by the existing medium launcher; the administrator policy was not weakened.

The first Mac/Ubuntu source fixture omitted `FileCat.slnx`, which the child-process harness uses as its checkout
marker. Both runs failed 48 child-process setup cases with `Assert.NotNull`; their logs/TRX remain retained. Supplying
the marker and rerunning **the same built binaries** produced the results above. No test was removed or changed to
obtain those passes. Normal repository checkouts already contain the marker.

The strict Linux 6.18.60 ARM64 guest on the authorized Mac passed **29 exact named kernel checks**, including real
archive-directory/file opens and ACL-directory opens, archive directory/file symlink refusals, ACL directory-symlink
refusal and rejection of a regular file as an ACL directory. Both positive opens and negative link cases are required.
The verifier now requires the complete expected field set so an omitted check cannot become a pass.

That guest run also passed all **16 actual TAR-family create/assess/replace/fresh-verification roles**, independent
extraction, and the existing parent-death/cancellation/helper-death/memory suites and extra-process startup refusals.
Compiler PDB hashes matched **811 source documents / 7,589,500 bytes** for the actual production worker bundle.

The separate host/provider memory limit still fails qualification: maximum RSS **1,233,076,224 B**, maximum footprint
**1,087,243,656 B**. These fixes do not close that resource gate, installed signing/image/package admission, metadata/
capture/publication/recovery, remaining format/UI work or the fuzzing/release requirements.

## Provenance and cleanup

Platform outputs are under the owned `unix-open-flags-v1` directories in each machine's archive-writing task root.
Successful Mac/Ubuntu results are `results/unix-open-flags-v2.trx`; failed setup evidence remains `test.log` and the
first TRX. Windows uses `results/unix-open-flags-win-v1.trx`, `medium.facts.json` and `medium.out.log`.
Raw copies remain in the isolated checkout's ignored `.tmp` directory.

Transferred source SHA-256 identities:

| Input | SHA-256 |
| --- | --- |
| Baseline source bundle | `C0465DE0FF40D938D03A9AA6EA1B7775448226F4F62A0DE0FC7EBA818FECA208` |
| Prior implementation overlay v6 | `D3CA07655F74CAB71B9E119475892AA88360C46170FEF2F16CE05F29450E1926` |
| Native-open delta v1 | `AEB92752BE04A83930672D6DEE3CFEDBEBB6441994425B345E51014D8505292E` |

Strict guest output: `/Users/benny/FileCatValidation/archive-writing-42829297/macos-vm-unix-open-flags-v1`.
Frozen `macos-vm-unix-open-flags-evidence-v1.tar.gz`: **262,635,259 B**, SHA-256
`F327002C678A3E5C38FC5C6D7EB939D42C1A75690DC0E72FDC2B5A98020F16C8`, **1,444 payload rows plus inventory**.
The copied archive digest and every inventory size/hash passed independent local verification.

The inventory's inherited `process_inventory_probe_added_after_run: true` label is inaccurate for this run: the
extra-process tests were part of the main runner before it returned. Compiler-source auditing was post-run. The
frozen payload/manifest are preserved; this explicit provenance correction does not rewrite captured sources/results.

Owned test/helper processes were reaped and Windows' final process census was empty. Other testers appeared after
the completed Mac/Ubuntu runs; follow-up execution was deferred until the relevant census was idle. No unrelated
process was stopped. Mac's until-reboot wake helper PID 36765 remains. No host UI was used, and `E:\FileCat` stays on
`main`, with implementation isolated on `codex/archive-writing`.

The full goal remains active. Continue the capture/publication/production qualification work; RAR analysis and any
viable licence-compatible implementation precede the final independent UX/UI/functional/security review.
