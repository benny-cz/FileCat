# E-REL-PIPELINE-PROMOTION-20261126 — Manifest-only promotion to the stable release: the tool, its refusals and a dry run on rc.5

2026-11-26. For REL-PIPELINE (checklist d14) and I18: "a negative and positive test of manifest-only promotion to stable". Until now no stable promotion existed. `eng/Assert-ProducerReference.ps1` refuses to build from a stable tag ("Approved manifest-only promotion is not configured"), and nothing copied a candidate's bytes to a stable release. The plan's section 13.1 asks for exactly that: a draft "FileCat 1.0.0" holding the qualified candidate's bytes, uploaded and then downloaded again and compared, and published only by the owner.

## The tool

[`eng/Promote-StableRelease.ps1`](https://github.com/benny-cz/FileCat/blob/db001ce4bbcc0d65fb655a5be180a3e03e93f7b9/eng/Promote-StableRelease.ps1) at `db001ce4`, on branch `release-tools/promote-stable` (its first commit `3a90885c`, then a check of the draft before the uploads). It is not on main yet: it changes no product file and nothing in CI runs it, so it waits to land with the next change that needs a full CI run.

It copies the exact bytes of a candidate's private draft (`v1.0.0-rc.N`) into a new private draft "FileCat 1.0.0" for `v1.0.0` at the candidate's source commit. It builds, signs and repackages nothing, and it never publishes.

**Before its first write it checks, and stops at the first failure:**
- The release notes, with line feeds as line ends and no line break at the end (the text the release keeps): the SHA-256 the GO names, and no open `[[...]]` placeholder.
- The candidate tag peels to the source commit the GO names. So does `v1.0.0`, which a real promotion needs, annotated; a dry run may run before it exists.
- No release for `v1.0.0` exists, as a draft or published.
- The candidate release is the draft prerelease of the candidate tag.
- The three producer manifests come first: their SHA-256 must be the values the GO names, and they must name version 1.0.0, the candidate tag and the source. Then every other asset: it must be one that a manifest lists, match GitHub's digest, and match its manifest pin by size and SHA-256.

**Then it writes:** one private draft, and each file uploaded once. Right after making the draft, and again after the uploads, it reads the draft back: still a draft, not a prerelease, the tag `v1.0.0`, the title, the source commit where `v1.0.0` does not exist yet, and the notes' SHA-256. Every asset must be complete and match by name, size and GitHub's digest; it downloads each one again by its asset ID and compares it with the candidate's bytes. Its report (`promotion-report.json`) lists both asset IDs of each file.

A dry run (`-DryRun`) makes the same draft under the title "FileCat 1.0.0 (promotion dry run: never publish)" and deletes it at the end, or when a step fails. A real promotion that fails after creating its draft leaves the draft for the owner to review.

## Refusals: the negative controls

Run 2026-11-26 at 01:37 CEST, on `db001ce4`, against rc.5's private draft 458724408, each from one valid set of values with one value changed. Every run stopped before its first write, and after each no release for `v1.0.0` existed.

| Control | Changed | Refusal |
|---|---|---|
| N1 | The Windows manifest's SHA-256, last digit | "FileCat-1.0.0-windows-assets.json has SHA-256 1f995eb1…a3881, not the 1f995eb1…a3880 the GO names", after downloading that manifest alone |
| N2 | The source commit (rc.4's) | "v1.0.0-rc.5 does not peel to 16f9379a…" |
| N3 | The real notes, which still hold their two `[[…]]` GO lines, with their correct SHA-256 | "The release notes still hold an open [[...]] placeholder" |
| N4 | The notes' SHA-256, last digit | "The release notes' SHA-256 is a6bf5b54…a0ef, not the a6bf5b54…a0ee the GO names" |
| N5 | A real promotion before `v1.0.0` exists | "v1.0.0 does not exist yet: the owner creates it at 19002592… before the promotion" |
| N6 | The candidate release: the signing records' draft 458724105 | "Release 458724105 is not the draft prerelease of v1.0.0-rc.5" |
| N7 | The candidate tag: `v1.0.0-rc.4` | "v1.0.0-rc.4 does not peel to 19002592…" |

## The duplicate refusal and the dry run: the positive control

Notes: a copy of the release notes with their two `[[…]]` GO lines replaced by "(promotion dry run: this text is filled at GO)", SHA-256 `a6bf5b54383e317f8a36077d52df01792df0fadadac7514da214faf1daa1a0ef` by the tool's rule.

**First attempt, on `db001ce4`, stopped safely.** The candidate's 24 assets downloaded and matched. Then the tool made private draft 458818928 and read it back before uploading anything, and the notes' SHA-256 differed: `3f9039ac…`. GitHub had kept the notes as sent; the tool had decoded gh's UTF-8 answer on the console's code page 437, which garbled the notes' "→", "…" and "·". Decoding the notes' UTF-8 bytes as code page 437 gives exactly `3f9039ac…`. The tool deleted the draft. `676cd00d` reads each gh answer as UTF-8.

**Second attempt, on `676cd00d`, passed** (2026-11-26, 02:00:07 to 02:04:49 CET):
- The notes, the tags, the three manifests and then all 24 candidate assets (867,785,985 bytes) checked out, by GitHub's digests and the manifests' pins.
- Private draft 458821313 was made for `v1.0.0` at `19002592` and read back before any upload: a draft, not a prerelease, the dry-run title and the notes' SHA-256.
- All 24 files were uploaded, each once, under their own names. Read back, the draft held exactly those 24, each complete, with the candidate's size and GitHub's digest equal to the candidate's SHA-256. Each one downloaded again by its new asset ID matched the candidate's bytes.
- The tool then deleted the draft, and a check afterwards found no release for `v1.0.0`.

**N8, the duplicate refusal:** started while that draft existed (02:01:53). It refused before its first write: "A release for v1.0.0 already exists (ID 458821313); a promotion makes the only one".

Nothing was published, and no tag was made.

## What the real promotion adds after the owner's GO

1. The owner makes the annotated tag `v1.0.0` at `19002592`. Its push starts CI from that commit's workflow, whose first job must refuse: "Stable references cannot invoke a producer". That red run, building nothing, is the stable tag's negative control. Checked from that commit's `ci.yml` on 2026-11-26: all 18 jobs need `producer-policy`, directly or through others, and none has a job condition with `always()`, `failure()` or `cancelled()`. So when its last step, "Refuse stable tag production", fails, every package, draft and signing job is skipped.
2. The tool runs without `-DryRun`, with the values the GO names, and leaves the private draft "FileCat 1.0.0" with the 24 assets.
3. The owner reviews the draft and presses **Publish release**. Then section 13.3's public download checks run.

## Kept

The run logs, the refusal list and every `promotion-report.json`, but not the downloaded copies of the assets, go into the Release Evidence Package under `ci/promotion/`.
