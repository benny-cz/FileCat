# E-I357 — retain the outer ZIP index throughout nested-member copying

Baseline **04f6f1866edcfd24cf6d31a81b57f8c3f03b44b5 /1616 raw Git blobs**, with one identical sixteen-case test overlay. I356 protects direct reader acquisition, but nested ZIP admission still released the outer cache lock before copying its member into a private spool. A concurrent unavailable-identity outer request could close that index during the copy, producing disposed-archive exceptions.

ZIP now holds its cache lock from outer index lookup through the complete nested-member copy. The outer path is resolved before that lock and passed into GetIndex, avoiding recursive nested resolution under the cache lock. Spool safety checks, bounded ownership and the existing reader leases remain. The final proposal has exactly **two overlays: one product file and the identical control file**.

Controls cover ZIP/TAR outer archives, ZIP/TAR inner archives, two/eight actual concurrent callers, known/unavailable stable identities and eight rounds per case. Original outcomes are host fifteen passes/one failure, Ubuntu fourteen passes/two failures, Mac fifteen passes/one failure and Windows guest fifteen passes/one failure. Every raw exception and healthy observation remains. All **64 final cases pass without skips**, with **2560 complete 4096-byte reads independently reconstructed**. Every observed native source handle closes while its wrapper remains retained; no forced GC or injected product hook is used. Source hashes remain unchanged and each owned case root is removed.

**7178 affected records /177 exact skips** retain all7114 I356 combined Core predecessors and add64 existing archive-reader controls. The **377-case headless App** inventory retains365 passes/twelve exact skips, including all29 unchanged edit-preparation cases. Independent postchecks rehash **1161 native files** and find no owned workers, temporary inputs or open Windows transport listeners. Four host scratch roots/twelve files are archived, independently verified and restored.

The first control compile failed because of a local variable name collision; corrected v2 is identical across the paired host/native producers. An initial independent-reader preparation selected the word qualify inside a helper string and stopped on a syntax error before writing a reader or executing qualification. V2 corrects only that preparation boundary. All source versions, raw baseline failures and qualified final outputs remain; no producer rerun or outcome reclassification hides these errors.

This preliminary remediation addresses nested copy admission. It does not establish atomic path/open-handle identity, arbitrary in-place mutation, every spool-eviction interleaving, whole-process native allocation or candidate qualification. I06/I106 remain open; I106/I110 physical-source validation stays on HOLD. Original earlier CI failure causes remain unproven. No workstation UI, physical source or persistent settings changed.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `nested-reader-admission-safety-20261011-v1/independent-final-v1.json` | `f7797c8a887bb5adc08566b824f53d8a62fe6052d88e84dd557c2f2a15618697` |
| `nested-reader-admission-safety-20261011-v1/retained-tool-sources-v1.json` | `58bd8f2262db528eb699e5ac703a1d8def3290b5385e6d6e6915a295ceb21149` |
