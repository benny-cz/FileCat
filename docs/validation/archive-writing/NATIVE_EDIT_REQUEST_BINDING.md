# Native archive edit request binding

`76b0c988` closes a parent-side request-binding gap found while tracing the remaining
Save copy integration. A retained snapshot already required one Replace, its captured source label,
a baseline and native review. Unlike the legacy ZIP/SFTP adapters, its native archive branch did not
also restrict the archive path, member, occurrence and profile to the originating session.

The input capability is internal and excluded from request serialization; a pathname or worker
message cannot create it. Nevertheless, an accidentally redirected parent request must fail before
input capture or publication. It now requires the originating native archive session, exact archive
path/profile/member/occurrence, one replacement, no rename target, no move, no create-replacement flag
and no addition-conflict policy. A remote snapshot cannot authorize an archive update.
`CommitArchivePlan` applies the same check immediately when it assembles a native snapshot request.

An explicit rebase can still use a fresh baseline/native object at the same selected archive path.
The complete member review and explicit rebase decision remain required; this binding does not pin
the old archive version after that decision.

The existing snapshot matrix now creates sessions through the archive adapter with the corresponding
explicit TAR/RAR/7z profile and actual native working/capture storage. Its contract assessment remains
a controlled fake; these cases do not claim codec/publication qualification. The request-binding case
adds archive/member/profile/occurrence/rename/redirection checks, missing native review/catalog,
create/add flags, remote-to-archive refusal and rejected/approved rebase. Existing source/output byte,
origin, editor-save, metadata, quota and cleanup assertions remain in place. Windows supplies its real
file identity implementation to the adapter fixture. Case counts remain 35 Windows / 34 Unix.

Both Windows-platform and Core test projects build without warnings/errors. Candidate `39e0f0c6`
passes the first native snapshot gates on all three platforms (35 Windows / 34 Mac / 34 Ubuntu,
including the expanded binding/rebase case) and the expanded checksum UI gates (12 passed, one
platform-specific skip). Ubuntu also passes the retained SFTP gate (23); complete inventories and
sealed evidence remain pending. Three-platform
execution continues as `awe-storage-v21`; it also includes destination-neutral
snapshot failure wording (the same retained source can feed ZIP, native archive or SFTP commits), the checksum fixture expansion from `v20`
and complete Core/Remote/App inventories. Mac/Ubuntu `v18` finished with no failures in all three complete
inventories and their raw evidence is preserved. Windows `v20` also finished with no failures (Core 8,089
passed / 594 skipped; Remote 2,405 / 264; App 2,835 / 118), its exact evidence is preserved, and it has
started the same frozen `v21` candidate. The application still
uses the legacy edit store and no profile or native-store factory is enabled.

Native Save copy destination publication, Unix cleanup, recovery actions/UI, safe discard/adoption,
retained-disk/resources, application composition and the remaining live/worker/fuzz/RID/package/CI
and final independent-review gates remain open.
