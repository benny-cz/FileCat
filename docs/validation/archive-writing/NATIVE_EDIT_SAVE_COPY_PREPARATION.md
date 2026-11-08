# Native preparation of a saved edit copy

Candidate `3143d6f9` adds the retained-byte/private-destination preparation part of native Save copy.
It does not publish a user filename, replace an existing file, register the native store or remove the
guard in `EditSessionStore.PublishSaveCopy`. Destination publication, recovery and application integration
remain required.

## Preparation

`EditSaveCopyPreparation` borrows the actual retained edit snapshot and a distinct native creator-owned
stage with an exact-size reservation. It reviews the source, destination-bound free-space accounting
and unused creation scope before writing. Empty/small files use a conservative one-KiB accounting
minimum while their native byte bound remains exact. The returned space observation stays available
to the eventual publisher; an unknown capacity is not turned into a known one.

The coordinator reads bounded views, hashes every consumed byte and requires complete EOF, exact length
and SHA-256. It seals and hashes the private output, applies the snapshot's exact timestamp and opaque
origin mark, then completely reads the sealed output through a bounded read view. Native ownership,
source bytes and output metadata are reviewed again before returning. A failed attempt stops private
writes and retains uncertain output; this operation has no publication/deletion method or pathname
source fallback. Both native owners remain the caller's responsibility through later reconciliation.

Windows and Unix stages implement the narrow `IEditSaveCopyStage` metadata operation. They preserve
their creator-private security, other metadata and non-origin stream/xattr inventory. Origin bytes
are copied without replacement; Windows uses the opaque snapshot stream policy rather than the
stricter aggregate archive-creation mark policy. An ordinary saved file is not represented as a
codec-verified archive or given archive manifest verification facts.

## Validation

Windows-platform and Core test projects build with no warnings/errors. The initial Mac and Ubuntu
native gates each pass all 17 cases; Windows also passes all 17 in the corrected `v23` cohort.
The `v22` cohort also runs the connected storage, snapshot, legacy ZIP, SFTP, history and UI gates plus
complete Core/Remote/App inventories. The full Core comparison requires 170 additional cases over the
preserved baseline (the earlier 153 plus these 17), with the same five explicit schema/name migrations
and no other missing cases or lost passes. Ubuntu's complete inventories pass (8,510 Core, 2,543 Remote,
2,765 App; platform skips remain explicit). Its sealed evidence is preserved and verified. Mac's App
inventory and Windows' corrected complete inventories remain pending.

The real-file matrix covers empty/multi-buffer bytes, exact origin and time, later editor saves,
cancellation, disposed/changed/replaced sources, replaced destination directories, corrupt source
and output views, mismatched reservation/length/kind and reuse of an already sealed nonempty stage.
It also asserts that preparation never creates the selected public filename and keeps the working copy.

The prior `v21` Mac/Ubuntu complete inventories pass and their exact compiler-source/PDB/DLL evidence is
preserved. The Windows `v21` inventory encountered an [unhandled comparison close failure](COMPARISON_CLOSE_FAILURE_DIAGNOSTIC.md);
its failed evidence is preserved and the corrected `v23` candidate also includes this preparation matrix.
No profile/factory qualification follows from
these preparation gates. Complete native Save copy publication/overwrite/recovery, safe discard/adoption,
Unix cleanup/recovery UI, retained disk/resources, production composition, live desktop/accessibility,
remaining worker/fuzz/RID/package/CI gates and the final independent review remain open.
