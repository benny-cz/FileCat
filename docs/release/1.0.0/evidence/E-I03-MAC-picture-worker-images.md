# E-I03-MAC — actual Mac picture-worker file provenance

**Finite native I03/V20 observation at 3b84bc1081ca2de6423715ef055e432ef6b5557b.** The actual production Mac worker factory decodes one owned 1,024 × 1,024 uniform BMP as ordinary user benny (UID 501), returns all 4,194,304 exact pixel bytes and exits naturally with zero. Current-file origins are retained during its actual lifetime.

## Native execution and observed files

The Mac is arm64 macOS 27.0.1, with installed .NET 10.0.12. The unchanged clean development payload's FileCat DLL is launched by its real `PictureDecoder.WorkerCommand`/`Worker.Start` path through the installed dotnet host. On Mac this factory uses ordinary redirected process creation, with diagnostics disabled; it does not use the Windows restricted launcher. A private observer obtains the real child PID and holds the pixel-output pipe while `vmmap -w`/`ps` and file-byte retention run. The 35-byte success header, original/output dimensions, BMP format, frame count, flags and absence of trailing bytes are checked independently.

Both captures report 523 distinct file paths. Twenty-nine are readable individual files; their before/copy/after hashes agree while the worker remains live, and the raw-only continuation rechecks current origins after its natural exit:

| Origin | Mach-O | PE with CLR directory | Other data | Total |
|---|---|---|---|---|
| Exact pinned App development payload | 1 | 5 | 0 | 6 |
| Installed .NET 10.0.12 | 6 | 11 | 0 | 17 |
| Installed macOS | 4 | 0 | 2 | 6 |
| Total | 11 | 16 | 2 | 29 |

The six product files are FileCat App/Core, Avalonia Base/Controls, managed SkiaSharp and the universal macOS native SkiaSharp library. The two data files are installed Helvetica and ICU data. The remaining **494 reported paths are unavailable as individual files**; their bytes are not claimed as retained or verified. The raw mapping report preserves the named system/framework paths and its corpse-snapshot target description; the original worker's live-before/after checks and later natural exit are distinct observations.

## Independent structure and package checks

A Python bounds/structure reader and 33 retained native `otool` commands agree on the eleven Mach-O files' CPU types, command counts/byte sizes, every load-command name/size, and dylib names/offsets/timestamps/versions plus weak/upward annotations. Their FAT/thin containers contain **23 declared architecture slices**, not 23 demonstrated loaded slices. The reader uses copied installed SDK headers and Apple's [Mach-O loader definitions](https://github.com/apple-oss-distributions/xnu/blob/main/EXTERNAL_HEADERS/mach-o/loader.h) and [FAT definitions](https://github.com/apple-oss-distributions/xnu/blob/main/EXTERNAL_HEADERS/mach-o/fat.h). Sixteen PE files have nonzero CLR directories; this classification is distinguished from the native structure comparison.

Four observed dependency files match exact selected members of three retained archives: Avalonia 12.1.1, SkiaSharp 3.119.4 and SkiaSharp.NativeAssets.macOS 3.119.4. Actual assets, canonical source lock and metadata content hashes agree. Raw archive SHA-512 separately agrees with its cache sidecar; all four raw archive hashes differ from their metadata/lock content hashes, and both hash domains remain recorded.

The independent seal checks all 1,102 raw producer blobs/modes/source-archive members, all 141 original App payload files, three private observer builds and 438 complete input references/archive members. The uniform BMP and every expected BGRA byte are reconstructed independently in Python. All 425 retained private files, raw native tool outputs, snapshots, origin/package comparisons, command statuses and failures are pinned.

## Retained failures and restoration

The first patterned BMP oracle assumes identity output. The real drawing stage applies Mitchell sampling even at 1:1; 675,653 channel bytes differ, while its worker header, complete output length and natural zero exit agree. The observer abort and original output remain intact. A fresh uniform-color fixture supplies the successful independent byte control. The subsequent [I190 picture-fidelity correction](E-I190-picture-pixel-fidelity.md) separately reproduces the supported pixel failures, preserves this original observer/output, and validates the changed producer with full local/four-lane controls plus every original patterned Mac pixel. This record keeps the exact 3b84bc1 provenance observation and uniform control; its original failed check is not relaxed or replaced.

The successful uniform worker is followed by a controller failure because `-L`/`-l` log filenames collide. Fresh analysis-only v12 uses distinct names and the original saved snapshots, without relaunching FileCat. The independent reader first omits weak and then upward display annotations; both failed sources/receipts remain, and fresh v16 compares the complete saved data. No worker, native capture or CI rerun erases those failures.

After retrieval and independent verification, both owned Mac stages are removed. All 552 stage files are checked for owner/path/no-symlink identity and their retained inputs/results are rehashed before deletion. All six owned PID absence checks agree; bounded awake helpers have exited, and parent-directory/persistent sleep/lid settings remain unchanged. No VM, physical source, USB, product, contract, candidate or publication changes.

This proves finite current-file provenance. It does not establish mapping-time memory authenticity, every reachable/static component, full SBOM, containment, signatures, legal eligibility, native desktop/human/reference acceptance or candidate qualification. I03/I08 and the wider campaign remain open.

Private `FileCatReleaseEvidence/mac-worker-images-20261007-v1`:

| Path | SHA-256 |
|---|---|
| native-session-v11/results/observer-command.json | 38f8f16648cedb3d273e966ca1d36aeab932fa05906094122a0a2888d7044ee9 |
| native-session-v11/results/outputs/worker-observations.json | 0dc1770d32ede76e0f82e09a271370bbcb5bb9fbcf4bcefeeb571e3edf2abd17 |
| independent-macho-reader-v16.json | 36cf52d14bc5879d90491fd6e7a9867587378d19b93e766984be1b2b80921b15 |
| owned-package-matches-v17.json | 70354ec6fc0cf9a2c268c533dc89d6a1514d9f52e329d4bde6d694e7a2220ef4 |
| owned-mac-restoration-v19.json | 92c22702572a018b9e14c3776f13a8b88cc4714b24013e4c25b893ea4fcd1691 |
| independent-native-worker-v20.json | a3efc4f1058363bcc3b9da7d2984b40595c7b8d76d4e09dcf1c6f8267c38e3d4 |
