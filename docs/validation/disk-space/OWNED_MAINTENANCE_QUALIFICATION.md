# Named-inventory maintenance qualification

This packet implements explicitly reviewed deletion and interrupted-operation recovery for current-profile `.fcdsa` named inventories. It does not qualify the complete persistence gate, generic source operations, comparison-file maintenance, replacement, retention, or physical desktop testing.

The browser offers deletion and recovery in memory and indexed views, including at the shared eight-tab limit. Opening another observations tab still requires admission before reading its inventory. The final themed review names the file, its private location, captured size, time and consequences. **Keep analysis** is the default/cancel action; **Delete analysis** is danger-styled. Sources and independent already-open observations are preserved. No secure-erasure claim is made.

The process-owned `OwnedAnalysisStore` selects a generated inventory identity from its strict current catalog. Preparation verifies profile/catalog identity, native metadata and payload identities, all container bytes, checksum and provenance, and exclusive maintenance availability. The opaque review is one-use and bound to that exact store capability. Execution revalidates the objects, bytes and profile under the catalog writer lock. There is no imported-path, generic-delete or protected-object bypass.

The existing immutable catalog record moves atomically, create-new only, to a generated pending name before payload removal. This avoids allocating a new deletion journal below the normal save space reserve. Actual payload removal and close precede pending-record removal and close. Failure before payload loss attempts an exact create-new rollback. An interrupted pending record requires a fresh explicit restore or delete review; catalog reads never replay it. Missing payloads can only finish catalog cleanup. Pending entries protect their identities and names against new-save collisions while allowing unrelated saves.

Windows uses private single-link native handles, native file IDs, sharing checks and exclusive range locks; rename and removal use the held handles. Unix uses verified private ancestor/directory capabilities, no-follow relative opens, device/inode identities, exclusive advisory leases, no-replace native rename and descriptor-relative unlink with durability checks. Unix maintenance relies on the owner-private namespace and cooperating FileCat lease/writer protocol; it is not a generic source unlink-by-handle guarantee. Neither implementation can delete profile/header/lock/capacity controls or `.fcdsc` files through this capability. Actual-close or durability uncertainty remains visible.

Frozen broad batch **r910** completed successfully on all three machines. The four additional pending-recovery UI cases passed in focused batch **r916**, which reused the same verified product inputs and changed only the rendering test DLL/PDB. All original runner handles reached terminal success, and final post-run inventories contain no FileCat/dotnet test processes.

| Machine | Core | Native Windows | Headless rendering | Additional recovery UI |
|---|---:|---:|---:|---:|
| Windows VM | 1,402 passed / 48 platform skips | 75 passed | 306 passed / 1 Unix-only skip | 4 passed |
| Mac | 1,428 passed / 4 platform skips | — | 307 passed | 4 passed |
| Ubuntu VM | 1,428 passed / 4 platform skips | — | 307 passed | 4 passed |

Each broad batch first passed exactly 18 new core cases and 16 UI/native preflight cases. Coverage includes one-use/wrong-store reviews, native-object/byte/profile/catalog substitution, active payload and metadata readers, cancellation and pre-unlink rollback, failed rollback, post-unlink uncertainty, explicit present/absent-payload recovery, unrelated-file preservation, low-space reserve simulation without new journal allocation, default-preserving themed review, source/tab preservation, and maintenance at the shared eight-tab limit.

This is actual guest native-filesystem qualification and headless Avalonia control testing. It does not establish physical desktop, theme/DPI/assistive-technology, package, scale or full persistence qualification. Full implementation and the final independent UX/UI/functional/security review remain open.

The [source and artifact manifest](source-manifest-r917.json) hashes 25 source/driver inputs and 206 evidence artifacts. It records broad source freeze r910 separately from final helper freeze r916. Full product build r907 and final rendering-only build r915 completed without warnings or errors. Production code did not change between the successful broad batch and the final focused tests.

Earlier failed evidence is retained. r900 exposed test timing around the dialog availability refresh; r903 exposed the missing layout frame; r906 found an old early-admission expectation that conflicted with the new maintenance browser. Code review also found and fixed the indexed browser being disabled at eight tabs. r913's memory-view recovery fixture omitted profile-state creation that real maintenance performs; its corrected r916 cases pass on all three machines. The partial, failed and busy runs are not counted as successful qualification.

The test-only batch driver now supports verified cached reuse on selected Unix guests as well as Windows. r906 reused 226 Windows and 159 Mac inputs while changing only the test DLL/PDB, reducing preparation to 11.71 seconds. The focused final batch verified 109 Windows, 105 Mac and 104 Ubuntu cached rendering inputs and transferred about 406 KB per machine. Machines occupied by the other archive tester were postponed; none of its processes or files was stopped or removed.
