# Shared archive workflow review

**7 November 2026.** `ArchiveWorkflowReview` connects complete assessed catalogs and sealed captured inputs to the
TAR, TAR.GZ, TAR.BZ2, TAR.ZST, RAR5 and 7z planners. This is the common parent-side review adapter; it does not
register an executor, enable a profile, grant native publication or finish the production UI/edit/F6 work.

Exact-member edits retain raw spelling, type, duplicate occurrence and catalog revision. Recursive rename/delete
requests also bind the revision and explicitly select the whole folder prefix, including an implicit folder that
has no physical directory row. Expansion uses an ordinal sorted index and checks cancellation, output counts and
generated path length. It never treats `treehouse` as a descendant of `tree`, folds case or normalizes Unicode.
Overlapping edits, stale/missing scopes, incoming duplicate names, type clashes and unrelated capture IDs refuse
before granting IO. Request collections are copied for review.

The review reports replacement-name and physical-occurrence counts with bounded name samples. Replacing an
existing name replaces its first physical file occurrence and deletes the remaining reviewed duplicates, so an old
later copy cannot hide the incoming bytes. Replacement metadata follows each format's existing preservation
contract. The caller must accept replacements and exclusions before constructing the final plan. A folder merge
keeps the existing directory records and all their optional metadata.

`ArchiveCapturedDirectoryMerge` binds an incoming captured directory to an exact unchanged source directory.
The format planners validate its owner, ID, revision, member type and unchanged target. They still require every
captured file/directory to have an explicit use. The merged directory remains in the native rereview and origin
evidence set even though its metadata does not replace the existing archive directory's metadata. It creates no
file read grant or worker-selected path, and needs no new wire record: the worker preserves that source member
normally while the parent separately reviews the captured directory's provenance.

Retained captures, the still-live source manifest and the workflow's own maps share one review heap ceiling. The
later format planner reserves the retained workflow state alongside its source/capture/output costs. Recursive
rename paths and canonical directory names are reserved before construction. Separate regressions exercise a
nearly exhausted capture budget plus a retained source manifest, and an overlong generated descendant name.

The unregistered version-2 7z profile now has an explicit new-input precision policy: timestamps added from disk
are converted to the earlier representable 100 ns FILETIME instant. The review reports how many added members
need conversion and requires that format conversion to be included in its accepted review. Existing archive
FILETIMEs remain exact unsigned 64-bit values, including values outside the managed timestamp domain. The native
source descriptors and rereviews keep every captured nanosecond; rounding the output cannot authorize a changed
source. Unix native creation cases now use real `utimensat` timestamps with finer-than-100-ns precision.

The final combined matrix contains 3,738 cases per platform: Windows 3,479 passed/259 skipped, Mac 3,683/55,
and Ubuntu 3,696/42. All 62 new workflow cases pass on each platform. Windows also passes six native preflights,
all 163 selected native containment/transaction cases and ten application-entry cases as a standard user. The
frozen bundles match all ten changed C# files through actual compiler checksums and matching DLL/PDB identities.
Their final censuses show no owned FileCat/test processes; the Mac wake helper is preserved.

The focused matrix contains 62 new cases across all six containers and the three RAR5/7z password modes. Ten
real-worker cases each create, assess, merge/replace/rename/add and freshly verify through four separately reaped
processes. Their public synthetic source/output archives undergo independent raw metadata and content checks.
The broad batch additionally covers existing archive/ZIP behavior, native creation and Windows native
containment/update/restoration. The [machine-readable checkpoint](shared-workflow-review-checkpoint.json) records exact source packets, DLL/PDB bindings,
counts and the retained failed phases; missing qualification is not treated as a pass. Windows' official-reader
filename selection defaults to case-insensitive matching. Its corrected extraction checks explicitly use `-ssc`
so the distinct `Case` and `case` members are checked separately. The earlier oracle failure and the pinned
reader-only supplement are retained; application tests were reused after that helper-only correction.

Remaining production work includes the typed job/executor/availability registry, operation-owned queued password
lifetime, Pack and mutation dialogs, read/view/edit password sessions, reviewed collision/exclusion presentation,
F6 source-removal ownership, Unix update/confinement and the remaining resource/crash/fuzz/RID/package gates.
No new profile is enabled by this adapter. The final independent review still follows the completed integration
and qualification campaign.
