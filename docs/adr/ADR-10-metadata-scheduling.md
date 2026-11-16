# ADR-10: Metadata scheduling and complete ordering

**Status:** Decided for v1 (2026-09-27), extended for column ownership (2026-11-16).

## Decision

- **Fields.** MetadataField declares Immediate, Cheap, Expensive or Analysis cost, applicability, a producer,
  formatting, and optional key/detail/explanation callbacks. The latter callbacks perform no provider or native I/O.
- **Demand.** Background admission serves visible rows and horizontally hidden profile fields of a vertically visible
  focused row. Obsolete queued viewport/store work drops. The existing per-device backlog is 256 admitted requests;
  execution and hung-worker replacement use DeviceIoScheduler's bounded workers. The former statement of four total
  outstanding requests was inaccurate. Path/revision/field cache capacity remains 50,000.
- **Eligibility and outcomes.** Get, Compute and gated Peek share name/kind/Offline gates. NotRequested, Pending,
  Absent, Denied, Unsupported and Failed are typed states. Gone is an observation on Absent; transient refresh failure
  can retain Available/LastKnown with its reason. Definitive Denied/Unsupported replaces an old value. LastKnown is
  visibly marked, and never counted as a current successful read. Unknown keys stay last in both directions within
  the existing folder/file partition, after the parent row.
- **Freshness.** Refreshable ownership/permissions use five seconds, thirty seconds on recognized slow Windows
  locations. Activation expires recorded times and explicitly requests repaint; hidden rows stay idle. Denied and
  Unsupported do not retry on timers/activation. Analyze retries them. Reread invalidates metadata and names;
  Attributes invalidates metadata. A narrow pre-pipeline store transition retires Owner/Group values and running
  production for the affected folder before replacement ordering/demand, including same-size/mtime replacements.
- **Immutable ordering.** Every metadata sort captures bounded keys outside comparisons, including external runs
  and merges. One captured resolver map and culture supply principal keys: resolved names, case-insensitive, then
  canonical numeric IDs; equal names tie by ID. No comparison reads a live metadata/name cache or provider. Filtering
  and streaming appends retain the snapshot; appended rows have fixed unknown keys until an explicit new sort.
  A direction change captures again. Ordinary metadata/name arrivals do not reorder rows.
- **Retention and bounds.** Analyze retains an admitted prefix of eligible store indices independently of ordinary
  cache eviction. Growable packed chunks, bounded primitive-key reuse (256), principal deduplication, name-map copies,
  values and strings share IndexMemoryBudget's process-default 512 MiB with existing listing indexes. Reservations
  precede retention and last until the final actual worker/sort lease releases. Additional ceilings are 1M rows and
  16M UTF-16 characters, charged per retained reference; observations use a conservative quarter share so key/name
  copies have room within the total. Unknown object graphs are not retained. Actual coverage and limiting resources
  are reported; unretained rows never fall back to a live cache.
- **Analyze folder.** This explicit cancellable pass reads listed files and applicable directories, never descendants.
  It distinguishes current, LastKnown, ordinary absent, Gone, Denied, detected placeholders, other unsupported and
  failed outcomes. Principal fields wait at most ten seconds for global name idle; Esc skips only that wait.
  Synchronous scoped publication claims exempt the pass's own origin and retire conflicting retention. Operation,
  store, row count and claim checks guard attachment and the accepted sort's completion banner. Navigation,
  replacement, field change, Reread and close retire it; running calls retain their store/buffers until return.
- **Identity and platforms.** Principals are immutable SID/UID/GID identities; a per-service resolver performs naming
  on dedicated threads, with four process-wide native slots including retired calls, queue 256, cache 4096 and
  262144 retained UTF-16 characters. Demand-based retry is 10 min resolved, 2 min missing, 30 s unavailable. Native
  successful-null lookup differs from failure. Names alone repaint and never enter focused row speech.
  Windows x64 reads owner-only security information from a shared READ_CONTROL handle opened on the final link;
  it requests no content, group/DACL/label, elevation or write. OS path traversal, authentication, auditing and name
  service traffic can occur. Linux x64 uses final-no-follow/no-automount statx and independently valid UID/GID masks.
  Windows ARM64, Linux ARM64 and the new guarded macOS ownership route stay Unsupported pending native gates.
  Unix Permissions retains its existing behavior and describes mode bits, not effective ACL rights.
- **Columns and compatibility.** Settings → Columns keeps existing profile/schema/default rules. Windows Owner is
  optional at 160 units; no built-in Windows profile gains it. Version, dimensions, link target, origin, hidden data,
  verification and platform ownership/permissions use their existing managed or bounded native producers; there is
  no blanket claim that every metadata parser is managed. Tooltips/Inspect add sanitized identity/state detail while
  preserving dialog structure and the existing focused spoken row summary. Windows Owner Analyze is filesystem-only.
  New Windows parses an imported meta:owner key. Older Windows omits it from the parsed layout; editing/resizing
  profiles there can reserialize without it, whereas an ordinary SaveSettings need not delete the raw key.

## Consequences

Browsing never starts folder-wide analysis unasked. Complete read counts describe captured observations, not a frozen
filesystem or guaranteed names. Storage provenance and native platform/interoperability limits remain explicit.
See the [implementation plan](../design/FILECAT_COLUMN_OWNERSHIP_IMPLEMENTATION_PLAN.md) and
[dated evidence](../validation/column-ownership/O5-20261116.md).
The [unreleased note](../release/1.0.0/COLUMN_OWNERSHIP_RELEASE_NOTE_DRAFT.md) records user-facing behavior,
settings compatibility and the validated runtime references without changing an existing published release.
