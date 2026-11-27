# E-GO-1.0.0-20261127 — The owner's GO for FileCat 1.0.0 on `v1.0.0-rc.10`

2026-11-27, 18:52:33 UTC. Under DEC-01 the owner is the human GO approver. The release session asked through its question dialog, after the owner's decisions of the same day, and the owner answered:
- **Question:** "GO or NO-GO for FileCat 1.0.0 on exactly these values? Candidate v1.0.0-rc.10 (tag object ce3cdaa572b1b465db50744a01c50e0e283e1c3e) at source fa6678c58aa639f62c4bcf793e6e84e6f1027200. Producer manifests SHA-256: Windows 96a6881c82dd87bb6d127f3f381b21da6bfce14e736a7ae46b5abdffecc71ab0, Linux 9cfeb52cc425f68837e42b1ef79d13ffa45af7d320c11e0c0bc6aa4c996f6f93, macOS a6efa45088606bdc49fea5b98f2cf0184e8185f3db121dd628eaf880285433ca. REP manifest SHA-256 e7b806452aab509cc0106b9fb372baaba7a309f09b799d09d45b2c72a1c7c06f. Release notes SHA-256 79cd59a2c4b8ee2d352fd1ddc6f234edd3db7817e44e3b891e19d3da1ffcd3dc. Support: DEC-02's tiers (Windows 11 x64 stable; Windows 11 ARM64, Ubuntu 24.04/26.04 x64 GNOME and macOS 26+ Apple silicon as previews)."
- **Answer:** "GO (Recommended)", the first of the two options, "GO" and "NO-GO". The session recommended GO in the [GO cover sheet](../FILECAT_1_0_GO_COVER_SHEET.md).

## What the GO names

| | |
|---|---|
| Candidate | `v1.0.0-rc.10`, annotated tag object `ce3cdaa572b1b465db50744a01c50e0e283e1c3e` ([record](E-RC10-TAG-20261127.md)) |
| Source | `fa6678c58aa639f62c4bcf793e6e84e6f1027200`, the tip of `release/1.0` (PR #80's merge) |
| Producer manifests (SHA-256) | Windows `96a6881c82dd87bb6d127f3f381b21da6bfce14e736a7ae46b5abdffecc71ab0`; Linux `9cfeb52cc425f68837e42b1ef79d13ffa45af7d320c11e0c0bc6aa4c996f6f93`; macOS `a6efa45088606bdc49fea5b98f2cf0184e8185f3db121dd628eaf880285433ca` |
| REP manifest (SHA-256) | `e7b806452aab509cc0106b9fb372baaba7a309f09b799d09d45b2c72a1c7c06f`: 3,266 files, staged in `E:\FileCat\.tmp\rep-1.0.0-rc.10` and verified ([record](E-REP-RC10-20261127.md)) |
| Release notes (SHA-256) | `79cd59a2c4b8ee2d352fd1ddc6f234edd3db7817e44e3b891e19d3da1ffcd3dc`: `RELEASE_NOTES_1.0.0.md` on `docs/release-notes-1.0.0` at `3d38c787`, with line feeds as line ends and no line break at the end, as `eng/Promote-StableRelease.ps1` checks it |
| Support decisions | DEC-02's tiers: Windows 11 x64 stable; Windows 11 ARM64, Ubuntu 24.04 and 26.04 x64 with GNOME, and macOS 26 or later on Apple silicon as previews |

## What it rests on

- **The GO cover sheet** at main `e8628cf3`, which recommended GO.
- **The owner's steps,** all passed:
  - the consent walkthrough (Windows W1 to W7 on rc.6, carried by the owner's acceptance; Ubuntu and Mac on rc.8 and rc.9);
  - the GA PC checklist on rc.10;
  - AC-11's phone Move.
- **The owner's decisions of the same day:**
  - the GA PC run's build and Smart App Control state;
  - W1 to W7's carry-over;
  - I677 shipping open and named in the release notes;
  - the other 16 proposed dispositions.
- **The register and the ledger:**
  - the register: 667 remediated, 19 closed, 4 unresolved before the GO;
  - the acceptance ledger: 72 rows Accepted, the rest waiting for the GO or following it;
  - the evidence index.

## At the GO

- **I03 and I10 close,** as the owner accepted: I03 on rc.10's manifests, SBOMs and REP; I10 with the final release notes. The register then has 667 remediated, 21 closed and 2 unresolved: I18 closes after the promotion and the public download checks, and I677 is the known issue.
- **REL-I03-PROVENANCE is Accepted.**

## What follows, in order

1. **The owner seals the REP** (DEC-10): `pwsh -File E:\FileCat\.tmp\release-scripts\Seal-Rep.ps1 -ManifestSHA256 e7b806452aab509cc0106b9fb372baaba7a309f09b799d09d45b2c72a1c7c06f`.
2. **The owner creates the annotated tag `v1.0.0`** at `fa6678c58aa639f62c4bcf793e6e84e6f1027200` and pushes it.
   - Its CI's first job refuses to build a stable tag.
   - The session checked that guard before the tag: `eng/Assert-ProducerReference.ps1` at the source throws "Stable references cannot invoke a producer" for `refs/tags/v1.0.0`, and every build job depends on that job.
3. **The session runs `eng/Promote-StableRelease.ps1`** with the values above. It copies the candidate's 24 assets into the private draft "FileCat 1.0.0" and checks them.
4. **The owner reviews the draft and presses Publish release.**
5. **The session runs section 13.3's public download checks.**

Corrections after this record are additive successor records (the plan's section 12.5).
