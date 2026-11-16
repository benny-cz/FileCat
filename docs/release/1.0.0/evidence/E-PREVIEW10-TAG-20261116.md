# Preview.10 — exact source, tagged CI and release inventory

The unsigned `v1.0.0-preview.10` annotated tag object
`210ebace7741e7680a7bc421ee7cac74d163c966` peels to source
`9cc76f2f42fcc1772d2fdb21a4f7d1ce53e1b470`. Both were read back independently
from GitHub after the tag push. Active tag rulesets 26082787 and 26082784
restrict creation to administrators and forbid updating or deleting the tag,
with no bypass actors for update or deletion.

The [preview.10 notes](../PREVIEW10_RELEASE_NOTES.md) cover archive writing,
password-backed browsing, native edit storage, file-list export, Windows Shell
menus and icons, NTFS name colors, column layout persistence and menu keys.
The [archive route decision](../../../validation/archive-writing/RELEASE_ROUTE_DECISION.md)
and [implementation status](../../../validation/archive-writing/IMPLEMENTATION_STATUS.md)
retain the route scope, owner decisions, historical failures, evidence limits
and unfinished signed/installed candidate checks. This preview does not close
those stable release gates.

## Source and test evidence

The preceding implementation `1131471595025d13767ae4078d1ad0edc82ccc7d`
passed all five required producer/platform jobs in
[run 43839503202](https://github.com/benny-cz/FileCat/actions/runs/43839503202).
Only three documentation files changed between that commit and the tagged
source; source, tests, dependencies and packaging are identical. Its 20
downloaded primary TRX files contain **60,090 passed, 5,743 skipped and zero
failed**. Against preview.9's 20 receipts, 16 passing rows were replaced:

- Eight oversized-session rows now require that an unsupported session stays
  visible for read-only recovery rather than being silently ignored.
- Four column-profile rows now require exact round-trip preservation, including
  fill proportions.
- Four verification-result rows became eight rows, exercising both an `.exe`
  and an ordinary data filename without title spoofing.

All replacement rows pass. No other prior row was removed or changed, and no
previous pass became a skip. The added rows comprise 18,595 passes and 4,169
skips; most skips require explicitly provisioned native containment, storage,
transaction, resource, package or platform fixtures. The new skip reasons
include 894 Windows containment rows, 656 native Unix archive-storage rows,
542 native-edit ordinary-user rows and 160 NTFS transaction rows. General CI
does not run those opt-in qualification lanes; their dated native evidence
remains separately scoped in the archive records.

The same-source [main run 43856410284](https://github.com/benny-cz/FileCat/actions/runs/43856410284)
passed all five required jobs. Its 20 primary TRX contain **60,091 passed,
5,742 skipped and zero failed**. They match the preceding green implementation
by path, name, outcome, skip message and multiplicity except for one Ubuntu
filesystem probe that now passes instead of skipping: `UnixDeviceTests.A_folder_reached_from_memory_through_a_link_is_on_the_disk_the_link_leads_to`.
The earlier runner could not name the test folder's backing disk. No prior
passing result was lost. The retained 20-file SHA-256 inventory hashes to
`2140106e708e4f660cd43d4ec1f6c650d62b5cda91a0e2af11cb7e49cebbc6e0`.

| Source test artifact | GitHub ID | GitHub-reported SHA-256 |
|---|---:|---|
| test-results-windows | 14223946930 | d45033f333150c57b3b025bb97e7977802c9ebb311a4c21da32450a951c737d9 |
| test-results-windows-arm64 | 14224207859 | 6493ea6243fd7c8f27fba9a1d97527d76e72c6c6a0acdaf8ab3f3b0dcdbc2c8e |
| app-test-results-ubuntu-24.04 | 14222993489 | 590d21e0ba3119508a2b1b5f376927074fda674bb877630e308acb5148af7ebe |
| app-test-results-macos-26 | 14225820044 | 5e11ed02d970b3f4e6dd137310db6661aa99623913dddc205f9ca064153b8e5d |

The [tagged run 43860532506](https://github.com/benny-cz/FileCat/actions/runs/43860532506)
is in progress. Tagged receipts, all three packages, producer manifests and
hosted draft reconciliation remain required before publication.

## Tagged CI first attempt

The first tagged Windows ARM64 job failed
`ZipListingRevisionTests.A_listing_reads_its_revision_in_the_background_and_a_later_change_cannot_move_it`: the known revision was still null after its
120-second wait. This provider test retains production low-priority background
I/O; the related lower-level revision tests explicitly disable priority lowering
for their scheduling-independent assertions. The same provider test passed in
the preceding green ARM64 run in 64.59 seconds. This supports a scheduling
explanation but does not turn the failed attempt into a pass. The failed
job log and TRX were retained (log SHA-256
`228ca03443e9814d7b0b7079ffd063151d6f07eac69a6a8a661445440820abc3`).
The exact-source main ARM64 job passed that same test in 15.73 seconds, and
its complete primary receipts match the preceding green ARM64 run.
An unchanged-job retry will be requested after
the other jobs finish. The immutable source/tag will not be moved.

## Tagged CI retry: still blocked

Attempt 2 repeated the same Windows ARM64 Core failure after 121.44 seconds:
`ZipListingRevisionTests.A_listing_reads_its_revision_in_the_background_and_a_later_change_cannot_move_it`
still had no known revision. The second job is `133307077793`; its retained
log SHA-256 is `170f54ca1595b5f062a4e467cac56b700c7ea94b497ada2169b61aecc6fc0147`.
The other four required jobs passed. Tagged Windows x64, Ubuntu and macOS
primary receipts match the exact-source main run by path, name, outcome,
message and multiplicity. All package and private-draft jobs were skipped.
**No preview.10 package or private/public release was produced.**

No further unchanged retry is planned. The correction isolates the provider
fixture from the Core suites sixteen-way parallel I/O while retaining real
production background priority, the 120-second guard and all existing
assertions. A locked restore and clean isolated build passed all **22 provider
and revision cases**, with zero failures or skips. No production code changed.
The retained focused TRX SHA-256 is `31e462310a0a694088f5c2711819c53f83fc843d27be74c6c35b6259fdbe3283`.
Full hosted CI of the correction remains required. The protected immutable preview.10 tag
cannot be moved to that corrected source; a new tag needs the owner's version
decision. Stable 1.0.0 remains NO-GO.

## Exact hosted inventory

Not yet produced. No preview.10 release has been published. The final inventory
will list every asset's exact name, byte size and SHA-256 after all three
package jobs pass and the producer files are independently downloaded.

## Publication decisions

DEC-09 requires the owner's protected `release` environment review before the
workflow uploads a private draft. DEC-07 requires a separate owner decision
on the exact unsigned tag, hosted asset inventory and hashes before public
publication. The intended public flags are `prerelease: true` and
`make_latest: false`. Stable 1.0.0 remains **NO-GO**.
