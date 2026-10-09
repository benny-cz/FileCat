# E-I03-MANAGED — selected NuGet runtime method IL in four earlier Windows publications

2026-10-09. Bounded preliminary I03/V20 provenance evidence across all 38 selected package runtime assemblies for the original **850298359fc16cf28503cea42f3ba9c0b1599240** publications. This is neither the current runtime nor a candidate; I03 remains open.

The four original win-x64, win-x64-fdd, win-arm64 and win-arm64-fdd payload manifests and actual FileCat.deps.json runtime targets select Avalonia/12.1.1, lib/net10.0/Avalonia.Base.dll. Its actual selected NuGet assembly is compared with each original published DLL. Every original assembly/dependency manifest byte pin is rechecked. No target assembly or native code is loaded or executed. A separately compiled, five-file .NET metadata observer reads the five PE files as data; its complete original output and build/run streams remain retained on E.

All five files have **14,666 method definitions, 13,686 IL bodies and 633,472 IL bytes**. All method tokens, names, attributes, implementation attributes, signature hashes and complete logical body records match across the original NuGet input and four published outputs when only the physical body RVA/file offsets are excluded. The body records include complete IL hashes, body sizes, stack/locals and exception regions. MVID also matches. Whole raw DLL hashes and whole metadata hashes differ: neither is represented as byte-identical. Actual native managed-header directories are present in the published files, with their original architecture/header fields retained; this reader does not validate the native machine code.

A second Python reader independently parses each raw PE section mapping/CLI metadata directory and all tiny/fat method headers, rehashes every complete IL body directly from its actual raw-file offset, and checks IL size, stack, initialization and local-signature fields. It independently reproduces the whole raw metadata hash for each file. The full method/signature/exception metadata comparison remains the .NET reader's observation; Python does not independently implement all metadata tables or decode instruction semantics.

The unchanged compiled observer then reads an owned one-byte-altered copy of the selected NuGet DLL. Exactly one raw file byte and one method IL hash change; MVID and the whole metadata hash remain equal. This positive detector control demonstrates why MVID/assembly identity alone is insufficient for this byte-level comparison. The altered assembly is never loaded or executed. Its exact bytes and the one workload log are archived/rechecked before the two owned roots are removed; two files are removed, no locks remain, and the three recorded completed command IDs are absent at the recorded observation time. No system setting or source device changes.

The first independent header reader refuses before the negative-control run because the raw absent local-signature value is 0 while MetadataTokens.GetToken represents its typed nil StandAloneSig as 0x11000000. The exact original reader/tool-only assertion and diagnostic are retained. An earlier read-only diagnostic omitted __file__ and stopped before inspecting any rows. Fresh v2 corrects only the typed-nil comparison and reads the original closed output; it does not replay the original observer or any product tests. Separate original reader streams/PID were not captured and remain unavailable.

The complete actual runtime-target inventory then selects **38 managed runtime assets from 27 NuGet packages** in each original publication. The already checked Avalonia.Base results are reused; the same unchanged five-file observer reads the remaining 37 original inputs and 148 published outputs, one asset at a time. Every selected runtime path resolves through the original dependency manifest and the actual cached package/version asset. All published inputs match the original production manifests. Complete original JSON/streams/37 command receipts remain on E.

Across the 38 selected assemblies, each publication preserves the original NuGet **101,465 method definitions, 89,555 IL bodies and 5,925,563 IL bytes**, together with all captured logical method/signature/body records. Python independently decodes and hashes every additional raw PE/IL body, as for Base. All 148 additional published method vectors match. Two selected forwarding assemblies have no method bodies; their empty method sets do not imply full metadata equivalence. Whole file/metadata equality, MVID and native-header fields are recorded per actual asset; no uniform whole-file or metadata equivalence is assumed. The one-byte detector remains the separate Base control, not 38 new detector executions.

The extra observer runs create no temporary files. Their single exact empty child TEMP directory is removed after all 37 recorded completed command IDs are observed absent. No earlier evidence is relocated, no target assembly runs, and no product tests or CI are replayed.

This closes only the selected type=package/runtime method-IL subset of these four earlier publications. Project assemblies, framework runtimepack assets, reference entries, native/runtimeTargets, other platforms and later/current/candidate producers remain outside this bounded result. It does not establish NuGet authenticity, complete managed/native/static/source composition, native compilation equivalence, loader resolution, license/provider acceptance, a complete SBOM, the excluded assembly/asset classes and platforms, or current/candidate provenance. The earlier resource/native/compiler receipts retain their own producer limits. All 24 final-candidate campaigns, the physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to `C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence` unless explicit. Nested receipts retain all executed input, payload, stream and archived-control pins.

| File | SHA256 |
|---|---|
| `managed-payload-followup-v1/Program-v1.cs` | `2b9a49026eacbeb897a9d88b9a20e10accb23d02b1099a7eead975d63deccd22` |
| `managed-payload-followup-v1/run-managed-metadata-probe-v1.py` | `1e8a024e595eac3e6aa2a6f9b7b81afbeab9f76078fa5fc6ed116836cadcd238` |
| `managed-payload-followup-v1/original-managed-metadata-probe-v1.json` | `ff3c0cee46d096acac2434e3d5ffb5f19dd553a3bd5642cf7e279b318c9b404d` |
| `managed-payload-followup-v1/metadata-build-v1-command.json` | `f876167a18e50ab99513a7cb82c49fc058375b5a374980b6717ce2580d7576a9` |
| `managed-payload-followup-v1/metadata-inspection-v1-command.json` | `a6e77fc040889dc43ef8540d29687543874644e2d23604fb38e6a614f78a6826` |
| `managed-payload-followup-v1/seal-managed-metadata-v1.py` | `d0013e5d8d52797f9833bbe7b3edf4ab1059da66bf293600d53cc3d4e66b6f6c` |
| `managed-payload-followup-v1/original-managed-reader-header-refusal-v1.json` | `e05d95cea8929bfa093ca938fd9f2efc73383677943b675fb36435bc9140e6cd` |
| `managed-payload-followup-v1/prepare-managed-reader-v2.py` | `17081b11c59e5a089fd8885e80d635d9c4a2713425a072f40ab234557e6ee9f1` |
| `managed-payload-followup-v1/seal-managed-metadata-v2.py` | `676e9dd367dff48a5ba49e55ccb1ed2eb01f456ea50da0b80cc483cd06ca4c99` |
| `managed-payload-followup-v1/independent-managed-metadata-v2.json` | `99a156d7741702e29a79fdd7d293c7e853a4b2467e23410509ba050c1ed66a72` |
| `managed-payload-followup-v1/negative-inspection-v2-command.json` | `088f021727c9ec161cae86d9c2b04385e57f5dbd57489e960494498fa89608bc` |
| `managed-payload-followup-v1/restore-managed-owned-v1.py` | `5a1ff5936221c7341a75e855222a171fc905b9587556da6e1ea10e90cdd55edf` |
| `managed-payload-followup-v1/owned-managed-restoration-v1.json` | `66fb8f2c380b1822c7c2f58f7d01102ba2443184f6c056e4d91dde2d824d790d` |
| `managed-payload-followup-v1/owned-managed-temp-manifest-v1.json` | `2fd66d90d1e88daf2a0cc981ee125305cf19716290895e6f0b1e330e2713abf2` |
| `managed-payload-followup-v1/remove-owned-managed-temp-v1.ps1` | `d1b39a8c378561521f248b9039b02d6963131ca19e17b556b93b8dc3bd170f1e` |
| `nuget-locks-20261006-v2/packages/avalonia/12.1.1/lib/net10.0/Avalonia.Base.dll` | `19596afa3c31f1643812b6bacf7dead04889e61c3b465714eb3e818b9ecb1bb3` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-x64/Avalonia.Base.dll` | `acf5605e036366ae1e4a7a8bfb04de6a47e6ab9268f577dd9efa19b4bf352c8a` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-x64-fdd/Avalonia.Base.dll` | `acf5605e036366ae1e4a7a8bfb04de6a47e6ab9268f577dd9efa19b4bf352c8a` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-arm64/Avalonia.Base.dll` | `42020491ba8bf270f6d0be2e3083c6c175043240a260101ca5569542f9102521` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-arm64-fdd/Avalonia.Base.dll` | `42020491ba8bf270f6d0be2e3083c6c175043240a260101ca5569542f9102521` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-x64/FileCat.deps.json` | `b7e2c1fe639bb068a7a4305b4dd74ffec3276edee013348ab02f18aee1d9a48e` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-x64-fdd/FileCat.deps.json` | `9ce5267a5420f39a5d94126cda8244332b3a30b364bc741aac02e202942d7488` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-arm64/FileCat.deps.json` | `2c8e7397808298b7a4ebbe8051cf5a9240f6b0f4a96ca4f670d0f53e1b596b56` |
| `resource-production-windows-20261006-v1/source/artifacts/publish/win-arm64-fdd/FileCat.deps.json` | `e75610253285bec74cc0962b76ec57f30bc812e1fe24a47efd0c7f07bd418cf7` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/build/bin/OwnedManagedMetadataObserver/release/OwnedManagedMetadataObserver.deps.json` | `b85dadd00d357a2bb80a4954e6551b1b635f44e29ee39e498f3fc526f624b7df` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/build/bin/OwnedManagedMetadataObserver/release/OwnedManagedMetadataObserver.dll` | `2cbdf0d8c0ad04e1bb83c2a5167dfb10307a8b6e002a8437e27a6383c475df3d` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/build/bin/OwnedManagedMetadataObserver/release/OwnedManagedMetadataObserver.exe` | `b46a54a7290bb7a2d161b33e0b4db4423942469f83fe08f0d1f4158c0c03e816` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/build/bin/OwnedManagedMetadataObserver/release/OwnedManagedMetadataObserver.pdb` | `a166994db5743f6a8fa49cdb346e5cc0f958b6afae737d3a6221c820a63174e4` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/build/bin/OwnedManagedMetadataObserver/release/OwnedManagedMetadataObserver.runtimeconfig.json` | `c230a317a54dd960bcbeb5f347f52e18dc665a26f7efda2159fced9a5ac7e097` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/metadata-inspection-v1-stdout.txt` | `e66621de83ccae792428125705f121d23a34bc4cb85d29138a33c5b26a420d72` |
| `resource-production-windows-20261006-v1/independent-windows-resource-regression-v1.json` | `b249ddb5448d6a992ba3731d4f268045921e9cf951ad2b236666015e561a0764` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/negative-inspection-v2-stdout.txt` | `25e742c3ae88d42de4f9afe1a16145dc6fa8c0cf4b52cf1012dbde82f8fa3624` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/negative-inspection-v2-stderr.txt` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `E:/FileCat/artifacts/release-evidence/managed-payload-followup-v1/owned-managed-temporary-files-v1.zip` | `116d3d67ae1d2eeedc507a8a3ecb694d8c38dc0b63eee5a076ccfb05209ab426` |
| `managed-payload-followup-v1/independent-all-package-managed-il-v1.json` | `f9b863578351cb51c80669ada15fbf1fb784012da1fcacf066d86f678068080c` |
| `managed-payload-followup-v1/run-all-package-managed-il-v1.py` | `20361c6865327ea4c9809f05aac7c0814cea62948cf0f2e61b71a038999ea465` |
| `managed-payload-followup-v1/owned-all-package-managed-restoration-v1.json` | `ecb93d7a036c8425dc509053645cc481000907acc4da33b468f2b5eef4d7cf8b` |
