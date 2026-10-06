# E-I162 — relative Git worktree admission and correction

2026-10-06. Discovered against exact source
`850298359fc16cf28503cea42f3ba9c0b1599240`, whose Git reader source was unchanged
at discovery HEAD `5280ad6ce3a1741a6fab5cc78f56a783db953c71`.
High path-admission risk; must fix under I16/V23 B10/V24.
Status: Remediated preliminarily at
`6215329ed67da6c8463a996eb51b12ac569194aa`; committed production, three native
systems and all four original CI lanes now verify. Final-candidate and broader
I16/B10 qualification remain open.

A private BCL-only probe loads the actual production FileCat component by
reflection and calls its production `SafeRepository` and `ReadAsync`, with the
installed Git executable explicitly selected and pinned. Its five actual local
Git cases establish ordinary badges, an ordinary absolute worktree redirect,
refusal of an absolute junction path, acceptance of the same junction through
`core.worktree=../target-link`, and restored ordinary badges. The relative case
admits the repository and returns Untracked for a file only in the owned target.
The absolute spelling of that same junction returns no snapshot/admission.

Expected: inspect the resolved relative worktree path for the same local/junction
policy before Git runs. Actual: `NamesThisComputer` accepts relative values
without resolving them. The metadata-tree check does not cover the separately
configured worktree. This is native component evidence of inconsistent admission
and redirected reads, not measured network contact, credential transmission,
execution of repository-defined programs or installed-candidate qualification.

The fixture uses only owned local folders and one junction. Its exact junction
is removed without recursion; target file bytes and every production payload
pin remain unchanged. The original configuration is restored. No physical source,
VM/Mac access, user/system setting or external service is used. The probe does not
execute FileCat's GUI or administrator helper.

Private `FileCatReleaseEvidence/git-relative-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-relative-worktree-v1.json | 4f2c62b7a8461a2ee9ce40fb0251caf978b8c540e2dc7425e2f839b70386fb2a |

## Narrow correction and controlled validation

Git documents `core.worktree` as relative to the actual Git directory. Shared
configuration in a linked worktree retains that directory as its base, rather
than the parent of the shared config file. The correction passes the actual
directory through both configuration checks, resolves relative worktree values
and applies the existing local-path/ancestor-junction policy. Other relative
settings, home-relative values, alternates, parser completeness, path races and
Unix mounted paths retain their broader qualification limits. See the primary
[Git configuration documentation](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coreworktree).

Durable tests were added before the correction. The first five cases produce
three expected refusal failures and two positive passes against the original
reader. A sixth linked-worktree context case raises that to four failures/two
passes. The final exact test assembly and all 721 input files were preserved.
The controlled correction replaces only `FileCat.dll`: all six same named
executions now pass, the other 720 files and test assembly are unchanged, and the
original payload remains unchanged. The full same App test assembly records 392
Passed/23 NotExecuted/415 total with no failures and distinct execution IDs. Skip
messages and complete inventories are retained. App compilation reports zero
warnings/errors. These executions use the explicit working source overlay on
5280ad6; they do not claim a clean committed producer identity.

The exact original native probe DLL is then reused with a copy of the original
production x64 payload. Only the newly published `FileCat.dll` changes, using the
original `0.0.0-i03resourcecheck` version/runtime settings. Both absolute and
relative junction spellings now return no repository or badge snapshot. All
three ordinary/restored positives retain their expected badges. Original
production/probe pins and owned target bytes remain unchanged; configuration is
restored and the exact junction is removed without recursion.

The first native repeat fails at owned Git fixture setup (`git add`, filename too
long), before a junction or FileCat component call. Its complete original logs
and exit remain retained. The corrected repeat uses a shorter owned fixture path
and the same unchanged probe/payload DLL; no compiler or test fixture is rebuilt
to obtain the pass. The frozen probe's embedded source field still names its
8502983 baseline; the independent record explicitly assigns the corrected DLL
to the working overlay and does not reinterpret that field as new provenance.

Private `FileCatReleaseEvidence/git-relative-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| controlled-before-v1/independent-durable-baseline-v1.json | 55e36a3b55c4264c842b5906d492a3ba734b0ecefb11602b4b9fba5a619d3695 |
| controlled-after-v1/independent-controlled-fix-v1.json | d40d80517fe4f6c7a0390910c0569d110ab2c2e26be3d0d7cb9ab3cd16aa5c78 |
| controlled-native-v2/independent-native-overlay-v2.json | b75f13b945ef85f73afdb9c539134f8a5fb1cda0c3a8728a8e935ebef9f42713 |

No network contact is measured by the working comparison. It opens no physical
USB, GUI, privileged helper, guest or Mac, and changes no user setting. The
original failure is retained, rather than replaced by the pass.

## Committed production, native repeats and original CI

The exact pushed fix commit supplies 1,039 verified raw Git files (mode, blob
SHA-1, length and SHA-256) and a retained source archive. The authoritative
Windows packager builds both x64 and ARM64 SC/FDD modes at
`0.0.0-i162check`. Four ZIPs preserve all fifty App notice files each, both
existing inventories match 38 locked package identities, and four final native
receipts/resource objects/source/payload pins verify. This is a raw export:
empty embedded revision fields are retained, with the external Git-source proof
kept distinct from final-candidate provenance. No installer or ARM64 execution
on the Windows host is claimed.

The unchanged original native probe repeats against the actual clean x64
production payload, with no working DLL overlay. All five cases pass: two
junction refusals and three ordinary/restored positives. All 303 payload inputs
remain unchanged, the target bytes remain intact, configuration restores and
the exact junction is removed. The frozen probe's embedded source field is
still its original baseline, not the identity of the new production DLL.

Separately published self-contained test payloads from the same clean raw source
run the 37 Git/environment/reparse/metadata/icon/browse cases inside the disposable
Windows and Ubuntu guests and ordinary-user Mac session. Windows records 34
Passed/3 skipped, including all six new cases passed. Ubuntu and macOS each record
17 Passed/20 skipped; both new ordinary-path cases pass and four Windows-only
junction cases explicitly skip. Every complete XML/TRX inventory, distinct
execution ID, 352 Windows/348 Unix payload byte pins, original retained output
and process/temporary cleanup check independently verifies. Mac's reused runner
retains an older I150 baseline field as history; the independent seal explicitly
does not treat it as this I162 comparison. The actual comparison baseline remains
8502983. Bounded Mac `caffeinate -i` ends with the runner; no persistent sleep or
guest setting changes are made. Both VMs remain running as the owner requested.

Original push run
[37505705190](https://github.com/benny-cz/FileCat/actions/runs/37505705190),
attempt 1, passes policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26, plus ARM64
startup/drawing/installer checks. Nineteen server artifact digests, four clean
SDK receipts, fourteen full execution inventories, 92 actual locked graphs and
all reference/asset/draft/package-set controls independently verify. All 415 App
cases are retained on each of four lanes. The six new cases have 24 distinct
execution IDs: both Windows architectures pass six, both Unix lanes pass two
and explicitly skip four. Both downloaded native evidence sets preserve and
verify RC/resource/source/icon/output pins. Three package jobs and draft
publication skip; no actual new Unix package or tag/draft/stable publication is
claimed by this push run.

Private `FileCatReleaseEvidence/git-worktree-production-windows-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-committed-windows-production-v1.json | 361a880942ebaf8ece0a6b158f181f421981694d4947892194c66c596a769f5e |

Private `FileCatReleaseEvidence/git-relative-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| committed-native-v1/independent-committed-native-v1.json | e2d951e44600840d4e640f215fc3b9ebccb11826eb0a45e1b0ca5567c73a04da |

Private `FileCatReleaseEvidence/git-i162-native-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| producer.json | 5f90414be151e3b9b9ec732031c39ab11b4cd20358e2da5612182490aab8124c |
| windows-executed/independent-guest-v1.json | a9761e195ee02724b3b3962aca3bf2615ef8fc0cdf33bfb6a89f3c66e556bc74 |
| linux-executed/independent-guest-v1.json | 911890d7cf48171473ba0dcc62a2a453d7bb21bca21b0d66e1d462e0a66630d3 |
| mac-executed/independent-native-v1.json | 06727f698938e4db39e0ae641ce626143f64900c22931c9cdd90c6ec03eeb8d6 |
| independent-committed-native-seal-v1.json | ad266d76defad23aed5cf52679ef190766c070d3b58c3601ea76bedc86aec834 |

Private `FileCatReleaseEvidence/ci-37505705190-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 461181013723812ef81f5d7308ffe804505a6b0e5395d6fa9e34638bbcc2bf86 |
| independent-fixture-ci-v1.json | decc7b533c942204774041ae508d88593cc7d568474042156906a762d8434bb5 |
| independent-producer-policy-ci-v1.json | 777885465e55c39f8216bd4f73e4cf30d24a603728a097c2c8ec77b13ff5a905 |
| independent-draft-guard-ci-v1.json | 36e7981ac941acf28abebcbe2afce681b2001c34f0ed6d505a28910b8407a609 |
| independent-separation-ci-v1.json | bb987bce780ee895b8ec944910d265180361876145869448eecad2c6c7a7e71f |
| independent-restore-ci-v1.json | 284a2588052b5ddeb693d88a22039e9d0a59ae063bb761040cbeee1c93fa1f71 |
| independent-native-resources-v2.json | fd118d44cbc377d3dc14fb03c6583cafbbcec57cdc10f9374271cdf6f4a79946 |
| independent-i162-ci-cases-v1.json | ba7c99a438b34be821d3bac8f82eef93ea0b565e9802f4ea7993e51552538d72 |

Preliminary finite remediation is sealed. Full parser/home-relative/alternate
and path-race boundaries, Unix mounted paths, measured network contact, native
GUI, physical ARM64 and final-candidate qualification remain separate. No physical
USB test, external user message, release candidate or human stable GO occurs.
