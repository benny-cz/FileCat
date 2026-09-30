# E-I28 — a damaged NTFS size made the whole volume unreadable to recovery (found by fuzzing)

Issue: [I28](../FILECAT_1_0_RELEASE_ISSUES.md#i28--a-damaged-ntfs-size-or-data-run-made-the-whole-volume-unreadable-to-recovery).
Plan: §17.3 (disk structures are untrusted input: a damage report or a smaller listing, never an unexpected exception,
a hang or an unbounded allocation); V11/V23 recovery robustness; I09 context.

## E-I28-D1 — discovery

`RecoveryFuzzTests` damages the recovery fixtures (FAT12/16/32, exFAT, NTFS, MBR and GPT disks) round by round — each
round's damage depends only on the image and the round's number — scans each damaged copy, and reads what the scan
offers. CI runs 200 rounds per image. On the owner's M1 Mac (E-X01 run M3), with the `5c54181` test build, 5,000 rounds
per image passed (108 s), and a 100,000-round run failed the NTFS row: a warning "The file system is damaged beyond what
FileCat reads (internal: …)" — the scanner's safety net for exceptions the parsers did not expect.

## E-I28-R1 — reproduction and mechanism (physical host)

- With the test's new selectors (`FILECAT_FUZZ_IMAGE=ntfs`, 100,000 rounds) the first failure is **round 8842**:
  "ntfs, round 8842: The file system is damaged beyond what FileCat reads (internal: OverflowException)." (output
  `fuzz-ntfs-host.txt` `3d20781bff0cdd81184ad6ef6f008d9ed065aabf877a65d420db51e24dc47fc5`).
- A temporary probe replaying round 8842 with a first-chance exception hook located the throw:
  `NtfsScanner.ReadStream` (`Ntfs.cs` line 489 at `d40e510`), `var data = new byte[size]` with
  `size = Math.Min(stream.Size, limit)` — the `$Bitmap` data attribute's recorded size, damaged to a negative value,
  became a negative array length. The safety net turned the exception into "damaged beyond what FileCat reads" for the
  **whole volume**, so nothing on it was offered for recovery, although a damaged `$Bitmap` only costs the free/in-use
  classification (the scanner already handles a short `$Bitmap` with a warning).
- Review of the same decoder found the neighbors of that defect: a first cluster number (`startVcn`) or last cluster
  number read as negative or enormous, a sparse run of any length (only non-sparse runs were checked against the
  volume), and a per-cluster walk over sparse runs in `Describe`, which a damaged run claiming billions of clusters would
  turn into a hang.

## E-I28-V1 — fix `98fb594`

- `AddRuns` ignores an attribute instance whose first cluster number is negative or beyond what cluster arithmetic can
  hold, keeps sizes and the last cluster number within that range and the initialized size within the size, and stops at
  a run that would pass it (sparse runs too).
- `ReadStream` clamps the size to `[0, limit]` and reads only runs that lie inside the stream.
- A sparse run becomes one zero-filled extent instead of a walk over every cluster it claims.
- `RecoveryFuzzTests` names the image and round in every failure, can run one image and a chosen range of rounds, and
  replays the rounds that once failed (`Rounds_that_once_failed_stay_fixed`, starting with `("ntfs", 8842)`) on every run.
- **Verification:** the saved round fails on the unchanged code in a clean worktree at `d40e510` ("ntfs, round 8842: …
  (internal: OverflowException)") and passes on `98fb594`; the whole Core suite passes on the host (545 tests, 0 failed).

## E-I28-C1 — fuzz campaign on the fixed build (in progress)

Rounds are split between machines so none repeats another's work; results are added here as they finish.

| Machine | Build | Rounds per image | Result |
|---|---|---|---|
| Owner's M1 Mac | `5c54181` | 0–99,999 (all images) | NTFS failed (this issue); the other rows: pending |
| Host | `98fb594` | NTFS 0–99,999 | pending |
| Ubuntu 22.04 VM | `98fb594` | 100,000–1,099,999 | pending |
| Windows 11 VM | `98fb594` | 1,100,000–2,099,999 | pending |
