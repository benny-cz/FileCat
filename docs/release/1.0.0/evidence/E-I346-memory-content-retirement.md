# E-I346 — retire disposed in-memory content payloads

2026-10-11 CEST. Baseline **d909333cd45a3bab4e1571bd0e8c7e6502298be4 /1572 canonical raw Git blobs**, declared test/product overlays. `MemoryContentSource.Dispose` does nothing, so a retained closed wrapper or disposed paged reader continues owning its entire byte array and permits further reads. Actual TAR members up to the existing 32 MiB in-memory threshold use this source; other callers supply their own arrays. Individual archive thresholds do not bound the number of retained closed consumers.

The source now atomically releases only its array reference and rejects new reads after retirement. Immutable name/length/revision metadata remains available. Caller-owned bytes and independent live wrappers remain unchanged; an active read keeps its local array until it returns. This introduces no new memory cap or array clearing.

Identical final controls retain **eight original failures/six healthy cases per baseline** and pass **fourteen local/42 native corrected cases**. They hold actual direct sources, disposed paged readers and materialized TAR members, measuring payload reachability at 4096 bytes/4 MiB while the wrappers stay alive. Complete deterministic byte hashes, actual paged-cache bytes/retirement, closed archive handles and caller-owned/independent live arrays independently verify. Garbage collection observations establish this reference retirement; they do not establish a process-memory ceiling or native peak. Earlier v1 controls remain separate from the stronger actual paged-read v2 producer.

Every **6341 affected predecessor outcome/message and 27 exact skips** remains: 1415 local records and 1642 on each native platform. Fresh postchecks verify **1161 native files**, no owned processes/temporary roots and stopped Windows listeners. Windows runs at measured high RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501 with pinned .NET 10.0.12. All owned host roots, complete logs and synthetic bytes are retained/verified before restoration.

The complete default headless App suite passes **1621 cases**, retaining **31 explicit skips**. Its exact inventory and diagnostics remain in the independent receipt.

No workstation UI, physical source, package or persistent setting changes. Wider concurrent/native allocation, materialized/hidden consumer, reference/human and exact installed-candidate resource qualification remains. Exact committed/hosted follow-up for this product correction is separate. I06 remains open; physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `memory-content-retirement-validation-20261011-v1/independent-final-v1.json` | `fce22dca158a32fafdd4a458ae4a59054aa2170445cf8db92b333082fb9fc5d6` |
| `memory-content-retirement-validation-20261011-v1/retained-tool-sources-v1.json` | `80ca2068bf31c35eb54280bdfc6b37ea15c847ccbdc780187c5f7516c4d03871` |

[Exact committed/native follow-up](E-I346-native-qualification.md) passes at ec5868e. [Original CI](E-CI-memory-content-retirement.md) retains one existing Mac HexOverlay failure; all 56 new memory controls pass. Their exact identities, predecessor inventories and limitations remain separate from this original paired producer.
