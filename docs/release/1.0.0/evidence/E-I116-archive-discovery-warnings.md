# E-I116 — initial archive searches retain provider warnings

I116/V13. Medium, partial search scope. Preliminary working-source evidence: base
`57de8af705a0f887def3b2d141f45bf8239a5cb7` plus the three-file overlay in `independent-results.json`.
Windows 26220 host, SDK 10.0.401, component/headless controls. No candidate/native input/frame evidence.

Two valid controls fail against unchanged production. An owned USTAR has a valid first member and a non-octal
size in the next header. The independent system TAR reader accepts the first member and rejects the next.
FileCat retains the usable prefix and its archive provider reports damage, but the Find adapter discards that
warning; the result and search log show no issue. An owned ZIP with duplicate names and a second member folder
likewise loses its provider warning. A first fixture mistakenly used a ZIP-only detector instead of the app's
composite detector; that four-failure attempt is retained and is not four product failures.

The adapter now forwards non-fatal provider warnings. Initial archive search reports an archive-wide warning once,
even when it is repeated for several member folders, within the existing 5,000-entry log bound. Usable members
remain available with their identities; the log keeps the original archive's folder/name for navigation. Member
contents, parser limits and nested-archive policy are unchanged. The compatible default interface overload keeps
older listers working; actual registered FileCat providers use the warning-aware route.

The two warning controls now pass. Two independent positive controls also pass for **TAR and gzip-TAR** result
narrowing through the actual archive provider: original duplicate ordinal, size/modification criteria, relative
folder and exact `x` content are preserved with archive discovery off. These extend I115's ZIP-only working cases.
All **32 affected archive/search-criteria controls pass**, followed by full Core **729/775 with 46 declared skips**
and App **258/279 with 21 declared skips**, including both new Find flows. An intermediate observer compared the
typed temporary path with a canonical disk-case path; its two failures are retained separately and the observer
now compares actual disk spelling.

Independent checks verify 1,387 baseline/final directory input files, eight final source copies, the active test
assemblies, every direct XML result/skip and 1,404 retained files. Directory inventories include unused nested
build outputs; qualification is limited to the active assemblies named in the manifest and actual run logs.
These are preliminary component/headless checks, not candidate/native presentation evidence.

Private root: `artifacts/release-evidence/i116-archive-discovery-warnings-20261004`.

| Evidence | SHA-256 |
|---|---|
| Valid baseline XML: two failures/two positive controls | `9d592c69d4a9d6bb5afe2ff529c0fa64124e5d807d9235f1dbe7473e8f770b9b` |
| Corrected 32-case XML | `41524a7438dca48416377cf4884ed7bdf2a089d9c276fc5402cf31681a6e102e` |
| Full Core / App XML | `41bd4a535283691d8da2c9d337104e7bf7547e32f28abefebb047e1a7ece0c40` / `82edbf9415d3fc8baacda3584ce92e09ae077ebed7dc6e95714c5c25e475cdb9` |
| Final source/payload manifest | `9de3a86a71978cf8c3cc8a1abbf09ceaa9e53fff1eef71f0b3a0c5ae51051740` |
| Independent input/XML/skip inventory | `017eb9765b51b17bf7d3acd33c862d81352f065768c5e5bf7319f4f182f74a6f` |

Remediation verified preliminarily on working source; clean CI/guest execution is next. Other formats, native
presentation/AT and exact candidate checks remain required. USB G6 quarantine and overall **NO-GO** remain.
