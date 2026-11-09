# Native edit storage accounting

The draft native accounting component measures retained default-stream bytes and conservatively charged native
metadata through held private namespaces. It includes working files, complete/interrupted captures, state records
and other ordinary files in the admitted session/capture/file layout. Unknown directory layouts, symlinks,
unsupported streams, changed native observations and exhausted review bounds refuse a complete result.
No saved JSON length, prior cleanup state, pathname prefix or age can refund a byte or authorize deletion.

The reader limits the complete traversal to 8,192 namespace entries, 128 sessions/captures, 8 GiB of charged bytes
and 128 MiB of metadata. These draft edit-store limits are separate from archive job/codec budgets and require
resource tests before enabling composition. Unix measurement includes bounded ACL/extended-attribute/origin data.
Windows requires a complete supported stream inventory, counts the exact origin and charges the maximum bounded
security descriptor. Payloads are not hashed just to measure usage. Native namespace enumeration accepts an
operation-specific count and cancellation token, enforcing the bound while accumulating names.

Accounting is currently a read-only foundation, not durable admission. The next connected implementation must
hold a root-wide cooperating-process write gate while observing usage and reserving/creating working files or
captures; charge possible destination stages/receipts; retain charges after interrupted operations/restarts; and
integrate fresh space checks and safe cleanup. External editor writes remain external mutations and require a
fresh bounded review. No claim is made about atomic global filesystem usage or an OS disk quota.

Core/Windows-platform compilation passes. Native runtime, resource/error/concurrency cases and the complete
connected admission workflow are still required. The running `v29` cohort contains earlier frozen runtime bytes,
so its results cannot qualify these new sources. No store or archive write profile is enabled by this draft.
