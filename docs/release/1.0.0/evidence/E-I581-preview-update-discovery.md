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
