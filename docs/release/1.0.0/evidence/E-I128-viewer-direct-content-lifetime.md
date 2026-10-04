# E-I128 — viewer direct-content calls retain the file through close

Preliminary V12 / AI-03 remediation; no candidate qualification. Baseline production producer
`9d332825237ffab2bb6f329e4a454fe78e3239e7`; execution resumes from clean main
`8b6a52a70152602b9325c5c7d538564ca090d889`. Private evidence is under
`FileCatReleaseEvidence/viewer-direct-content-20261004` in the existing C-drive evidence root.

The owner's elevated Codex restart did not restore Computer Use 26.930.41038. Prescribed initialization exits
before any target or input; one retry reports `windows sandbox failed: helper_unknown_error: setup refresh had errors`.
Exact results are retained in `elevated-computer-use.json`, SHA-256 `997fbcf89685eef4705de44a1a8347cd9a0ce3504638d21c82991931d2113f44`.
The shell identity observed at 17:20:00 UTC is Windows 10.0.26220.0, marek, **unelevated**, despite the reported
elevated desktop launch (`shell-identity.json`, `fc4c4c0429765d5b2ae521e154385dcf5b4ac9126f090fcb2e14eac1aaba7e80`). VMware command execution remains
available. Both guests stay running. No USB work was performed; the existing source-change gate stays held.

## Reproduction before modification

An independent test assembly references the actual retained App/Core/vendor binaries from the clean 9d33282
SDK-free package. Its original input manifest is rechecked against its recorded hash
`0972c6da15f0eee9b68144378c8c53e6e91065a02fcbf7ad8bcea1bbd23d577b`, clean producer identity and every copied DLL.
The headless test opens the actual ViewerWindow over an owned real FileContentSource, waits for initialization,
caches its pages and stops only the fixture's refresh timer. The actual line command, ShowInfoAsync, or HtmlPage
captured from ShowPage performs the held read. No native browser, desktop input or physical device is involved.

Four close-during-read cases fail on unchanged production: line, HTML, Markdown and Info. Each disposes its
source once while the read is active; that read then observes a disposed file. The line operation also returns
an uncaught `ObjectDisposedException`. Actual application termination was not observed. All four controls that
release the read and complete before closing pass; their line offset/content/inspection results are checked.
Every owned file's independent SHA-256 remains unchanged. Baseline v2 and the stronger identical v3 inventory
are both retained; v3 XML `121cac3cd72b5a90f73df4602a36381e1c59f5e319c5c4a21bfa4ca866dde078`. The initial probe compile error used an inaccessible
test-only status property; it is retained as a harness failure and has no product disposition.

## Correction and host validation

Line scanning borrows the source through PagedReader.WithSource, links cancellation to close, disposes its CTS
and rejects canceled or superseded progress/results. HTML/Markdown pages accept the reader owner and closing
token, retain the active resolution and refuse requests after close/cancellation, including adjacent assets.
Info borrows the source for the inspection and fallback length, observes cancellation around source calls and
rejects late reports. Encoding initialization also avoids starting a timer or a mode after close.
The existing source-only HtmlPage API and rendering/containment/size limits remain available.

The same eight-case probe passes with only FileCat.dll and FileCat.Core.dll replaced; every other input,
including the probe assembly, is byte-identical. Corrected XML `22ebf5a1b3d17f1244c30aa2ba533af5dd351218dc9533cd7f92c548c69c918d`. Close returns below
the fixture's three-second bound while the read is held, immediately retires cached pages, does not dispose
the source until the call returns, performs no further read and discards canceled page/line results.

| Working host inventory | Pass | Declared skips |
|---|---:|---:|
| Core affected | 102 | 0 |
| Headless App affected | 41 | 0 |
| Core full | 754 | 46 |
| App full | 290 | 21 |

Eight App regressions cover all four held/complete paths; two Core regressions cover owner-close refusal of
main and adjacent files and cancellation of an otherwise open Markdown reader. The two retained working
captures contain 1,096 files, each with twenty-five source/build inputs (21 C# files, two projects and two
properties files). All bytes, XML case IDs/multiplicity/counts, complete probe observations and selective
production replacements independently verify in `independent-working.json`, SHA-256
`593e1ac17b2e47bbe3e0e8b86d151789b3abb2c7fd4fc80142f686593cd2ca52`. Working source is the recorded 8b6a52a base plus the four-file overlay;
these are preliminary host results, not a clean committed build.

Clean CI and SDK-free guest revalidation now pass for the exact producer below. Native engine callbacks/input/frame timing, actual hung
hardware, aggregate picture/page/other queues and exact-candidate qualification remain open. The former
viewer passes do not qualify this direct-source close race. Overall **NO-GO** remains; no stable publication.


## Clean committed revalidation

Clean source `c45392542055a0d8dbd4ab1568f78596e38e3b01` passes all four required jobs in
[CI 37220325504](https://github.com/benny-cz/FileCat/actions/runs/37220325504), including ARM64 tests,
package startup and installer compilation. The three tag/manual package jobs are declared skipped.
All three downloaded server artifact digests/sizes, six complete TRX inventories, raw test/execution IDs,
case multiplicity and affected case names independently verify. All 143 affected Windows cases pass;
the 41 affected App cases also pass on Ubuntu and macOS, zero affected skips.

| Direct CI inventory | Pass | Declared skips |
|---|---:|---:|
| Windows Core | 753 | 47 |
| Windows App | 296 | 15 |
| Windows Platform | 166 | 33 |
| Windows Remote | 88 | 28 |
| Ubuntu App | 268 | 43 |
| macOS App | 268 | 43 |

Only the native XML runner's additional backslash/quote escaping is accounted for in TRX name comparisons;
every case and multiplicity remains checked. The live NTFS-history fixture passes. Independent CI inventory
SHA-256 `e58a0ea9a088405a9e83568a469e4e13ccfbdbb7d62b6d0b440b077390f1bdc1`; private `FileCatReleaseEvidence/ci-37220325504`.

The self-contained SDK-free guest run passes **143/143**, zero skips: 102 Core and 41 headless App cases.
Guest UUID `9D224D56-1161-A849-ABA7-2581A980895C`, Windows build 26300, administrator token. All 697 payloads,
698 ZIP members and twenty-five canonical source/build inputs verify against exact clean Git bytes, with
CRLF normalization only for source comparison. Guest root `C:/Users/Public/FileCat-viewerdirectcontent-validation-5c45262dc61242ae81dfda6b2d7661d2`;
private `clean-c453925`. The run ends at 17:34:25 UTC; cleanup at 17:36:31 UTC finds controller 7092/workers
4544/8104 and all owned executable children absent, with no temporary files. This is controlled preliminary
read-lifetime evidence; it does not qualify native browser callbacks or actual hung hardware.

| Clean guest evidence | SHA-256 |
|---|---|
| ZIP / input manifest | `396a1a529ce923af5d4dcef697874caf40bdc462de311fad37b42922dd6e22ef` / `1626fc189dd8c7074cdec3a739b5db399abbe68ef854f8f9488390c00eabbac2` |
| Runner / cleanup script | `22f96a6013b1ba00842dc34cd4c53fa886696120395bd796448e660bcc234f14` / `447b90d415c1142bf4cae160efe88ca971d0803fad6336d0023ad3948ee1242a` |
| Core / App XML | `68077dec21cf250951075386da038aea299b8bb87d38fa96acd4e171b53e07b7` / `c7e54a0203edaabc16f7bb1101d50907ceda1f80cfe25055724f220e5846f8c2` |
| Identity / exit record | `b29e8fec444159e7b1d88b4fc9a8d1bb26689d34f835f92c5b18542bd30e13d1` / `214dc7065a93b1bf197f45dcd2dd42818eb4bf82d470d859069daf539dbe742f` |
| Owned process/temp cleanup | `cfa9ea8e960aa350655937558d0bb70078c4f7b1b806932a3baa1fdc9be6af58` |
| Independent native inventory | `a4133edbbcc5c1b6159ed6e7f097d9be7cc12155aa80e45a8533d5d37997cd49` |

The final slice verification rechecks all 1,096 working captured inputs/twenty-five canonical sources and
every retained CI/native proof file. I128 preliminary remediation is verified; native/hardware/aggregate/
candidate gates and overall **NO-GO** remain.

The owner reconnected and again authorized rewriting the disposable USB at G:. Read-only volume/disk inventory
matches disk 5, Silicon Power 8 GB, USB serial `2F2000129618`, FCTEST/NTFS, size 7,796,162,560 bytes, non-boot/non-system.
No USB bytes were changed or FileCat physical-source checks performed. The earlier G6 source-change evidence
remains held and is preserved; a future authorized fresh fixture does not retroactively qualify it.
Both VMs remain running. Computer Use remains unavailable before any target/input.
