# E-I136 - missing middle RAR volume is not reported during listing or Find

**Requirement:** V10/V13, ARC-001/002, PI-05; truthful partial archive outcomes.
**Severity/disposition:** Medium (archive correctness and completeness reporting); must fix.
**Status:** reproduced on actual clean production inputs; remediation in progress.
**Baseline source:** `3caf48088a4aaeed1ebd471b1c4aaf9266dd9dde`.

An independent corpus adds xorriso 1.5.6 ISO 9660 and Joliet images, a genisoimage UDF 1.02
image, six entry points into a complete six-part RAR5 set, and four incomplete RAR sets.
Owned disc source bytes include empty files/folders, small and large files, Unicode, spaces and
long names. Seven-Zip 24.01 independently lists and extracts the images and complete RAR set.
Every generated disc byte matches the known source; FileCat selects ISO/Joliet and UDF correctly.
All **54 complete-fixture initial/narrowed name/size controls and 72 reverse/forward byte/hash
content controls pass**, preserving typed parent/ordinal, frozen scope and source bytes.
Initial search sees each physical RAR part as an archive; every alias's results are verified
separately, without interpreting those aliases as one deduplicated source. No search scratch is created.

Removing `Rar5.multi.part03.rar` leaves parts 01, 02, 04, 05 and 06. Seven-Zip rejects extraction
with a missing-volume error. FileCat still lists all three files without a warning; all six
initial/narrowed name/size searches finish without a missing-part log. Opening `jpg/test.jpg`
then refuses decompression in both read orders. Two unaffected members retain their exact hashes.
This is **seven missing-warning failures**, with content refusal preserved, rather than a claim
that corrupt content was published. Missing last/first and first-only sets retain their partial
warning or listing refusal; the empty narrowed scopes in those refused cases do not qualify revalidation.

SharpCompress 0.50.4's RAR entry completeness check examines only the first split-before and
last split-after flags, which can miss an interior volume hole. FileCat discovers all numbered
paths and trusts the aggregate completeness check when reporting warnings. Primary source:
[RarArchiveEntry](https://raw.githubusercontent.com/adamhathcock/sharpcompress/0.50.4/src/SharpCompress/Archives/Rar/RarArchiveEntry.cs).
The independent image command reference is the
[Ubuntu genisoimage manual](https://manpages.ubuntu.com/manpages/questing/man1/genisoimage.1.html).

Private evidence: the authorized second workspace's
`FileCatReleaseEvidence/archive-disc-multipart-20261005`, including the Ubuntu generation script,
tool hashes/version/help and installation logs, owned source bytes, digest-verified safe ZIP
retrieval, independent listings/extracts, raw Git-pinned RAR inputs, complete production DLL pins,
private probe source, observations and independent verifier. `corpus-v2/independent-baseline.json`
SHA-256 `142e5194628ccafbb873e92717f06775daa68b30f3d262de92001c429e8317fc`.
The first private build included runtime/native DLLs as compiler references and failed before
FileCat execution; it remains retained. The first verifier incorrectly used mapping values in a
Counter and failed before producing a proof; its source is retained, and the corrected verifier
compares complete key inventories. Neither failed attempt is labeled as a product failure.

Next: preserve safe content refusal, report discontinuous numbered sets before member enumeration,
repeat the identical corpus, add regressions and perform affected clean CI/native checks.
Pure UDF/other revisions, legacy RAR numbering, native desktop/AT and exact-candidate qualification
remain separate scopes. Both VMs stay running, Mac remains deferred and G: is untouched/HOLD.
No candidate or human GO; overall **NO-GO**.
