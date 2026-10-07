# I202 — directory comparison lifetimes, admission and truthful outcomes

**Preliminary remediation sealed on committed 6e9dadf12f873fe267990d632531870342a478d2; original CI 37681607535 attempt 1 is sealed partially: Windows x64/ARM64 and macOS pass; Ubuntu package installation fails with exit 124.** This is not a release candidate. Parent I06 and campaigns V12/V13/V16 remain open.

Original product: 5957b8005f089ec5a9771044b6cadbfcc0c6f547 (unchanged f59343d runtime). The final baseline exports all 1130 canonical Git blobs and adds only the two test fixtures; it reproduces 30 failures and two responsive positives across 32 cases. Actual production core methods and MainViewModel directory comparison/modal/preview routes run with owned providers, real file contents and independently checked hashes. This is Windows headless component evidence, not a native desktop/hardware interaction qualification.

| Reproduced gap | Corrected behavior and control |
|---|---|
| Seven mark or recursive comparisons enter a held provider outside shared device queues. | Opens, lengths, short reads, tree enumeration and one-sided folder snapshots share device workers. Tests cover left/right, same/separate device keys, open/read/enumeration holds, an independent device, shutdown and ownership. Admission remains asynchronous between calls; it does not reserve a pool thread per queued comparison. |
| A canceled short read continues 129 calls on each side, and already-canceled unequal contents probe metadata. | Cancellation is checked before metadata, between every short read and before another side/result. Active calls retain sources and buffers until return; idle canceled sources can close. |
| Old results mark/label refreshed, navigated-away-and-back or closed tabs. | Captured listing generations and close events cancel that comparison; both tabs must still match before either marks or label publishes. |
| A closed recursive preview publishes its late result. | Closed previews suppress queued progress, results and actions; their token source is released after the worker finishes. Result-set captions retain captured source paths. |
| Missing content readers and provider enumeration warnings count as equality; an open failure escapes compare-and-mark. | Requested but unavailable content is unknown and marked; tree warnings retain an unknown folder result; unreadable content stays unknown. Mark comparison requires complete listings without read errors. |

Working and exact clean committed runs each pass 39 Core and 48 App cases (32 new and 55 existing), with one existing `CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` skip: `FILECAT_COMPARE_BENCH=1` is not enabled. Case names/outcomes match across working and clean runs. Clean qualification preserves all 1135 raw Git blobs/modes/archive and 1869 retained actual payload references across fixture/discovery/working/clean receipts. The independent reader reconstructs 96 final baseline/working/clean observations and checks source ownership, cancellation, publication, worker identity, bytes and counters.

The batch also bounds Linux CI dependency preparation after original I201 run 37673555486 reached its 30-minute limit in the combined package/keyring/test step. That log does not identify which subcommand stalled. One visible dependency setup replaces repeated quiet package refreshes, with two bounded index attempts, one bounded noninteractive installation and a bounded private keyring start. Five actual-launcher owned-command controls cover success, retry, repeated failure, installation failure and a real timeout. They pass again from the exact Git export. They do not qualify a native package installation; original CI must provide that evidence; its first run now identifies an Azure-mirror package download timeout. No required test is skipped by this change.

Earlier attempts remain retained: missing interface methods and ambiguous Avalonia type names failed fixture compilation; direct tab disposal contaminated close cleanup, corrected to actual panel closure; two working assertions counted safely disposed idle sources as active; the initial regression filter omitted the six new Core cases. The corrected filter runs all 32. A clean preflight failed before any build/test because private fixtures had CRLF and canonical Git had LF; the fresh export accepts only that explicit normalization and preserves both hashes. No earlier result is overwritten or silently promoted.

## Current native setup follow-up

Original 37689672199 attempt 1 at 2600e3c passes all 102 executed I202 additions and the corrected macOS admission subset, while Ubuntu's native install again exits 124. This time the marker passes and the configurator runs with zero replacements; APT's active `/etc/apt/apt-mirrors.txt` remains outside its target set and attempts Azure first. The exact original logs, digest-bound artifacts and case-level qualification are retained in [I203](E-I203-directory-content-evidence.md#original-follow-up-ci--2600e3c). The f2679bc correction adds only that owned runner mirror list, validates every target before writing and preserves every non-mirror byte. Nine actual CLI controls pass from committed helper bytes, including the new mirror-list and late-symlink controls. The old canonical helper reproduces the missed mirror-list regression. [I204](E-I204-archive-transfer-warnings.md) holds the exact receipts. Native installation and final candidate qualification remain open; the five-minute install bound and required package/test set are unchanged.

Private `FileCatReleaseEvidence/dc202-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| independent-directory-working-v2.json | 450db631052b33cd72966d199c681af0a1a38d93061201af80da960ed8c552b5 |
| seal-directory-batch-v2.py | 1a6a1c98d400fc6a38c8592366eacb6207bba7f92f9e8711af4ad12185ef7627 |
| baseline-v5/command.json | bc7c380ff2495a45d361877efa307ea4ada205dbc25d309b180a0bdbae20e560 |
| baseline-v5/results/core.trx | b0838aaa08b284b77e216f961ee8fdbabf15e799df1398344527e6406a60eaa0 |
| baseline-v5/results/app.trx | 679ebc7a01e4aaba946b96110dc6ce1d4cea1d9532167704efcb4fe2937de646 |
| working-v7/command.json | 92e094dcbfce81a753e5275853ade548a03b86dc34d447d06601a0666a7a59bc |
| working-v7/results/core.trx | 9a27b5af62758c72b12e612aa1d52ca8b48a540f4584d5c1ee8a515078979f28 |
| working-v7/results/app.trx | a2d055201025221764baa0289863dc3786cf6fcd8e971e98ea41298339efffb6 |
| clean-v8/command.json | 02235ceb5865cb73f45735b4a4c8fb403770b0ad6a16e51998c6407c78a426da |
| clean-v8/results/core.trx | 7093658347a5ce522d0093d0341f1845110cf48efc1fe5bef9f9d41a6329c707 |
| clean-v8/results/app.trx | 856c42637693cfc4df7811f69c2469bfa1655bb48e95a1f7cca52f09c7898fb4 |
| run-directory-batch-v8.py | 85d7fac53178bf832585dfb973c4da3f0afdd89784a5bf8076012cb7b664590b |
| independent-directory-clean-v3.json | 83c924fce90c1c42c376e41dc3c15289d092f8b94da38dc75ead12982ea159b1 |
| seal-directory-clean-v3.py | 16f808bb5a6c01b1529c83e282a8e6bfd59eb0b17c4e7ee6f881e3c7cedcca75 |
| clean-preflight-line-endings-v1.json | 7b10e197f625c685fa72f5bbb4537520b2848524454753a967592ad285511e98 |
| linux-dependency-working-controls-v1/controls.json | 67ca1a220c629c4cf0a57ff3031b3496644874cd617cdd6d9de48d69bc08b108 |
| linux-dependency-clean-controls-v1/controls.json | d34161ec6a28e0f054f7a03abdb2e891b52ae577f46d088fc2eacc5d552d4b5a |
| clean-linux-launcher-command-v1.json | 9b1ba377b8a85a00f229f01ddaaafafca8d12ea63b744a130c46d87b770f83e1 |
| run-clean-linux-launcher-v1.py | 222fa1931d4a685e65d3d1c171624a4216eea0bf61c88872c6675173fecc8673 |
| status-row-transitions-v1.json | ef49b25813c7597e783960e56af1161c7fa20bd77dcd5eba2d2b00d1ced6ee9a |
| write-directory-record-v1.py | 36c11831d95b2fb4a89c9dc86bdf881df45c3daf9b387e41049ae5de95edbe47 |

The manifests retain all 136 discovery/fixture/working/clean files and their exact hashes; individual payloads remain locally retained. No physical USB, persistent Mac/VM setup, signing, contract freeze, candidate, tag or stable publication changed.

## Original CI failure and mirror follow-up

Original 6e9dadf run 37681607535 attempt 1 preserves 20 selected server digests/every member and 12 available raw TRX inventories. All 102 executed new directory cases pass: 26 App cases on Windows x64/ARM64/macOS and six Core cases on all four platforms. Ubuntu Remote/App is not executed. The unchanged broker suite also passes 62 executions/64 wire-CRC observations on the two Windows lanes. Five owned dependency-launcher controls pass in the Linux runner. Every available preceding case name/outcome remains accounted for; no rerun replaces this failure.

The bounded install exits 124 while downloading from `azure.archive.ubuntu.com`, after visible package progress and a retry for `libnfs14`. The follow-up changes only the disposable runner's existing Ubuntu source files: exact Azure Ubuntu mirror URIs become the [official Ubuntu archive](https://documentation.ubuntu.com/project/how-ubuntu-is-made/concepts/package-archive/). Source suites, components, signing-key configuration and unrelated repositories remain intact; target file/parent escapes are rejected. Five actual-CLI owned-file controls pass, including CRLF Deb822, legacy format, unchanged canonical/unrelated sources and a rejected symlink. This proves the transformation, not native mirror/download success. At the 645b947 mirror producer, runtime and test identities remain identical to 6e9dadf (721 Git identities independently verified); that original follow-up result is retained below. The subsequent correction and native follow-up scope are tracked in [I203](E-I203-directory-content-evidence.md).

Private `FileCatReleaseEvidence/dc202-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| mirror-working-receipt-v1.json | 949f3efb5658818ef8376c0c95178150371885b16a3cb0b3acad1a7419d2ac17 |
| mirror-working-controls-v1/controls.json | 50983209082cb2da7ee596a0902038e176f00d9b16347fe6429336b0178bbb4b |
| verify-failed-directory-ci-v1.py | a73fdc7d0af772fe6ddf45bafab41a06ef729b76a96801c8674cbf2d1228f7e0 |
| ci-failure-capture-v1/annotations-stdout | 60a4b01f9fc6087ef12537311b6b8b3a7cc452bb021514c7dc7ce26c163b44ca |
| ci-failure-capture-v1/ubuntu-log-stdout | f54d5328a6d9a2ec0ec985ec387839e00c70806f380b191a4da560d660708525 |
| independent-mirror-working-v1.json | bd42f14b22c6609fa479be1b8fbc4f4887f7ac0df34ae84e221cc26d63c28228 |
| seal-mirror-working-v1.py | f5fa760ea0280413ba7b27175dce67650bb483043c2b5d547ac66e0971bfc337 |

Private `FileCatReleaseEvidence/ci-37681607535-failed-assets-attempt1-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| independent-failed-directory-ci-v1.json | 1d47ada6dde9ffc5833331c7e870cd2a15aeb3c81a8ff108a090efa36f0075bf |
| independent-failed-directory-ci-audit-v1.json | 30f19d896857ee28a76755db5c2476d64a2a50fa1a2953c0518362bed6c9ea12 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Selected receipt or reader | SHA-256 |
|---|---|
| collect-i202-failed-ci-v1.py | f8cebddd587b55ff6d835ddd8c52b0ac91788518bee248f0cb6b0b368c93e646 |

## Committed mirror follow-up and original failure disposition

Exact 645b947 committed export verifies 1138 blobs, all 721 runtime/test identities unchanged from 6e9dadf, and five owned mirror controls/no skips. Original CI 37686386645 attempt 1 passes the two Windows jobs, fails Ubuntu before the native rewrite (exit 2 because sudo removes `GITHUB_ACTIONS`) and fails one macOS held-read assertion (three calls observed, two expected). Twenty selected server digests/every member and twelve raw inventories are independently verified. All 62 broker executions/64 wire-CRC observations pass; 101 directory executions pass and one assertion fails, with every other available preceding outcome retained. The historical cause of the macOS count is unproven; no device-health observation exists for that attempt. Ubuntu Remote/App does not execute and the native source-rewrite receipt is absent.

Committed 86e8c6d519c411fa509a2f1d95bf5b064a813cf0 explicitly passes the CI marker and qualifies the fixture with recorded health-dependent caps/fewer unheld short reads. It passes the combined local checks recorded in [I203](E-I203-directory-content-evidence.md); native follow-up and candidate qualification remain open. The original failed attempt is not replaced or rerun.

Private `FileCatReleaseEvidence/dc202-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| run-mirror-clean-v1.py | ec29ea0822ade21fda05ee26b63f3dbba5843b68345d25ac7c97a8b4ea48ffd7 |
| independent-mirror-clean-v1.json | f2f5deba7667c98726e433cbbebf73dd14f8a1db6365e6a5621d57e769665592 |
| mirror-clean-v1/command.json | 8ad766733c5ba2dfa324948188f5dd696161e092c7b18848f7c03f333deb0a3a |
| mirror-clean-v1/controls/controls.json | 393a08ab680861b8900214c9ef28bdf27e21512620bd3b73c2bf9ce429f573e7 |
| mirror-ci-failure-v1/ubuntu-log-stdout | 06a18f367383f22f08db0551d8d4349ece98934c63a3f65494037fdf47f98c9a |
| mirror-mac-failure-v1/mac-log-stdout | 78c2d533fede661157dde6e7eaf87c9611869d2d8bb33a152a9ef3f88622928c |
| capture-mirror-ci-failure-v1.py | 09dc414fd7f6b70d214e9cc6181eaa80bd23009c2f5374ccd0911c65d28d1aa9 |
| capture-mirror-mac-failure-v1.py | e19a432bca5c9202d39c913d3a6dba23d8b54d5da9e17e61aed3f30b51c392bc |

Private `FileCatReleaseEvidence/ci-37686386645-failed-assets-attempt1-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| independent-failed-mirror-ci-v1.json | 300f289ffb69df9e2e02d8ff89321fa9b64867afbbb57f188ae36d5273f30e96 |
| independent-failed-mirror-ci-audit-v1.json | c69c5cba8b521bdfe482933e028ec7e46a14147585078d4c6759d8d7bf590b5b |

Private `FileCatReleaseEvidence/ce203-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| verify-mirror-ci-v1.py | 94bd5fff405304b25317c6dd7fd3f44cbb81b929905a88c52b404e2ecc3df0b9 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Selected receipt or reader | SHA-256 |
|---|---|
| collect-i202-mirror-failed-ci-v2.py | d15cb30c28a7fcf733a9d9375a8c2f90a46f73b07c74d04223fe5759947e31c0 |
