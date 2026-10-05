# E-I136 - missing middle RAR volume is not reported during listing or Find

**Requirement:** V10/V13, ARC-001/002, PI-05; truthful partial archive outcomes.
**Severity/disposition:** Medium (archive correctness and completeness reporting); must fix.
**Status:** remediated at `a1c265f`; verified preliminarily on host, clean CI and both VMs.
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

## Working correction

Numbered RAR discovery now retains an interior numeric gap independently of the decoder's endpoint
flags. Member listing reports the missing-volume warning before visiting entries, so an incomplete
entry cannot prevent that warning. Supported complete sets and safe content refusal are preserved;
no member decompression is introduced into discovery, and no dependency changes.

Ten new controls cover every complete-set entry point, the missing-middle listing/six searches,
independent hashes in both read orders, and existing missing first/last/first-only refusal behavior.
Before the production change, nine pass and the missing-middle case fails exactly at its warning
oracle. Baseline test/source/TRX pins are retained in `regression-baseline-v1/result.json`, SHA-256
`ff38c6a16d20f5e12c7a5a658adc58331ef2dc7b6bf5bca24df9a379b72c9e75`.

After the fix, all **108 affected Core and four Find checks pass**. Two opt-in archive measurement
cases (benchmark and index scale) retain explicit skips. The identical independent corpus preserves
all 54 complete-fixture search/72 content outcomes and all negative content outcomes; every one of
the seven missing-warning controls now reports the warning. Only FileCat.Archives.dll changes in
the identical private probe; all other program files, source archives and private source remain pinned.
Independent proof `working-v1/independent-working.json` SHA-256
`595cd9a9c47a35536ad6df2526dd4649024ad9ccdbb5013844d9caf0ab5b6c18`.
The first working verifier assumed both skips contained BENCH; the retained index-scale reason
corrects that private assumption without changing tests or product behavior.

Clean committed-source CI/native and exact-candidate qualification remain. Both VMs stay running,
Mac stays deferred and G: remains untouched/HOLD. No candidate or human GO; overall NO-GO.

## Clean committed-source validation

Exact source `a1c265f1feb1acde1b18d1a0854862b7b0cbb21a` passes all four required CI jobs in
[run 37250443369](https://github.com/benny-cz/FileCat/actions/runs/37250443369).
Four downloaded artifacts match server digests, and six complete TRX inventories independently
verify all outcomes and declared skip reasons. All **77 affected Core cases**, including all ten
new multipart controls, pass without skips in Windows' inventory; all four affected Find cases
pass without skips per Windows/Ubuntu/macOS App inventory. Complete Windows Core passes 787/834
with 47 declared skips, Windows App 330/345 with 15 skips and each Unix App lane 302/345 with
43 skips. ARM64 Core/App, package start/render and installer checks pass with log totals;
no per-case ARM64 or Unix Core TRX is claimed. Three tag/manual package jobs correctly skip.
Private `ci-37250443369/independent-ci.json` SHA-256 `803da2f98b5ee8993d1fb31708fc2962662c8c3d9756b19eb19a6d7c308f3e1b`.

Both SDK-free guests pass all **77 Core plus four Find controls without skips**: Windows Insider
26300/Admin/elevated at the pinned VM UUID and Ubuntu 26.04.1 x64/benny/UID 1000. All 1,271 Windows/
696 Linux payloads, 1,272/697 ZIP members, twenty-one canonical Git source exports and ten byte-exact
fixture Git blobs per lane independently verify. Native/host case inventories match. Before/after
input hashes, retrieved output pins, tracked bootstrap/controller/test/observer PIDs and owned
executable-path censuses verify; all owned processes and fixture temp files are absent. Both
initial tracked cleanup and independent post-bootstrap PowerShell/read-only root observations pass.
Private `i136-guests-independent-v1.json` SHA-256 `d1f1eacdd726337aed4a7928976a55f4aa6bd0ff57da872338c99af7c92906e1`.

This establishes preliminary ISO 9660/Joliet/UDF 1.02 and complete numbered-RAR corpus outcomes,
the missing-middle warning remedy and retained content refusal. Pure UDF/other revisions, legacy
RAR naming, native desktop/AT and exact-candidate qualification remain separate scopes. All failed
product/private attempts remain retained. Mac stays deferred, both VMs remain running and G: is
untouched/HOLD. No candidate or human GO; overall NO-GO.
