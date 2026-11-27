# E-CUSTODY-20261126-ubuntu-cleanup — Raw files that records cite were removed from the Ubuntu VM

**When:** 2026-11-26, about 22:50 (the host's time). **Who:** the release session, cleaning the test machines at the owner's request ("clean all machines from leftovers and prepare them for the upcoming work"). **What was lost:** raw run files from 2026-11-07 to 2026-11-20 on the Ubuntu VM that 15 file-system-details records cite. No release candidate's evidence is among them: rc.1 to rc.9 were qualified from copies on the host (`E:\FileCat\.tmp\fq-rc*`) and in the staged Release Evidence Packages.

## What happened

The session ran `fc-cleanup-20261126.sh` on the Ubuntu VM. It extends the rules of the earlier `fc-cleanup-20261121.sh`, and it removed:
- every top-level entry of `~/FileCatValidation` older than three days, except `dotnet-sdk`, `nuget` and `aw-claude`;
- finished run folders, scripts, candidate and source tarballs in `~` older than two days;
- temporary entries in `/tmp` older than a day, and one python crash report.

The VM went from 107 GB to 36 GB used.

Before removing anything on the host and the Mac, the session checked the records for citations of each folder, and it kept every cited one. **On the Ubuntu VM it did not check first.** The check afterwards found:
- **Cited:** 47 of the 630 removed entries are named in `docs/`.
- **Copies:** for 7 of the cited files, a file of the same name exists on the host (for example `orca-find-details-ext4-r1.json` and `ext4-restrictions.trx`); the rest exist nowhere else.
- **Snapshots:** the VM's snapshots all predate 2026-10-02, so none holds them.

## What it changes

- **The 15 records below** keep their transcribed results, counts and hashes. Their pointers to raw files on the VM no longer resolve, and each now ends with a custody note.
- **The disk-space drivers and manifests** under `docs/validation/disk-space/` name `~/FileCatValidation/dsa-batch-20261107-r…` as the folders their runs worked in. Their results are committed beside them.
- **Two committed receipts,** `app-details-extra.trx` and `core-details-extra.trx`, name a removed build folder (`~/FileCat-main-3126e90fe`) only as their test assemblies' location. The receipts themselves are intact.

| Record (under `docs/validation/filesystem-details/`) | Removed entries it cites |
|---|---|
| `2026-11-17-foundation.md` | `~/FileCat-native-20261117-01a1e6d0`, `~/FileCatValidation/filecat-exact-main-85cfc78c-ext4-r1`, `filecat-ext4-capabilities.9g96rAzD`, `filecat-ext4-restrictions-7d2d788a-r1` |
| `2026-11-17-supplementary.md` | `~/FileCatValidation/filecat-linux-inventory-28dcc768-r2`, `~/filecat-supplementary-delete-8eeeca7e-r3` |
| `2026-11-18-darwin-ordinary-attributes.md` | `~/FileCatValidation/ci-fix-3877f7ef` |
| `2026-11-18-efs-hex-save-as.md` | `~/FileCatValidation/filecat-sparse-saveas-6f805f63-r2` |
| `2026-11-18-efs-native-copy.md` | `~/FileCatValidation/filecat-sparse-copy-121afad6-r4` |
| `2026-11-18-efs-recovery-journal.md` | `~/filecat-efs-admission-8eeeca7e-r1`, `~/filecat-efs-history-ui-121707ff-r1` |
| `2026-11-18-efs-remote-upload.md` | `~/FileCatValidation/filecat-sparse-upload-9762f943-r1-live` |
| `2026-11-18-ext4-directory-namespace.md` | `~/FileCatValidation/filecat-ext4-casefold-891f04d9-r4` |
| `2026-11-18-linux-btrfs-properties.md` | `~/FileCatValidation/btrfs-fixture-DfGmU9`, `btrfs-fixture-X0qW1o`, `ci-btrfs-6eea2ee8` |
| `2026-11-18-linux-details-orca.md` | `~/FileCatValidation/details-92d52654.zR6HolZy`, `filecat-exact-main-85cfc78c-ext4-r1` |
| `2026-11-18-linux-fiemap-details.md` | `~/FileCatValidation/filecat-btrfs-extents-984b6f47-r1`, `filecat-xfs-details-98833205-r1` |
| `2026-11-18-linux-fscrypt-policy.md` | `~/FileCatValidation/ci-fix-3877f7ef`, `filecat-ext4-key-status-423311b9-r1`, `fscrypt-ext4.oha8aT` |
| `2026-11-18-linux-mount-details.md` | `~/FileCatValidation/details-92d52654.zR6HolZy`, `filecat-zfs-details-846814b7-probe`, `filecat-zfs-details-846814b7-test-r2` |
| `2026-11-18-supplementary-value-reports.md` | `~/FileCatValidation/ci-fix-3877f7ef`, `details-92d52654.zR6HolZy`, `filecat-report-device-loss-f7e1253c-r1`; `~/filecat-details-output-d2ed94ec-r2`, `filecat-details-publish-8eeeca7e-r1`, `filecat-metadata-report-4f7257ee` |
| `2026-11-18-unix-link-target-details.md` | `~/FileCatValidation/filecat-link-status-53f808d5-ubuntu-r1`, `filecat-unix-link-orca-2785dc13-r7`, `filecat-unix-link-orca-2785dc13-r8`; `~/filecat-unix-target-300743ee-r1` |

The full list of removed entries is kept with the session's working files (`ubuntu-cleanup-removed.txt`), with the map of which document names which entry.

## From now on

The session checks `docs/` for every folder's name before removing it from any machine, and keeps each one a record names.
