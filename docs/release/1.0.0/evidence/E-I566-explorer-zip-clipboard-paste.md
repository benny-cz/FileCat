# I566 — Explorer ZIP clipboard paste into FileCat

**Status:** high-severity UX/data-integrity issue remediated preliminarily in product commits `b57fd170` and `31b87c9a` on `main`. Exact pushed-source five-lane CI and final installed-candidate qualification remain open. This record does not qualify or authorize 1.0.0 publication.

## Report and cause

The owner copied two files from a ZIP opened in Windows Explorer and pasted into a FileCat directory. FileCat refused the selection as having no local file-system path. Explorer advertises ZIP members as virtual Shell files: the Win11 VM clipboard had `FileGroupDescriptorW` and indexed `FileContents`, with no `CF_HDROP`. Its complete descriptor manifest named `alpha.txt` and `beta.txt`, declared 34 and 33 bytes, and supplied their modified times. The previous FileCat path-only importer rejected those virtual entries before a copy job could start.

The correction reads the complete native descriptor manifest before accepting the Avalonia file list. It requires ordered, safe paths and every declared file stream, and copies each Shell stream into a private, bounded staging directory on a dedicated STA thread. That thread is required because the first native probe's cross-apartment stream read failed with `RPC_E_WRONG_THREAD`. Ordinary FileCat Copy then transfers the staged bytes. The owner's later exact ZIP revealed a consequential gap: Explorer's five selected top-level items yielded **11 descriptors**, including four nested directory records but only seven file streams. `b57fd170` wrongly required one stream per descriptor and showed the owner's reported incomplete-selection message. `31b87c9a` reconstructs the declared folder tree, maps only file descriptors to streams, and submits five root items to Copy. Empty virtual folders are also supported. A failed or changed clipboard, an unsafe/incomplete selection, a stream/size mismatch, or a failed job cannot produce a silently accepted subset. The staging bound is 1,000 descriptors and 4 GiB of file bytes; mixed local/virtual selections remain explicit refusals. External clipboard data never grants Move/Cut authority. The containing archive's download provenance is unavailable through the virtual stream, so staged files receive the conservative `ZoneId=3` mark. Staging is removed after job completion or on failure, with a startup sweep for abandoned directories. A high-integrity FileCat session refuses this staging route.

## Focused source controls

Windows x64 Release `ClipboardIntentTests` at final source: **51 passed, one existing case-sensitive-filesystem skip, zero failed**. The new controls cover exact virtual bytes/metadata/mark and cleanup, completed normal Copy job, nested folder plus root file reconstruction, an empty virtual folder, missing child stream or parent, unsafe/incomplete manifest, second-stream failure after a first file, and declared-size mismatch. The class retains the prior owned Cut, clipboard-generation, external Copy, destination-lifetime and native-drop refusal controls. `git diff --check` was clean before the product commits.

## Independent native Win11 VM check

Disposable Win11 VMware guest, ordinary interactive Admin session. A local framework-dependent Windows x64 build was packaged as `filecat-zip-test-app-pruned-v4.zip`, SHA-256 `5D601E21B1E1FFF5262A6DE889701829CEC4A0F6106F07B70B1B71D756076C7E`, and copied into the guest with the same digest. It is a local test build, not an official signed/final package. The isolated FileCat profile and destination were `C:\FileCatZipClipboardProbe\profile` and `C:\FileCatZipClipboardProbe\destination`.

The final fixture used a fresh `source-final.zip` Explorer window identified by its Shell `LocationURL`. Both entries were visibly selected, then the live native clipboard sequence **46** exposed exactly two `FileGroupDescriptorW` records plus indexed `FileContents`; neither `CF_HDROP` nor a local-path list was present. Both records declared modified time `2026-10-30T18:44:08Z`, matching the fresh ZIP entries, and sizes 34/33. This unique timestamp matters: an earlier automation attempt copied from the wrong foreground window and retained an older ZIP clipboard; that attempt is excluded from the final result.

The corrected build pasted both members through a completed FileCat Copy job. Independent guest reads found:

| Destination | SHA-256 | Bytes | Modified UTC | Download mark |
|---|---|---:|---|---|
| `alpha.txt` | `69DA94DB4112F69A47C1DFABB3D116B24460DBD16E7B00E778FA8760AE33A05D` | 34 | `2026-10-30T18:44:08Z` | `ZoneId=3` |
| `beta.txt` | `4B63EA71B6A2B6BDF9697BA46F52AFCA37FE72EF94E404725895D0CEBE8FE10C` | 33 | `2026-10-30T18:44:08Z` | `ZoneId=3` |

The exact text of both destination files matches the source members, both originals remain in the archive, and the isolated clipboard staging directory has **zero leftovers**. Guest verification JSON and the native clipboard descriptor capture are retained under the gitignored `.tmp` probe files. The Win11 guest is available for later candidate work; no physical workstation input was used.

## Owner's exact mixed folder ZIP

The owner reported that copying **all top-level items, including `docs` and `preview` folders**, from `C:\Users\marek\Downloads\FileCat_README_Update.zip` still produced the incomplete-selection error. That ZIP is SHA-256 `F4D3732C02C5F8E65AA5303D001740197D95010A32CE6A6C1A816719FE0901D3`. In the disposable Win11 guest, Explorer's own selected-items Copy generated clipboard sequence **64** with five selected root items and 11 `FileGroupDescriptorW` records: four folder records and seven file records. There was no `CF_HDROP`. A local build containing final source `31b87c9a` was copied into the guest; the guest and host package SHA-256 both equal `7A32BE95510400F7D10582C18BC0713CFFDE0AA0AA8F5B01B795133472CE2600`. This is still a local test package, not a final signed candidate.

The exact ZIP selection pasted into a clean isolated FileCat destination and completed. Independent verification compared every ZIP member's stream hash, byte count and clipboard-declared modified time against the destination. All **seven of seven** files match; all four declared folders exist; no additional destination file exists; every file has `ZoneId=3`; the original ZIP hash is unchanged; and staging has **zero leftovers**. The seven destination SHA-256 hashes are:

| Relative path | SHA-256 |
|---|---|
| `README.md` | `BD93C951DB0811C977703B4E921B1F3BBDADAE95AC07FFD4BD79F32FA3F0ABFC` |
| `docs\DOWNLOAD.md` | `A84B4FD056457042AB0768814D84AFC142B9401D0C7F1F1762F83C20D7F935C5` |
| `docs\release\1.0.0\PREVIEW3_RELEASE_NOTES.md` | `5AB74E088F3FDF47AB2E1A78D2F6A9AC08BC4EAA95635AED81DF5D5344BC419A` |
| `FileCat_README_Update.patch` | `8E85B097CE39F2F06F6FE562A3D28ED4694661A1E9DA4679E84E9FC323B64C42` |
| `DRAFT_NOTES.md` | `852059F4ED0C53451A3EC2D4BCC496221BC0C0A0FF511493C6D3FC247E95916C` |
| `preview\README.html` | `CE3064C57C69B7EA77ED6371E89C05290C6BECC2DCED2337EB6BF9211F8EDA18` |
| `preview\DOWNLOAD.html` | `0D878F5C81C30FF2F3F805BC47C047BB4CF83A28E4EF83288436BBACD787D9B3` |

The native descriptor and exact verification JSON are retained under the gitignored `.tmp/zip-readme-v6-*` files. The physical workstation desktop was not controlled.

## Release stopping rule

I566 is a must-fix for 1.0.0. Its source/native child can be accepted when the pushed `31b87c9a` source passes producer, Windows x64, Windows ARM64, Ubuntu and macOS CI with intentional platform skips reconciled. Reopen only for a consequential source change or a reproduced regression. Final candidate acceptance requires an actual Explorer ZIP copy/paste of files plus nested folders into the exact installed Windows 1.0.0 candidate, matching all selected member bytes and metadata, preserving an appropriate download-origin warning, and leaving no staging debris or source mutation. The local build and preview.3 binaries do not satisfy that candidate rule; preview.3 predates this fix.
