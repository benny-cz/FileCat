# E-I446 — edit-session metadata input bound

**Scope:** High automatic state-read availability finding within I06/I16. This is preliminary component and native qualification, not candidate qualification.

At exact parent `0f5ad89b8f685a6bab86d360ef8c31a969ba467e`, `EditSessionStore.LoadAll` read an entire `session.json` before parsing. Two owned 8 MiB padded records, one ordinary and one behind a short-metadata symbolic link, were both reloaded despite their unnecessary size. The same two final test cases against the parent failed **0 passed / 2 failed**. The final reader caps an opened record at **1 MiB**, reads exactly the accepted length, checks EOF, and ignores oversized or changed records; a normal saved record still reloads.

Final Windows focused controls passed **3/3** and full Core passed **4,075 passed / 119 explicit skips / zero failed**. One verified **48-member** package, SHA-256 `cea1b5cef81b5b525cddba221e2b8255ce4b3a2c3beeb243e5d89fdedebea80c`, ran the full edit-session class **4/4** on both Mac and Ubuntu; both new bounds cases passed on each system. Each native machine's 54 staged/result files were hashed before cleanup and both owned roots/stages were confirmed absent. The package manifest SHA-256 is `315eeb06347419beb35a6bea9c276af60920bf581abd967c16b7c6b9c0696af4`. The full Release solution build completed with zero errors (174 warnings).

The private archive `V:\FileCat\artifacts\release-evidence\i446-edit-session-input-budget-20261014-v1` retains exact parent/final source, test patch, baseline and final TRX/logs, native package, raw native results and cleanup receipts. Independent rehash of all **65 files** and every package member passed. Self-excluding manifest SHA-256: `594d0fd06e1217db74cce7615678b0c91f8c623a912079663b4434a2d06a2d13`; audit SHA-256: `f67801f339655662d7e5ecb24896bb133db4ac756520c0841b57152f41644463`.

The evidence qualifies bounded automatic reload of the owned record variants. Whole-process memory, arbitrary concurrent replacement, installed UI and candidate scope remain. Exact pushed-source hosted CI is separate; physical I106/I110 validation remains on HOLD.
