# E-I358 — bind a known source identity to its opened handle

Baseline **dfe32b33cbb6c53203fd15cb4636f8f0db2987be /1619 raw Git blobs**, with one identical eighteen-case control overlay. Archive and regular recovery-image cache keys observed path identity before opening the source, but did not compare that identity with the actual opened handle. An independently replaced, same-length/same-time source could therefore be admitted after the path observation. Known path identity could also be accepted without an available handle-identity capability.

ZIP, general archives and regular recovery-image sources now compare a known expected identity with the actual opened source before parsing or reading. A changed or unavailable opened identity is refused and its handle closes. General archive readers propagate the check to source opens and lazy reopens; AppServices supplies actual Windows file IDs and Unix device/inode identities. Original reader entry points remain compatible. Disposed lazy readers refuse reopening and release the guard callback; this last property is source-review evidence, not empirical weak-reference/lifetime qualification. Physical-device opening and device cache keys are unchanged. The final proposal has **sixteen overlays: seven product files, the control, an identity helper and seven existing fixture capability updates**. Every predecessor fixture assertion is unchanged.

Controls cover ZIP/TAR/regular FAT12 images, matching/unavailable/path-only capabilities and unchanged/independently replaced sources. An actual independent thread replaces the owned source through the existing identity callback; actual distinct path IDs and positive opened-handle controls verify on each platform. Each baseline has **nine passes/nine failures**: three actual path-to-open replacement admissions and six known-path/unavailable-open-identity admissions. These are distinct failure classes. All **72 final host/Ubuntu/Mac/Windows guest controls pass without skips**, with **36 required refusals and84 complete4096-byte healthy/retry reads independently reconstructed**. Complete ZIP/TAR members and32768-byte FAT12 images also reconstruct. All120 original complete reads and36 failed assertions remain. Every refused source closes exclusively and every case root restores.

**7242 selected affected records /177 exact skips** retain all7178 prior combined Core outcomes and add64 existing nested-reader observations. The **377-case headless App** inventory retains365 passes/twelve exact skips, including all29 unchanged edit cases. Independent postchecks rehash **1161 native files** and find no owned workers, temporary inputs or Windows transport listeners. Five host scratch roots/53 files are archived, verified with exclusive access and removed.

The first baseline compile lacked an archive namespace import. The first proposal passed the targeted controls but produced24 affected reflection failures after constructor arity changed. Corrected v2 restores those original entry points and passes the unchanged affected assertions. These raw failures, prepared versions and independent qualification remain. No successful rerun reclassifies an earlier failure.

This preliminary remediation qualifies fresh source admission. Controlled lazy-reopen interleavings remain to be executed separately; unknown initial identity, cached path-observation races, arbitrary in-place mutation, every nested-spool eviction interleaving and atomic publication remain outside this proof. I06/I106 remain open, I106/I110 physical-source validation remains on HOLD, and no candidate or human GO is implied. No workstation UI, physical source or persistent machine setting changed.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `opened-source-binding-safety-20261011-v1/independent-final-v1.json` | `33be77361bba851d1cade2f3348e62c7e6f463e73de0d8a3c2da7c480be29cab` |
| `opened-source-binding-safety-20261011-v1/retained-tool-sources-v1.json` | `4c24886cda072e614694f3caa4b2c3fabfd47d7a7aaf47fcf760ae576ae892b8` |
