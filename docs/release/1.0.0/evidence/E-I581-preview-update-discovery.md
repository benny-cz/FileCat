# I581 — preview update check missed published prereleases

**31 October 2026.** Medium UX/truthfulness issue, remediated preliminarily at product `eb66d756`. Public preview.4 is an unsigned prerelease, not a stable 1.0.0 candidate.

## Consequential reason to revisit an accepted case

[AC-S2-057](../../../validation/UX_UI_ACCEPTANCE_LEDGER.md#ac-s2-057) accepted the Settings/privacy and manual Help route on 27 October when no release existed. Public preview.4 was subsequently published. GitHub's official [latest-release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release) excludes prereleases, so the same request no longer answered the user's actual preview-update question. This changed publication state is the sole reason for reopening the update-result boundary; the accepted preference, network disclosure and NVDA cases were not repeated.

On 31 October, unauthenticated `GET /repos/benny-cz/FileCat/releases/latest` returned HTTP **404**, while the exact preview.4 tag endpoint and release list returned **200**. The old `UpdateCheck.CheckAsync` always called `/latest`. In the disposable Ubuntu 24.04.5 Wayland VM, the old Git-built app at `f78638f03b297133416da5ebdcc39f3f0afbdc4c` (App DLL SHA-256 `4edd607c0e81d02729cee49134909628ceefbaba9e744eee05a3c5fc31c8b4d9`) ran the enabled `app.checkUpdates` command and visibly said **“No release has been published yet.”** [Before screenshot](../../../validation/evidence/exploratory-20261031-update-before.png), SHA-256 `2eb683bab7e80d411154a4442a62439fa9a033fb5bddb3cda9d9ae58a8fc7063`.

## Correction and bounded check

Preview builds now query GitHub's bounded release list, reject drafts/unpublished or malformed releases, choose the highest valid version, and offer only a FileCat-owned HTTPS release page. Stable builds keep the stable `/latest` channel and now say **“No stable release has been published yet”** on its 404. Numeric prerelease components compare numerically, so preview.10 follows preview.9. The check still only notifies; it does not download or install. The automatic preference and daily gate remain intact.

The isolated VM worktree was at `bf1155e5b94213631b02bb45cb55071c0913e2f8` plus only the two product files in the committed `eb66d756` change. Its patch SHA-256 was `7d490a53f1ba3f41ccdfdefb937088fdcf9fc7c7e1802b685faa3752c28c09a7`. The corrected Release build had zero warnings/errors and App DLL SHA-256 `e606aaf0f54a82b86e188fe16712cdf838907c5b61ed4bdf6a011c5b51e62ad8`. With a fresh `--data` directory, the same native command showed **“FileCat 1.0.0-preview.4 is available; this is 0.1.0-preview”** and offered **Cancel / Open release page**; no page was opened. [After screenshot](../../../validation/evidence/exploratory-20261031-update-after.png), SHA-256 `ef75142632583881dc5cdbcc683f36e9e7b9e4999778ced0fdc144bf43ac1699`. The VM lock screen initially blocked input; after unlocking the disposable session, both actual dialogs were observed. Each test-owned process and state directory was removed.

Focused Windows source tests pass **3/3 App** (scheduled preference, stable/preview channel, draft and URL controls) and **12/12 Core** release-version cases. The latter include preview.9→preview.10. The native screenshots establish the visible change, while the tests establish the stable-channel and adverse-response behavior. No installed package or signed final candidate was used.

## Finite candidate stopping rule

On the exact installed 1.0.0 candidate, use Help → Check for updates and the disabled-automatic preference: the explicit check must finish with a truthful stable-channel result, without downloading/installing or a surprise startup request. For the preview path, one exact installed preview build must report a newer public preview when one exists, or its own current version when none is newer; a draft must never be offered. Compare the offered URL to the exact FileCat release page. Stop after those affected paths and latest-product five-lane CI with skip reconciliation. Do not repeat the broader AC-S2-057 Settings/NVDA campaign absent a new observation.

Stable 1.0.0 remains **NO-GO** pending the independent signing and frozen-candidate gates.

## Exact-source CI and skip reconciliation

[Main run 41207536822](https://github.com/benny-cz/FileCat/actions/runs/41207536822) at exact product `eb66d7568d60a0c667f17ece0a818b74e213bfb0` passed the producer, Windows x64, Windows ARM64, Ubuntu and macOS jobs. Linux/macOS/Windows package and private-draft jobs skipped by design on `main`. Its 20 downloaded TRX files were compared by lane, test name, outcome and multiplicity with 20 TRX from prior green [run 41204821400](https://github.com/benny-cz/FileCat/actions/runs/41204821400) at `bf1155e5`. Prior: **41,246 Passed / 1,535 NotExecuted / 0 Failed**. Current: **41,266 Passed / 1,535 NotExecuted / 0 Failed**. Each lane adds the one App preview-channel test and four Core prerelease-order rows; no prior pass was lost and no new skip appeared.

| Lane | Prior official artifact ID / SHA-256 | Current official artifact ID / SHA-256 |
|---|---|---|
| Windows x64 | `13045335739` / `d178e330c5d3992534a556b80e97c996bffc6a9709b1e27df5e2e3fafa4c3581` | `13046276361` / `6f3308dd3f295c68305ef4fa2c7eaac0621b47a0dc1a7d26813485da3f23ad90` |
| Windows ARM64 | `13044822830` / `fabee93b6b9e2852f230f60d3c9ec95c1bad548c88d1df944e110dd7b58a3688` | `13045857027` / `82fd356e6b1dbcb8a4b2ac192e7b9b2d865c75c9cc9a89ebafcee736a72724a0` |
| Ubuntu | `13044731913` / `ab2b0c96c0b469f78bfffcfbe1ba7a6ad0524c36f692cdfd0b76effcf3259d8f` | `13045986758` / `fba9b4fbaefd5f45bf76ba3bfc10fb727ecfb8e959c9da3c90f4f0db5979e1ee` |
| macOS | `13044868004` / `67a944ce06b3d5e7b1ef5c5fa83965a4240933310604c052d0ff90029f2d955c` | `13046396606` / `2fae349335e8b9aab5409d5ddcd46388d62d029e538be6c0369ecd462ce0ce8d` |

The digests in the table are GitHub's reported artifact digests. The 40 downloaded TRX, comparison script and output are retained locally in ignored `.tmp/i581-ci`; script SHA-256 `db20f7bc2a9f7e4c2ea41ec857ec30aad1a987cf0e143714d4d9addbcbab4aa9`, output SHA-256 `4d4f814d2ec9bc43bb3c77280df42d6235f0eae0607c62f7afe68fddbe5252a5`. This is an independent outcome comparison, not a rehash of the original server ZIP bytes. Later docs-only [run 41207844625](https://github.com/benny-cz/FileCat/actions/runs/41207844625) also passed the same five required jobs with tag-only skips. The exact installed-candidate checks above remain Open.

The subsequent loop-cursor documentation-only [run 41208496513](https://github.com/benny-cz/FileCat/actions/runs/41208496513) at `3a489af6` passed producer and the same four platform jobs; its four package/draft skips are likewise expected. This adds no product-source change or candidate acceptance.
