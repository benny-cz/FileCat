# E-I134 - empty 7z members are falsely classified as encrypted

**Requirement:** V10/V13, ARCH-001, PI-05; truthful archive metadata and readable supported members.
**Severity/disposition:** Medium, core archive correctness; must fix.
**Status:** reproduced; remediation and revalidation underway.
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
