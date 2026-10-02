# E-V03-CLONE-1 — ReFS/Dev Drive and same-server SMB copies

Preliminary native development-build evidence for plan §9's large-copy controls (account for ReFS cloning and
SMB server-side copies). No candidate or installed release artifact is qualified.

## Environment and interlocks

Owner-lent VMware Windows 11 Insider 26300 guest, `DESKTOP-A60F1NE`, elevated local administrator, .NET 10.0.5.
The original boot/system disk is excluded. The old ignored `artifacts/vm/win-refs-clone.ps1` was **not executed**:
fixed Q:/share names, continuation after setup failures, and reuse/deletion without identity binding were inadequate.

Tracked replacement: `eng/validation/Invoke-ReFsCloneVm.ps1`. Each run refuses a pre-existing GUID root or share,
verifies guest manufacturer/name and bundle SHA-256, creates a new 60 GiB expandable VHDX, and resolves its exact
image-to-disk mapping. Before format/cleanup it rechecks file-backed bus type, size, non-boot/non-system flags,
disk identity, ownership marker and absence of reparse ancestors. Formatting uses that disk's partition object.
Successful VHDX deletion follows verified detach; failed fixtures and logs are retained. Owned SMB shares are removed
only after their paths match. No existing physical/system disk, fixed drive letter or ordinary data was selected.

Wrong-guest and wrong-bundle controls were both refused, with original disk identities unchanged and no fixture root
created. The first harness run stopped before initialization on a PowerShell CIM bus-type conversion; `2a58fdb`
uses the raw CIM value. Its empty failed disk was independently identity-checked and detached, retaining the VHDX.
The second attempt stopped after a refused plain-ReFS format: CDXML Storage commands needed explicit
`-ErrorAction Stop` to reach the fallback. `021a885` corrects this. Failed disks were detached and retained.
The third attempt used the verified same-partition Dev Drive fallback after plain ReFS was refused.

## Oracle and observed cases

`CloneCopyTests` generates a seeded, incompressible 1 GiB source (seed 1603), logs its SHA-256, copies by buffered
CopyFile2, unbuffered CopyFile2, and the FileCat job, measures elapsed copy time and free-space delta, hashes each
copy, changes its first byte and verifies the original's full hash remains unchanged. A clone-space assertion allows
less than one quarter of the source size; actual local deltas were **0 reported MiB** for all three paths.
Failure retains the test root; a skipped test cannot count as passed in the harness.

| Attempt | Build / harness source | Result |
|---|---|---|
| `refs-clone-20261002` | `a5a3c0c` / `a5a3c0c` | Setup guard stopped before initialization; no copy case executed |
| `refs-clone-20261002-r2` | same build bytes / `2a58fdb` | Plain ReFS refusal stopped qualification; no copy case executed |
| `refs-clone-20261002-r3`, local | same build bytes / `021a885` | **1 passed, 0 failed, 0 skipped**, 276.184 s including generation/hashing; copies 14 / 69 / 220 ms, 0 reported MiB each; bytes and copy-on-write checks passed |
| `refs-clone-20261002-r3`, SMB | same build bytes / `021a885` | Failed native volume query before any copy (I97) |
| `refs-clone-20261002-r4`, local | `ca1afe0` / `ca1afe0` | **1 passed, 0 failed, 0 skipped**, 256.391 s; copies 9 / 83 / 223 ms, 0 reported MiB each; bytes and copy-on-write checks passed |
| `refs-clone-20261002-r4`, SMB | `ca1afe0` / `ca1afe0` | **1 passed, 0 failed, 0 skipped**, 38.622 s; copies 20 / 111 / 213 ms, 0 reported MiB each; bytes and copy-on-write checks passed |

Third attempt identity: VHDX root `C:\Users\Public\FileCat-refs-fe82872741124f95bd8f10acc3319637`, disk 1,
UniqueId `60022480BAA053B4EA305382B161443D`, size 64,424,509,440 bytes, file-backed virtual, non-boot/non-system;
volume `\\?\Volume{62294f4c-de24-45eb-8100-3a5fad9bc240}\`, label `FCCLONE_fe82872741124f95`, ReFS,
serial `24E74F68`. Original disk 0 remains the system disk. Source SHA-256:
`3187ef60c755b2a95e4fc5d9b27f1912d50f1b8a0baef426eadc8547aa47996a`.

## I97 — UNC root rejected before the SMB test reached copies

The test used `Path.GetPathRoot`, which leaves a UNC share root without a final backslash. An independent native
probe on the same identity-checked VHDX and a new owned share reproduced **Win32 123 without the separator** and
success with it (serial `24E74F68`, flags `1CC610CE`, including block-refcount support). The successful call's stale
last-error field is not an error verdict. [Microsoft's documented root syntax](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationw)
requires the trailing separator (read 2026-10-02). `ca1afe0` normalizes the root and logs the native error on failure.
This is a test setup defect, not a demonstrated FileCat copying defect. The corrected local and SMB cases both passed
without skips, including all three copy paths, byte verification and copy-on-write checks. Four-lane CI 36989898493
at `ca1afe0` also passed; its three release package jobs skipped.

## Provenance and limits

First/third bundle source: `a5a3c0cb68dd0868e8642565dcf49cc93ddecee4`, Release framework-dependent publish.
Bundle SHA-256 `b37613e4628530f8d10c88de31b64add39bc744504e48082cd045dba15f89c8a`; test EXE
`670f9039344b4dbb7651f859cbce505e9e002d8c523b7850a154ddd279cf3684`; test DLL
`de467907c6fb5af32e34af1ef99af9fdd08f344348ffd3bb94ef42e86554b274`.
Third harness SHA-256 `b66293388168516a8e746a24f9cbbf670d85034b34cbf004371872e84501113d`, from
`021a885be619e72e13eebe1467f5611e5cb614da`. Each attempt's local `manifest.json` records its full source IDs,
bundle/binary/harness hashes, guest root and run ID. The fourth publish had only release-record documentation changes
outside its committed source; the manifest records that working-tree delta explicitly. No source patch is hidden in it.

Raw roots: `artifacts/release-evidence/refs-clone-20261002{,-r2,-r3,-r4}/`. Third local TRX SHA-256
`e29fdd7e71f4992106fc790b7df9c6edcb2b405b424048a2818cb4102d015d0e`; third SMB TRX
`c2474165109f5fbd12f0fceae045c607c97c965152e9f340cfa0f996e737bc13`; independent UNC probe JSON
`5a854ec24bd51d783226417b895ef990a45e4fcf9403a12c3281d0ff138fd00d`.
These local records are not the sealed REP store. A sparse virtual Dev Drive on an Insider guest does not establish
GA, physical-device, general network/server, benchmark or final-package behavior. Copy timings exclude byte verification
and generation; they must not be presented as physical-disk throughput measurements. Candidate reruns remain mandatory.

## Corrected run identity and retained outputs

Fourth run source/harness: `ca1afe0db40c89eb8e65bcb30310223f1377a164`. VHDX root
`C:\Users\Public\FileCat-refs-e1f52fbb7f1f44f8bdc5b112c10a4285`, disk UniqueId
`60022480267D374F8402C9D55341E5E1`, volume `\\?\Volume{1255985b-2f42-4eff-b4e0-06c5da893518}\`,
ReFS label `FCCLONE_e1f52fbb7f1f44f8`, serial `76D29402`. The SMB client reports the same serial and flags
`1CC610CE`; local flags `5FC636CF`. Source bytes have the same SHA-256 as the third run.
Post-run check confirms the successful VHDX and owned share are absent, with only the original system disk remaining.
Failed earlier VHDX files remain detached and retained.

All files below are under `artifacts/release-evidence/refs-clone-20261002-r4/`:

| File | SHA-256 |
|---|---|
| `winplat.zip` | `01cc84784c4516b3a1e16903f059de10c328a4a68c487771d46c9e304a584dcb` |
| Test EXE (in bundle) | `c5e19b777e036c3e6eae12d32a83942cdce5204e24bdbe5f88ca1d462cc71771` |
| Test DLL (in bundle) | `ae55fe92d07787fbc3948346fbc5d223eb1ed61d895e55dbe555fa8741cb360f` |
| `manifest.json` | `e3b553eaf88d986875c2f6e0de6a15de4059a264df29c412f68c2c12e5797d37` |
| `controls.json` | `7e58502e2d566ec65b7041f9fa81507a67937ca49aa16e26de48fb42fd8750a5` |
| `local.trx` | `cf4d4053e6ee24f4f099747edc492a0e70e8497628d9564a1f2732ec24789d5b` |
| `local.txt` | `ef69b6f4725d7a61d143958a9071519a4ae4f9754fb0c6fac457d0b67a6154dc` |
| `smb.trx` | `c31837ed2d333aaa9bbfab5b6fadbf42d23727d36e0c5c530d9242325797ae1f` |
| `smb.txt` | `abf2c507713d21c320683180145c07f079f72c8634f897fd61ecf38f4d2b2384` |
| `harness.log` | `198f34f6c4ffda9376cc3c8df2f046c5b47e16e04b97fe0e4d8f8ef9ed1963e7` |
| `disk.json` | `7f01b58f35bc0cc8477e132ba19e759e43668f102e975462f9f020decd9b7901` |
| `volume.json` | `02153c90db2d1f5fb367b6e77d0f7232de126f370aa7e3fe87ec562efc6df927` |
| `cleanup-check.json` | `dd23a887a98eb8cb30ce33c0a770a7f0d114933c3255523828fd6db9597fbbc2` |
