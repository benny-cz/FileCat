# I365 — charge retained archive directory prefixes to the index budget

**Status:** remediated preliminarily at `a21fd0aebf282224febb03262af893e23eea5eb2`. Exact candidate, total managed-memory, wider archive and publication qualification remain open.

The ZIP and TAR indexers retain one string and directory node for each generated ancestor of an archive member. Their cache estimate charged member names and entries, but omitted those generated prefixes. A single valid archive member with 1,024 `d/` path segments retains **at least 2,097,152 bytes of UTF-16 prefix characters alone**. Against exact baseline `165085a2f4050c970b1dbaf11ab121ec6b9d0ac0` / 1,643 raw Git blobs, the TAR index reported 8,421 bytes and ZIP 8,696 bytes. Under a 65,536-byte retained-index limit, opening three such archives kept **three** indexes. Both identical baseline tests fail their minimum-prefix assertion; the first shorter baseline also fails and remains retained.

The indexers now charge the UTF-16 length of every newly retained ancestor path plus a conservative per-directory node allowance. The final paired test reports **2,269,413 bytes for TAR** and **2,269,688 bytes for ZIP** on the first archive. A third opening now evicts an older index, keeping the documented minimum of two. This is an index-cache estimate and eviction check, not a claim to have measured the full process heap or a universal memory ceiling.

The identical fixed test passes both local controls. The affected selection passes **430 cases with 18 existing skips**, retaining all 446 previous names, outcomes, messages and skips. Windows 11, Ubuntu and macOS proposal runs pass eight controls each (the six earlier deep-path byte controls plus both budget controls). Their affected selections contain **1,552 passes and 77 exact skips** across 1,629 records; all 1,623 predecessor outcomes and skip reasons remain. Fresh guest postchecks verify 907 staged/runtime files and no owned test process or temporary fixture residue.

Exact committed `a21fd0a` source is independently exported as **1,646 raw Git blobs with no overlays**. Its eight local and 24 native controls pass, with the same complete recovered bytes, budget observations, affected outcomes and guest cleanup. The initial exact-export reader assumed every checkout file used CRLF and stopped before compilation when the new test used LF; the corrected reader accepts either byte-identical or CRLF-normalized checkout content while building only raw Git blobs. In the proposal export, `Before.Path` named a file later overwritten by the producer; the first independent reader rejected that stale path. The next reader rederived and checked the original bytes from the pinned raw Git archive, retaining the erroneous path and first reader rather than rewriting their history.

Original-push CI remains separate. The two newest indexes may exceed the configured limit by design; other retained objects, process-wide allocation, candidate workloads, physical-source behavior and I06 remain open. No host UI, source device, contract freeze, candidate or stable publication occurred.

## Selected immutable receipts

Paths are relative to the private `FileCatReleaseEvidence` root. Nested receipts pin actual source overlays, commands, original failure messages, budget numbers, per-guest results/skips and cleanup.

| File | SHA-256 |
|---|---|
| `prefix-budget-host-qualification-20261011-v2/independent-final-v1.json` | `5584f7f75a94393aee0e167ea1f5eb59ed74ba1025c470cad6983ced0962b69f` |
| `prefix-budget-native-qualification-20261011-v1/independent-final-v1.json` | `719568021981565216d83015e6f9a1a8a7cea8d75bca2fa605e802dbd9dfd950` |
| `i365-exact-host-qualification-20261011-v1/independent-final-v1.json` | `589468fb8f5d143261e133f4c0d19fc9d70d09fe07e516a70aa5f9c5f446ddab` |
| `i365-exact-native-qualification-20261011-v1/independent-final-v1.json` | `820ca81e666e82a7de5d02408c9cdf615fc69488d3c7dedf930232f12cab9d66` |
