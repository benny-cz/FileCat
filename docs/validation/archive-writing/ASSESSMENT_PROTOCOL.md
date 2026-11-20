# Archive assessment protocol checkpoint

Status: implemented for strict TAR assessment, behind disabled write-profile registration. Rebuild/finalization/fresh
verification and publication are not implemented by this endpoint. This is not completion of AW1 or format qualification.

## Ownership and conversation

`ArchiveAssessmentClient` owns the global in-process queue reservation until the actual process exits, diagnostics drain
completes and process disposal/reaping finishes. Its launch factory must explicitly supply a confined native process;
there is no ordinary-process or in-process-parser fallback. Source-opening callbacks must return views of retained,
reviewed regular-file objects/private snapshots. This API is not itself a reviewed source-lease implementation.

The conversation is strict and single-use:

1. Child sends exact protocol/schema hello.
2. Parent sends one bounded assessment request: profile, actual source length and reviewed SHA-256.
3. Child may issue bounded reads of numeric input ID 0 only. The parent validates every request before source IO.
   Writes, stage ID -1, new IDs, phase changes and unsolicited messages refuse before touching a stage or source.
4. Child sends a catalog envelope followed by exactly its declared number of ordered member records. Input IO is no
   longer accepted after the catalog begins. A controlled refusal can terminate between complete records.
5. Parent reconstructs the complete immutable manifest and recomputes all three digests. The terminal digest must match.
   Trailing bytes, missing records, wrong profile/revision/counts, unknown reasons or a nonzero worker exit refuse.
6. Parent awaits final stderr classification and checks final OS CPU accounting before accepting the assessment.

Only bounded reason codes cross the refusal channel; raw member names or decoder exception text are not refusal messages.
No assessment result authorizes a publication, source path, destination, cleanup or filesystem capability change.

Parent-observed accepted bytes and complete validated catalog records count as progress. Heartbeats/empty reads do not.
The watchdog enforces 30 seconds without progress, a CPU allowance derived from actual input length (30 seconds plus
two seconds/MiB, capped at two hours), and a twelve-hour wall allowance. Native Windows job limits provide the hard
process memory/user-CPU/lifetime boundary. Cancellation currently kills and reaps; cooperative cancellation and a
dedicated cancellation channel remain required work.

## Canonical data and bounds

`ArchiveWriteManifest` freezes profile options, format features and ordered physical members. Each member contains an
exact raw name, type, duplicate occurrence, logical length, content SHA-256 and versioned semantic metadata. Occurrences
must be contiguous per exact (name,type); equality never folds case or Unicode. Zero-length content must have the known
empty SHA-256. Aggregate entry, logical-byte, encoded-metadata and retained-heap budgets apply to the complete manifest.

Canonical digests use `FCAM`, schema 1 and separate full/content/metadata domains. Integers are fixed-width little endian;
variable data has an explicit byte length; member order, name/type/occurrence and profile options are included. The full
digest includes both content and metadata; content/metadata digests distinguish those dimensions. The semantic manifest
digest is separate from the physical source/catalog revision.

The TAR semantic blob has a ushort schema (1), int32 mode/UID/GID, length-prefixed exact UTF-8 user/group names, int64 UTC
mtime ticks, and explicit presence flags plus UTC ticks for atime/ctime. Format features currently identify schema and
USTAR/PAX dialect. A fuller hidden-header/feature inventory and complete preservation corpus remain qualification work.

Wire records repeat their type, complete record length and exact fragment offset. Each complete record is at most 1 MiB;
fragments are at most 64 KiB including the nine-byte record prefix. Cumulative record/control and frame-count limits apply.
The receiver reserves a record before allocating its buffer and checks string byte/character lengths before decoding.
Failed/truncated/replayed/interleaved records poison the reader. No recovery resumes a partial conversation.

Cumulative wire allowance is distinct from the 80 GiB physical-file ceiling: assessment reads the pinned source for SHA-256,
raw strict framing and independent runtime-decoder comparison. Parent IO grants remain derived from actual source length;
the protocol's finite global traffic ceiling also accommodates later bounded stage reads/backpatches.

## Windows application entry

`--archive-worker` dispatches before UI, plugin startup and the instance check. Its fixed bootstrap includes task nonce and
the local LUID of SeChangeNotifyPrivilege, resolved by `StartAssessment` using Win32 in the trusted parent. The parent
independently verifies the actual suspended child token using the privilege name before resuming it. The child repeats
the privilege-list check using that immutable startup fact because LPAC refuses the LSA name service. Archive bytes and
worker receipts never supply it. No additional privilege or capability is granted.

The child verifies exact package identity, zero capabilities, low integrity, mandatory no-write-up, no enabled administrative
groups or other privileges, LPAC positive/negative access controls, and immediate job memory/CPU/process/UI/lifetime flags
before opening its input stream. Wrong startup facts refuse before the parent serves any archive bytes. Removing the
traverse-check privilege prevented runtime startup; the failed privilege-removal experiment was discarded. Legacy worker
token behavior is retained. Native process command lines are explicitly NUL-terminated and archive working directories
are fixed to approved assets.

Linux/macOS application entry refuses until native controls qualify. The portable child tests are explicitly non-shipping
test harnesses and do not establish confinement on those platforms. AppServices/job/UI registration remains disabled.
