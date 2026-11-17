# Bound storage evidence publication — 17 November 2026

Continues the [prefix reader checkpoint](2026-11-17-storage-prefix-export.md) on `main`, parent `40480c8e`. Live operation summaries and recorded-call windows now offer **Save all recorded calls…**, separately from **Save this call page…**. The captured prefix never advances while choosing a destination. The native save picker requests a new local file; existing files are never replaced.

Core verifies the entire source prefix before creating output. The Windows output owner walks ordinary local components relative to held no-follow directories, respects each directory's case mode, rejects observed reparse/offline/recall-backed folders, and compares held identities against the private evidence directory. Publication does not rely solely on the UI's path-prefix exclusion. With the source held, two rolling directory handles fit the three-slot envelope; at the final folder those slots become source + destination folder + new staging file. Every started handle is charged and retired through actual completion.

The output is created through that held directory, held without sharing, verified/flushed, then renamed through its own handle into the same held directory without replacement. A preflight metadata probe refuses an existing target before copying; a target created afterward is still protected by the atomic no-replace rename. Cancellation/failure deletes only the owned staging object through its retained handle. A published file is retained even if Stop arrives or the folder flush fails afterward; a flush failure is returned as a published-output warning. Cleanup failure reports the kept temporary name. There is no pathname reopen, overwrite or pathname-only temporary deletion in this route.

The older inactive **Save call details…** route now uses the same output owner, preserving its exact reviewed raw snapshot behavior for damaged evidence. The staging-reader API remains available with its explicit caller-publication contract; product UI uses the bound publication API. Closing a report cancels its export token while the owned I/O delegate and output remain alive until actual return. The main service path links session shutdown and uses interactive accounting plus `RunOwned`.

Verification used the supplied Windows 11 x64 VM at medium integrity, Ubuntu x64 VM and macOS ARM64. No test ran on the workstation. Final builds passed with zero warnings/errors.

| Selection | Windows | Ubuntu | macOS |
| --- | --- | --- | --- |
| Core call/cursor/ownership/acknowledgment, 60 cases | 60 passed | 60 passed | 60 passed |
| App call/operations/history/report selection, 30 cases | 29 initially passed; one corrected case passed on repeat | 29 passed, one Windows-only skip | 29 passed, one Windows-only skip |
| Full native private-evidence class, 18 cases | 18 passed, zero skips | Not run | Not run |

The first Windows App attempt exposed an invalid directory-open option in the new export path (error 87). Removing that option restored the ordinary no-follow directory recipe; the affected actual-App fixture passed, exporting the full 71-call captured prefix through the real manager, scheduler and Windows publisher. Only that affected App case was repeated after the native change. The final 640 × 420 close/lifetime fixture was repeated on Ubuntu and macOS after its layout update.

Native checks include exact live-prefix and inactive raw bytes; read-only existing-target preservation; private namespace refusal; a destination appearing after staging; blocked staging substitution; cancellation cleanup and evidence-lane release; junction refusal; and output to an explicitly prepared FAT32 virtual disk. The 128 MiB VHDX was created only in the disposable VM, verified as its separate disk before formatting, mounted as `R:`, and retained as a reusable fixture. The initial invalid FAT label was corrected to `FCEXPORT`. Local-disk filesystem observation is shared with the existing helper without relaxing its default private-NTFS policy. The new output path can use non-ACL local filesystems; it does not claim private ACL protection there.

Core fake-owner cases enforce validation before output creation, no publication after digest change or cancellation, collision propagation and output-before-source retirement. The portable UI fixture checks display gating, one export at a time, cancellation on close and ownership retained by a producer that ignores cancellation. Native save-picker interaction, high-text-scale/screen-reader behavior, crash orphan handling, non-FAT32 output filesystems and architecture-specific qualification remain open; these focused checks do not establish those broader gates.

Final source archive SHA-256 `C618147F3E3C6CBCC3ADDCF9475A6616EA3A1BAEF02A61F328D64C9590880D16`; Windows App archive `D549169F06D81FD275018A21DF9784E6FCA35159FD26B91F6D78FC979D6A26E6`; native archive `0095A042EECA7BC770B9F32AD6E4E18AB563D7C3B0A77A3BB38CA40DDB293F8D`; Core archive `686F4EB206DC66E1DEE30D15863CF91E029A158B294804C16DF0EEF62D8BAAF9`. Scratch scripts/logs are `.tmp/filecat-bound-export-*` and remote `/tmp/filecat-bound-export*.log`.

After the behavioral checks, the inactive picker title was clarified to request a new file; that text-only change built cleanly without repeating the selections. Hosted run [44128424862](https://github.com/benny-cz/FileCat/actions/runs/44128424862) for parent `40480c8e` completed successfully on Windows, Windows ARM64, Ubuntu and macOS. This new publication implementation still needs its own hosted run after push.

The implementation plans remain active, including native UI/refresh/collector qualification and EFS. This checkpoint is not product mutation enablement or full-plan completion.
