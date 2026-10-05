# E-I134 - empty 7z members are falsely classified as encrypted

**Requirement:** V10/V13, ARCH-001, PI-05; truthful archive metadata and readable supported members.
**Severity/disposition:** Medium, core archive correctness; must fix.
**Status:** remediated; corrected identical corpus and working affected tests pass; clean CI/native next.
**Baseline:** actual committed Core/Archives/Recovery DLLs from clean
`578a0edd06f1269b235ef21ce91ec38280833f3c`; other private probe dependencies retain the pinned fdb17b4 baseline.

Independent 7-Zip 24.01 creates a solid and a non-solid archive containing an empty file, small/large
files, Unicode names and a nested ZIP. Technical listings identify `empty.txt` as zero-length and
unencrypted; independent extraction yields its exact empty content. Actual FileCat marks it Protected
and returns null from OpenContent in both archives. Fifty-five other byte/hash content controls pass.

The wider corpus has thirteen fixtures: ZIP, TAR/bzip2/xz/zstd, a single zstd stream, generated 7z/solid
7z, upstream 7z/LZMA2/solid and RAR4/RAR5/solid. All seventy-eight file-member initial/narrowed name/size
controls pass against independent manifests, including typed identity, frozen relative scope and
the corrected unknown-size zstd exclusion. Search creates no scratch directories or nested member
matches. Intentionally included directory and source-archive matches are distinguished from file-member
controls. All archive inputs remain unchanged; candidate/native interaction is not claimed.

Private `archive-search-formats-20261005/corpus-v5` retains fixtures, independent tool commands/listings/
extracts, manifest, actual producer/DLL pins, private source, full observations and verification.
`independent-baseline.json` SHA-256
`94c229eb4d0a0bc1987443e4ce67dba7e1f3eb16e4de15f856d5317d16ebb58f`.
Failed private setup attempts remain retained: Windows libarchive's USTAR filename encoding differs
from the Python UTF-8 fixture assumption; compressed TAR needs two independent extraction stages;
two earlier probes abort on the same real empty-member refusal before recording the full corpus.

Pinned SharpCompress 0.50.4 delegates encryption metadata to a nullable 7z compression-folder lookup.
A member without a data stream has no folder; that lookup can report encryption despite no AES stream.
FileCat currently forwards that value into its protected flag/content refusal. The pinned reader also
contains explicit empty-stream handling. Primary sources:
[SevenZipEntry](https://raw.githubusercontent.com/adamhathcock/sharpcompress/0.50.4/src/SharpCompress/Common/SevenZip/SevenZipEntry.cs),
[SevenZipFilePart](https://raw.githubusercontent.com/adamhathcock/sharpcompress/0.50.4/src/SharpCompress/Common/SevenZip/SevenZipFilePart.cs),
[SevenZipArchive](https://raw.githubusercontent.com/adamhathcock/sharpcompress/0.50.4/src/SharpCompress/Archives/SevenZip/SevenZipArchive.cs).

Remediation must preserve encrypted nonempty/header refusal, content-free member-name enumeration,
declared-size/expansion bounds and correct empty-file identity. Earlier archive passes do not prove
this boundary. Corrected identical corpus, regressions, affected CI/native execution and candidate
qualification remain. No candidate or human GO; overall **NO-GO**. Both VMs stay running, Mac work is
owner-deferred and G: remains untouched/HOLD.

## Working correction and revalidation

A narrow workaround reads the pinned dependency's existing HasStream header metadata for zero-length
7z entries. It clears the false encryption flag and supplies the known empty stream only when that
metadata explicitly says there is no data stream. It does not infer absence from declared size alone.
Unavailable metadata preserves the prior protected refusal; encrypted data streams and encrypted
headers remain refused. No member content is opened during enumeration, and no dependency is upgraded.
The dependency has no public HasStream entry API, so the workaround uses three cached property lookups;
published regressions must keep detecting future package/metadata incompatibility.

Four independently created/decoded owned fixtures cover distinct solid/non-solid structures, encrypted
nonempty data beside empty streams, and encrypted headers. All four new regressions pass. The same
thirteen baseline archives, same private probe and same search scopes now pass all **78 file-member
search controls and 57 byte/hash content controls**, with only FileCat.Archives.dll replaced. Inputs,
all other probe files and search scratch behavior are unchanged. Affected Core passes **98/100** with
two declared benchmark skips; headless Find passes **4/4** without skips. Source/fixture/compiled DLL
and full-case pins independently verify in `i134-working-v4/independent-working-v2.json`. The initial
private proof checked all 57 controls but retained a copied summary count of 55; it remains retained
and is superseded by the derived-count proof.

The affected run exposes I135's unrelated disk-spelling fixture assumption: nine Core and one App
initial-search identity assertions fail under default `C:\WINDOWS` TEMP. Their retained failures,
test-only canonicalization and same-default-TEMP passing rerun are separate from this production fix.
One intervening private App build misses the required namespace import and remains failed/retained.

The repository's opt-in search benchmark passes without skips on the shared host regression profile:
50,000-file name search 88 ms/first result 7 ms; ignore-case content search 3,694 ms/46 MiB allocated;
200 MiB content search 83 ms; cancellation settles 9 ms after the request and labels results incomplete.
Owned C: temp files are removed. Raw output/TRX, exact compiled DLL pins and cleanup are retained in
`search-benchmark-v1`; this does not qualify the exclusive physical V16 reference-machine profile.
Clean committed-source CI/native and final-candidate requirements remain; overall **NO-GO**.

Working derived-count proof SHA-256 `f836cffe030e58578f12fe6888d67d0083ef35eec55a508b912c7fb03442172c`.
Benchmark run/cleanup SHA-256 `d5b204be3acdd81bcae1770a18c168393b9add4f2983f90f5df2f82d4b7b9859` /
`9cd68237d292644417ac9ed2d4b48753b5d1232b02cce43c643ec84c1f4ec6f0`.
