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
and [tagged run 43860532506](https://github.com/benny-cz/FileCat/actions/runs/43860532506)
are in progress. Exact-source/tagged receipts, all three packages, producer
manifests and hosted draft reconciliation remain required before publication.

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
