# I203 — trustworthy directory content evidence

**Preliminary remediation sealed on 86e8c6d519c411fa509a2f1d95bf5b064a813cf0.** The committed batch qualifies 117 new controls, passes 209 comparison/synchronization cases and four scheduler controls, plus five owned mirror controls. One existing opt-in comparison benchmark is skipped. Parent I06, campaigns V09/V12/V13/V16 and exact native/candidate qualification remain open.

Original source: 645b947aef3edbfc8cbea5ca2a33081d5d5efe97. Baselines export all 1138 canonical Git blobs and add only the disclosed fixtures. They reproduce 102 failures and 15 positive passes across 117 cases: 96 Core cases use direct content, compare-and-mark, recursive and shared-worker routes; 21 App cases use actual MainViewModel/options/refresh routes in headless Avalonia. The controls use owned file bytes/hashes, real same-length file mutation and synthetic revision/partial-content/provider states. They do not qualify a native desktop interaction or a recovered physical device.

| Reproduced defect | Correction and evidence |
|---|---|
| Changed, lost or unreadable revision evidence is ignored; mixed reads can count as equal. | Length/revision evidence is checked before and after a decisive comparison. A changed record, gained/lost revision or unavailable final probe produces Unknown. Real owned-file mutation is retained through before/after hashes; left/right cases cover every Core route and actual mark publication. |
| Recovered bytes substituted with zeros or a guessed-start caveat count as real equal content. | Existing or newly discovered missing ranges/caveats produce Unknown. Genuine stored zeros remain comparable; stable short-read controls pass. |
| Matching premature endings count as equal, and malformed negative/oversized read counts count as a difference. | Known lengths must be fully honored, with no early EOF or extra bytes. Invalid counts produce Unknown. Tests include both early endings with no revision support. |
| Canceling a revision probe has no effect because the probe is never performed. | Cancellation is checked around metadata and partial-content getters, before reads and before results. Shared workers retain an active source until its callback returns. A tab refresh during a held initial/final left/right revision probe publishes no marks or label and starts no subsequent read. |
| Uncertain recursive results can enter synchronization proposals as a content difference. | Unknown contents retain an explicit reason and cannot be included in the Mirror proposal. The tests run the actual SyncPlanner on these results. |

Working qualification passes 140 Core/69 App cases; exact committed qualification repeats those 209 and adds four existing scheduler replacement/cap controls. All preceding comparison cases retain their names/outcomes; the only skip is `CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable`, requiring `FILECAT_COMPARE_BENCH=1`. Five actual mirror-CLI owned-file controls also pass from committed bytes. The independent reader verifies 1140 canonical clean Git blobs/modes/archive, 974 actual payload references, 70 retained files, explicit Git/private line-ending normalization and 468 baseline/intermediate/final/clean observations. Earlier successful working stages remain preserved.

The batch also corrects two failures in predecessor CI 37686386645 attempt 1. Ubuntu's five owned mirror controls pass, but sudo strips the CI marker and the native helper refuses before changing APT sources. The workflow now passes only that marker explicitly. A macOS admission fixture sees three held calls against an exact-two assertion; its historical cause is unproven because health was not recorded. The fixture now records health, requires two workers while responsive and the scheduler's hard cap while not responding, retains active ownership/publication checks, and reduces unheld short-read round trips. Actual scheduler watchdog/replacement/cap controls pass separately. The original follow-up result is sealed below; no rerun replaces either failure. Full original-attempt details are in [I202](E-I202-directory-comparison-lifetimes.md).

Revision records are weak evidence (length/time/available identity), not an atomic filesystem snapshot. Sources with no revision support can still compare complete bytes; equal unavailable revisions do not prove absence of concurrent edits. Broader alias/provider/identity races, native interactions and final-candidate repeat remain open. No physical source, persistent borrowed-machine setting, signing, freeze, candidate, tag or stable publication changes.

## Original follow-up CI — 2600e3c

Original run 37689672199 attempt 1 at 2600e3cbbd75ced94820570fb67966ed21335551 passes Windows x64, Windows ARM64 and macOS. All **447 executed I203 additions** pass: 96 Core cases on four lanes and 21 App cases on three lanes. All 102 executed I202 directory additions pass, including the corrected macOS admission fixture. Both Windows broker subsets pass (62 cases/64 independently decoded wire and CRC observations). Ubuntu's dependency installation exits 124 at its existing 300-second bound; its Remote/App tests do not execute. This remains partial qualification.

The independent reader verifies 20 server digests and every selected archive member, 12 raw TRX inventories with definitions/outcomes, the 447 raw content observations, 78 lifetime observations, four builder receipts and 92 actual locked restore graphs. Every earlier available case name/outcome is retained. Native mirror configuration executes but replaces zero URIs: the runner actually uses `/etc/apt/apt-mirrors.txt`, outside the helper's then-current two targets. APT tries Azure first, stalls/retries, then reaches the official archive too late for installation to complete. [I204](E-I204-archive-transfer-warnings.md) records the mirror-list correction and its nine committed owned controls; native installation follow-up is still required. No package, candidate or release qualification is inferred.

Private `FileCatReleaseEvidence/ci-37689672199-failed-assets-attempt1-v1`:

| Selected receipt | SHA-256 |
|---|---|
| independent-failed-content-ci-v1.json | c90cebf4590aa40657a8d607f2884043bd08e78ec1347ab85177b905db52ae63 |
| independent-failed-content-ci-audit-v1.json | 01356226a6d6b925b32250d2c758cca4808254c53863d68419c33af2c246d50a |

Private `FileCatReleaseEvidence/aq204-v1`:

| Independent reader | SHA-256 |
|---|---|
| seal-content-ci-v2.py | 40683f04f7e7dbc571ce843036bf27e4cce182627af79e2114ead11e0ddf91dd |

Private `FileCatReleaseEvidence/ce203-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| fixture-stage-v1.json | f468164d2659e274d5cabef85ffad7cddeff1c9d743dc8d5788ba032ea26272d |
| DirectoryContentEvidenceTests-v2.cs | ac717e0e4590c96938375efc1c6aaf3dd2940f0cea7ca5737f449fdb54eca235 |
| DirectoryContentPublicationTests-v2.cs | 2750a25d974d237f5f2f4ad3346e46ba74cbcbb8cfef727f1ed48a10c09504ba |
| DirectoryComparisonLifetimeTests-v4.cs | 1f7c2e96e22d817521766183e51cb4177801cd5d18e2291d4a1d7e43d069fc71 |
| baseline-v1/command.json | b6bc31c49869d09b6edbbe80fa545dbf891bf6b434b1f52631c9a38f819fd6ae |
| baseline-v1/results/core.trx | 16327b0849aeb1dc7ac907752b62f1f6e1710baeb7f10fa3e5bc322176ca7872 |
| baseline-v2/command.json | 56cf839d32916df3009b535ad29d4e6d4bc6ea72f548d05a0734cfadf67e2ee3 |
| baseline-v2/results/app.trx | da4db6ae11a8c3843381a5dd390e5db3fd880370920a386b2b863a52147747c7 |
| working-v4/command.json | 9236e41d7bf93aadb34d14074f8ebc9f55d5960fe23e1f28270ba4c54741828c |
| working-v4/results/core.trx | d7a964a8fce0a61ccd09d46996520c379bc705bf55f2db4d7b41488b59f75a3c |
| working-v4/results/app.trx | 70e03036075da6f17ad4c31975411028c381304ee9da2339d88a50ba604d2b41 |
| clean-v4/command.json | 5eef405f95a62738bcb4653a07c8e56d586baf6871698474df1854bcee1c157f |
| clean-v4/results/core.trx | 8994faf85ef0e80c5cc70c3de2975cf6911ccb42da436aa5fc20f5c6db0cfcd7 |
| clean-v4/results/app.trx | 3a899ef7753882f9b159fe96b7f3de50561d53bbe024e156d6586933d01a9f3f |
| independent-content-working-v1.json | c9dc8ca9736996321a6698499c96e0d840215e4fb3be764112e93565391d5089 |
| seal-content-working-v1.py | db89a25c0b815e93a0beefee43a491c03af60345c18eb9dd4d60d1a90d1e9342 |
| independent-content-clean-v1.json | 085e528ab955080f9beb660bf46828aa387e9162797d7f7e3a104cd47b294ca7 |
| seal-content-clean-v1.py | 063801b0cfc5273414179353666b6ad9398a01d03a0e4e176136bf0f8c847fca |
| run-content-batch-v1.py | 1b2a559f73fcf408a5f1b329a0e74f848ec112a812c65e924998610dba38440c |
| run-content-batch-v2.py | b6103de711b9a9bb9b12cdbabd087a223cb6928cde2724ff336b848b49035d86 |
| run-content-batch-v3.py | f0a744e5371cf6a88b57cfdb5c5173e58636c4b30addff90e6e30ff4343e00d9 |
| run-content-batch-v4.py | 7e611c1db8cc0847c13a0dc0fea8da6b33da012ec2f8dc456003b60960b1b71d |
| run-clean-extra-v1.py | f5e12fff65224bdc89a3ed12d0612fb94183d4b6b5dc6e2652e8eeac34f91626 |
| clean-extra-v1/command.json | 8cab07d941aff5dec51ec9fefc6eb8080605a344c35184996124989e397ce04e |
| clean-extra-v1/results/scheduler.trx | 3fb03f242d187510e8b3d265071ff1f358dff7e282c4c660ed8f76bfa3fdb8c1 |
| clean-extra-v1/mirror-controls/controls.json | ea169dcefc8b3814c715deadd9b459079cca50f03f87880b1a0fa743953ef3fc |
| document-status-transitions-v1.json | 83e8435bb7e1bbbd5341558b3e4fb80180542dfb39c06609b448a5a06bb02eef |
| write-content-record-v1.py | 9d3cdd4f5705e7570b3321ddd5ba3d81cc47e685f25e8fa9b595a23dfaaa6add |
