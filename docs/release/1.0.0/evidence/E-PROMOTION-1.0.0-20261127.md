# E-PROMOTION-1.0.0-20261127 — After the GO: the sealed REP, the `v1.0.0` tag and the private stable draft

2026-11-27 (UTC). The steps that follow the owner's [GO](E-GO-1.0.0-20261127.md), in its order. This record adds to the GO record and changes nothing in it. **The release is not published:** the owner reviews the draft and presses Publish release.

## 1. The owner sealed the REP (DEC-10)

- **The owner ran** `Seal-Rep.ps1 -ManifestSHA256 e7b806452aab509cc0106b9fb372baaba7a309f09b799d09d45b2c72a1c7c06f` on the release session's host. The owner pasted its output:
  - three checks, each `RESULT fail=False files=3266/3266 manifest-sha256=e7b806452aab509cc0106b9fb372baaba7a309f09b799d09d45b2c72a1c7c06f`;
  - then `Sealed: V:\FileCat\artifacts\FileCat-1.0.0-rc.10-REP`.
- **Access, read by the session:** the folder no longer inherits its parent's rules. The owner's account has full control; SYSTEM, Administrators and Users may only read and execute.
- **The session's independent check:** `verify_rep.py` on the sealed copy gave 3,266 of 3,266 files matching the manifest, with the manifest digest above (`sealed-verify.txt`, SHA-256 `87d017060724eab4…`). That is REL-REP-CUSTODY's stopping condition.

## 2. The owner created `v1.0.0`

- **The tag:** annotated tag object `42dec55b173d52254f216c6efb3fb733122a23e3`, tagger `benny-cz`, 2026-11-27T19:03:55Z, message "FileCat 1.0.0". It peels to the source the GO names, `fa6678c58aa639f62c4bcf793e6e84e6f1027200`, locally and on GitHub.
- **The push** was the owner's. GitHub answered "Bypassed rule violations for refs/tags/v1.0.0: Cannot create ref due to creations being restricted": the admin bypass of the tag ruleset, as for the RC tags.
- **The stable tag's negative control:** the push started [run 46054331772](https://github.com/benny-cz/FileCat/actions/runs/46054331772) from the source's workflow.
  - Its first job, "Producer reference policy (stable blocked)", failed at "Refuse stable tag production" with "Stable references cannot invoke a producer".
  - All 17 other jobs were skipped. Nothing was built, packaged or signed.
  - The session had checked this guard before the tag (the GO record).

## 3. The session promoted rc.10's bytes to the private draft "FileCat 1.0.0"

`eng/Promote-StableRelease.ps1`, without `-DryRun`, with the values the GO names, ran from 19:07:30Z to 19:16:15Z. It is the version whose dry run passed on rc.5 ([E-REL-PIPELINE-PROMOTION-20261126](E-REL-PIPELINE-PROMOTION-20261126.md)), unchanged since then and at the source.
- **Before its first write:**
  - the notes (30,329 bytes) had the SHA-256 the GO names and no placeholder;
  - `v1.0.0-rc.10` and `v1.0.0` both peel to the source;
  - the three manifests have the SHA-256 values the GO names, name version 1.0.0, `v1.0.0-rc.10` and the source, and list the 21 other assets;
  - all 24 assets of candidate draft 460882817, downloaded, matched GitHub's digests and the manifests' pins.
- **The draft:** release 460975687, "FileCat 1.0.0", created private, of the stable kind, for `v1.0.0`, with the notes as named.
  - Each of the 24 files was uploaded once.
  - Read back, the draft's kind, tag, commit, title and notes were as named.
  - All 24 assets, downloaded again by their new IDs, equal the candidate's bytes.
  - Result: "draft ready for the owner to publish".
- **The session's own check afterwards, through GitHub's API:**
  - release 460975687 is a draft and not a prerelease, for `v1.0.0` at `fa6678c5`, titled "FileCat 1.0.0";
  - its notes' SHA-256 is the GO's;
  - its 24 assets have the candidate's names, sizes and GitHub digests, all uploaded;
  - it is the only release for `v1.0.0`;
  - the candidate draft 460882817 is unchanged.
- **Immutable releases** are enabled for the repository, so the release becomes immutable when it is published, as preview.11 did.
- **Kept on the release session's host** (`E:\FileCat\.tmp\fq-rc10`), outside the sealed REP:
  - `promotion/promotion-report.json`, SHA-256 `6288725332f3bd34…`;
  - `promotion.log`, SHA-256 `d6e3c06cc2c32078…`;
  - the downloaded copies, 1.7 GB.

## What follows

1. **The owner** reviews the private draft "FileCat 1.0.0" (Releases, at the top) and presses **Publish release**.
2. **The session** runs section 13.3's public download checks:
   - `eng/Check-PublicRelease.ps1`, anonymously;
   - a clean install from the public Setup with c1's script;
   - a preview's update notice (I581).

   Then I18 closes, and REL-PIPELINE and REL-GO-PUBLISH are Accepted.
