# E-CUSTODY-20261127-post-release-cleanup — The temp files, test machines and temp branches cleaned after 1.0.0

2026-11-27, after FileCat 1.0.0 was published ([E-PUBLIC](E-PUBLIC-1.0.0-20261127.md)). The owner asked to clean up the test machines, the leftover temp files and every temp branch, local and remote. The session kept everything a record names: before each removal it searched `docs/` for each name, as a path and as a bare name. A name counts as cited if it appears anywhere, even inside a longer name; that keeps some generic names too. On the test machines the owner approved the list rule: "Delete them (Recommended)". **No sealed evidence was touched:** `V:\FileCat\artifacts`, the REP sealed there and the evidence store stay as they were.

## Branches

- **Remote:** 47 branches deleted; `main` and `release/1.0` stay.
  - 45 were fully contained in `main` or `release/1.0`.
  - `release-tools/promote-stable` (tip `2bce7cbb7`): its files are identical on `main`, which got the same changes through other commits.
  - `docs/rc3-owner-walkthroughs` (tip `02e3c3165`): the rc.3 draft of the walkthrough and GA PC checklist, which `main` added itself in `e9bddbca7` and has developed since.
- **Local:** 83 branches deleted; `main` stays.
  - 82 were contained in `main`, `release/1.0` or a deleted remote branch above.
  - `claude/osi-sharpcompress-trim-wip` (tip `ab5520ae7`): an uncompiled WIP commit of 2026-10-29. The owner chose to delete it ("Delete it (Recommended)").
- **Worktrees:** nine removed, each clean: two app worktrees under `.claude/worktrees`, `.tmp/rc5-src`, `.tmp/rc9-src`, and the five `.tmp/wt-*`. An empty app folder (`agent-a76aeb83…`) is held by a process and stays.
- **Tags** stay, the RC tags and `v1.0.0` included.

## The release session's host

- **The staged REPs of rc.5, rc.6, rc.8, rc.9 and rc.10** (`.tmp/rep-1.0.0-rc.N`, about 19 GB), which [E-REP-RC5](E-REP-RC5-20261126.md) to [E-REP-RC10](E-REP-RC10-20261127.md) name as staging places. Before removal, every file of each was found with the same SHA-256 and size in the sealed REP `V:\FileCat\artifacts\FileCat-1.0.0-rc.10-REP`, at its own path or under `carried/rcN/`:
  - rc.9: 2,739 files; rc.8: 2,312; rc.6: 1,472; rc.10: the same 3,266-line manifest;
  - rc.5: 969 files, all there but its `REP-README.txt`, which rc.6 rewrote when carrying it. rc.5's original guide is committed byte for byte in `v1.0.0-rc.5/REP-README.txt` (SHA-256 `bd212c018a28d18f…`).
- **The working folders `fq-rc5` to `fq-rc9`** (about 7 GB): no record names them, and their evidence is carried in the sealed REP. Two scripts that the session's notes still use, `wait-run.sh` and `Run-Ac11.ps1`, were moved to `.tmp/release-scripts` first.
- **`fq-rc10`: the copies only** (about 4.4 GB):
  - the downloaded and promoted asset copies, byte-identical to the immutable public release `v1.0.0` (the public check);
  - the files the sealed REP holds.

  The files that [E-PROMOTION](E-PROMOTION-1.0.0-20261127.md) and [E-PUBLIC](E-PUBLIC-1.0.0-20261127.md) name stay where they say: `public/`, `promotion/promotion-report.json`, `promotion.log`, `public-check.log` and the public check's outputs, and `sealed-verify.txt`. **This corrects two sentences of those records:**
  - E-PROMOTION's "the downloaded copies, 1.7 GB";
  - E-PUBLIC's "the downloaded assets".

  Both copies are removed; release `v1.0.0` itself holds the same bytes.
- **2,821 other `.tmp` entries** (943 MB), which no record names as a path or a bare name: random-named temp files, patch bundles, logs and scratch folders. The 40 that a record names by bare name stay.
- **The test build in `E:\tmp`:** `FileCat-4c43d63b` and the contents of `FileCat`, the unsigned test Setup of the owner's retest and its publish folder. The empty `E:\tmp\FileCat` folder stays: a safety check stopped the session from removing `E:\tmp`.
- **What stays in `.tmp`** (about 17 GB, 333 entries): every cited item. That includes the Hyper-V ACL guest's files (`acl-validation`), the EFS live-test files, `i602-vm`, the release-native builds the records name, the Hyper-V and Ubuntu harnesses (`fq-rc2`) and `release-scripts`. E: went from 147 GB free to 186 GB.

## The test machines

- **Ubuntu VM:**
  - 416 entries in `~/FileCatValidation/aw-claude` that no record names, about 2.6 GB: mostly the old candidates' package copies (`fq-rc3` to `fq-rc9`, `public-1.0.0`), builds and scratch;
  - FileCat's test folders in `/tmp` (1.4 GB).

  Deletions stayed on one file system, and nothing was mounted there. The GUI harness and its results (`fq-gui`), `fq-rc2`, `packets`, every cited evidence tarball, the .NET SDK and the NuGet cache stay. 35 GB used, 152 GB free.
- **Mac:** 3,340 entries in `~/FileCatValidation` that no record names, mostly old clones, builds and test runs (0 not removed). The cited folders, the .NET toolchain, `FileCatCapture.app` and the GUI harness stay. Disk afterwards: 630 GiB free, from 492 GiB.
- **Windows qualification VM (FileCat-FQ-W11),** inside the OneDrive guard, which found OneDrive closed and unchanged before and after:
  - 41 entries of `C:\FQ`: copied assets, builds, runtimes and logs whose results were copied to the host. `gui`, `guard` (the OneDrive baseline), `vhd` and `input` stay.
  - The test account's 13 FileCat test profiles (`fq-*`).
  - The I689 test disk T:, detached from the running VM and its file `i689-scan.vhdx` deleted.
