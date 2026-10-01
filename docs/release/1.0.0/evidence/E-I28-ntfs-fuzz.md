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
| Owner's M1 Mac | `5c54181` | 0–99,999 (all images) | NTFS failed (round 8842, this issue); the other six images passed (1,920 s; `fuzz-100000.txt` `c9bfe2289f0ed0134d8e56a16c7a34ab87713620df6056e4be133ee2d2b0badf`) |
| Host | `98fb594` | NTFS 0–99,999 | **failed at round 56958**: the scan threw `ArgumentNullException` (second finding, E-I28-V2; `fuzz-ntfs-98fb594.txt` `779846a58993d5da10643e82e2d675e7411c55dfc3a6eb1732edbeed451171cd`) |
| Host | `bb977d0` | NTFS 0–99,999 | passed (1,283 s; `fuzz-ntfs-host-fixed2.txt` `c6570e4c2b6618b92ff873fca01b7c37385cf7fd61d6b3894a87d34703ebb413`) |
| Ubuntu 22.04 VM | `98fb594` | 100,000–1,099,999 | NTFS stopped on the second finding (317 s; `ubu-fuzz-ntfs.txt` `9c829e934c1238e6f28a8d891fe719c6f41a30585969942c258276d33fe87dce`); the other six images **lost**: the VM ran out of memory at 01:54 (I37) and the runs were stopped with VMware Tools' service group |
| Ubuntu 22.04 VM | `bb977d0` | NTFS 100,000–1,099,999 | **lost** with the others (I37) |
| Ubuntu 22.04 VM | `02acee6` | 100,000–1,099,999 (all images) | restarted 2026-10-01 00:34 UTC as user services (heap cap 1 GiB, lowest priority; `fuzz-02acee6.zip` `b1a86a5adcc6dcfa0603587b52c7947e2e5f71db911f879d46bfb96db3eda674`). NTFS **stopped at round 169,883** on the allocation budget (1,596 MiB for a damaged compressed size; fixed `0ec94f1`, replayed in every run, E-I37); the other six images running |
| Ubuntu 22.04 VM | `2ba114e` | NTFS 169,884–1,099,999 | resumed after that round on the newest decoders (`fuzz-2ba114e.zip` `9e66fe99b0405718fb174a14ce4023a1c378aaddeec666fe05f3582b77b0fab8`); **lost** at the VM's reset (2026-10-01 06:11, its network adapter hung, E-ENV-05), as were exFAT, FAT16, FAT32 and both disks of `02acee6`; FAT12 of `02acee6` had **passed** (12,965 s; `fuzz-c1/ubu-i37/`) |
| Ubuntu 22.04 VM | `2ba114e` | 100,000–1,099,999 (all images, four at a time) | started again 2026-10-01 04:24 UTC as one queue (heap cap 768 MiB, lowest priority); fat16 **passed** (5,855 s; `fuzz-c1/ubu-r2/`); the others running (paused for 4 minutes while the host's VM drive was full, E-ENV-05) |
| Windows 11 VM | `98fb594` | 1,100,000–2,099,999 | fat12 (5,530 s), fat16 (12,731 s), exFAT (11,856 s), disk-mbr (21,593 s) and disk-gpt (22,206 s) **passed**; NTFS stopped on the second finding (317 s); fat32 **lost** unfinished when the VM was reverted (2026-10-01 07:35, its host drive full, E-ENV-05) |
| Windows 11 VM | `bb977d0` | NTFS 1,100,000–2,099,999 | **passed** (11,981 s) |
| Windows 11 VM | `2ba114e` | NTFS, exFAT, FAT32 6,100,000–7,099,999 | started 2026-10-01 01:32 UTC; **lost** unfinished in the same revert |
| Host | `07e6833` | FAT32 1,100,000–2,099,999; NTFS, exFAT, FAT32 6,100,000–7,099,999 | the lost ranges again, on the newest decoders (I52's FAT change among them); started 2026-10-01 05:38 UTC, below-normal priority, heap cap 1 GiB; running |
| Owner's M1 Mac | `bb977d0` | 2,100,000–3,099,999 (all images) | **all seven passed** (1,899–10,815 s per image; `done.txt` on the Mac) |
| Owner's M1 Mac | `02acee6` | 5,100,000–6,099,999 (all but NTFS) | fat12 (1,842 s), fat16 (6,225 s), fat32 (8,646 s), disk-mbr (8,358 s) and disk-gpt (8,606 s) **passed**; exFAT **stopped at round 5,326,394** (256 MiB for a 16 MiB image: exFAT's declared cluster count, held to the volume by `b9c41eb`; replayed by `9347070`, E-I37) |
| Owner's M1 Mac | `2ba114e` | exFAT 5,326,395–6,099,999 | **passed** (2,421 s) |
| Owner's M1 Mac | `bb977d0` | NTFS 3,100,000–5,099,999 (two processes) | **passed** (3,130 s and 3,172 s) |
| Owner's M1 Mac | `2ba114e` | 7,100,000–8,099,999 (all images) | **all seven passed** (2,081–9,271 s per image; `fuzz-c1/mac-v5/`) |
| Owner's M1 Mac | `07e6833` | 8,100,000–9,099,999 (all images) | started 2026-10-01 06:17 UTC on the build with I52's FAT change (`fuzz-07e6833.zip` `4e3979c9947884d55289182b09653df92a703b0423a0a7e38556c6b908b92f3e`); running |

Outputs of the finished runs, fetched from the Mac and the Windows VM, are kept under `fuzz-c1/` with a hash manifest
(`fuzz-c1/SHA256SUMS.txt` `dcf8dc8ed9ab05171bfdf20b62fa802dbd317892081078b113d2d31379d8b570`, 37 files). Passed so far,
each on the decoders of its run: rounds 0–99,999 and 2,100,000–3,099,999 of every image; 1,100,000–2,099,999 of every
image but FAT32 (lost, running again on the host); 5,100,000–6,099,999 of every image but NTFS (not run there); NTFS also
3,100,000–5,099,999; 7,100,000–8,099,999 of every image; 100,000–1,099,999 of fat16. Running: the rest of
100,000–1,099,999 (Ubuntu), 6,100,000–7,099,999 of NTFS, exFAT and FAT32 and the lost FAT32 range (host),
8,100,000–9,099,999 of every image (Mac). The stops since the I37 fixes were allocation findings of I37, fixed and
replayed.

## E-I28-V2 — second finding and fix `bb977d0`

On `98fb594`, NTFS round 56958 made `RecoveryScanner.Scan` throw `ArgumentNullException` from `Number` (the listing's
numbering), outside the per-volume safety net: damage had cleared the root record's in-use or folder flag, and the root
record — which the record loop keeps even without a name — was listed as a file with no name. Fix: the root record is
never listed as an item of its own listing, and the listing's preparation (numbering, lost-file marks) runs inside the
per-volume safety net, so a slip there leaves a warning on that volume instead of ending the scan. The fuzz test now names
the round when the scan throws, and replays round 56958 as well; NTFS rounds 0–99,999 pass on `bb977d0`.
