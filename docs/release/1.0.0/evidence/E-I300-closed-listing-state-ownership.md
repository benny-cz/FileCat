# E-I300 — closed listings retire their materialized state

2026-10-10 CEST. A disposed ListingModel retained its visible-row and reverse-position arrays, marks, computed-size dictionary and remembered selection while the model stayed referenced. The pipeline’s index reservation could already report zero. An independent package-free console probe against unchanged **d24a457** Core assemblies reproduces both arrays and marks at 16, 4,096 and 100,000 synthetic rows; the largest retains 800,000 integer-array bytes alone. No UI, physical source or actual large directory is used.

The correction makes disposal idempotent and drops the listing-owned row/mark/size/pending/remembered-name and diagnostic state, leaving no reported rows or focused item. It drops a borrowed selection reference without releasing a separately owned selection lease. Pipeline/store and external-index retirement retain their existing cancellation/lease rules.

Twelve permanent controls at 16/4,096/65,536 rows cover held completed models, held streaming models, live positive listings and separately leased selections. They use actual provider/pipeline/listing/selection calls and weak reachability observations. Original source produces **six retirement failures and six live/lease passes**; the corrected source passes **all twelve**. All five captured materialized owners collect after disposal while the model remains held; live owners stay reachable and usable. Streaming cancellation cannot repopulate closed rows. All three separately leased selections remain readable after their listing closes and release their store when the lease ends. This is managed ownership evidence, not a whole-process/native/frame peak measurement.

## Full regression results and declared fixture limitation

| Producer | Passed | Failed | Explicit skips | Meaning |
|---|---|---|---|---|
| Original full Core + new controls | 3,474 | 7 | 65 | Six new ownership failures; one GnuPG fixture setup failure. |
| Corrected full Core | 3,480 | 1 | 65 | All twelve controls pass; the original GnuPG fixture limitation remains. |
| Corrected full App | 1,469 | 0 | 25 | Every 1,494 predecessor outcome/message and exact skip retained. |
| Two fixtures × original/corrected short-root GnuPG repeats | 4 total | 0 | 0 | Same unchanged no-build payloads; separate new environment observations. |

Every **3,532 other Core outcome/message multiplicity and 64 exact skips** agree. One further GnuPG skip retains both raw messages: only its owned temporary-home path differs, and an explicit path-only normalization verifies the same remaining refusal reason. Both original/corrected short-root repeats of that separately skipped case also pass; the original skips remain skips. Both full Core runs retain GnuPG’s actual `Filename too long` / `No agent running` setup failures under the long owned TEMP roots. The same one-case fixture passes on both unchanged payloads with short owned TEMP roots; no test is altered or skipped to obtain that result. The full Core runs remain failed. Future controls should use short owned TEMP roots where native tools impose socket-path limits.

The corrected build uses an independently verified export of **ff4704b58dc4d508582baad3bade2055952c5d93 /1391 canonical blobs**, with exactly two declared overlays: ListingModel and the new test file. The reader freshly verifies all 1391 actual Git objects and every export mapping; 853 files obey Git archive’s CRLF conversion policy, whose before/export bytes remain distinct. The locked restore-library graphs agree. The original before-state working-file hash is retained, but that exact raw working file was not backed up before replacement; its normalized Git equality was checked at original run time. No fresh raw-byte comparison of that missing file is claimed. Three failed reader versions remain: strict archive/Git hash equality, a strict raw preimage comparison and reliance on the SDK’s zero summary skip counter. The final reader counts all actual NotExecuted rows and preserves the raw summaries; no product test is rerun or rewritten. The main working-tree fix/test match those validated bytes. The original full Core uses unchanged parent product source plus the new test overlay. The first baseline preflight compared Git LF bytes with working-tree CRLF too strictly and stopped before tests; its executed source and observed tool failure remain, followed by a new version with explicit newline normalization. The old canonical overlay target pin is a before-state receipt; its exact bytes remain in the immutable canonical ZIP and extracted preimage.

The first committed no-overlay follow-up retains 13 passes and one streaming-readiness timeout before retirement observations. [I301](E-I301-streaming-fixture-readiness.md) corrects that fixture with one test overlay and passes 14 Core/50 affected App controls while recording actual published rows. Corrected 7bdaa89 no-overlay 14 Core/50 App controls and [four Windows/Ubuntu native cases](E-I06-current-native-retirement.md) now pass; all 64 captured listing owners collect across the native cases. [Original 5363a8b CI](E-CI-listing-streaming-readiness.md) remains failed at I301’s readiness assumption. Corrected hosted and candidate follow-up remain. Earlier native combined evidence retains its unchanged d24a457 source and runtime; this correction does not retroactively qualify new artifacts. I06 remains open for other consumers, aggregate/native allocation, platform/reference/input/human/candidate scope. Physical-source HOLD and every owner/freeze/publication gate remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested records retain exact source/build/restore and original raw results.

| File | SHA256 |
|---|---|
| `i06-listing-dispose-20261010-v1/independent-listing-dispose-final-v1.json` | `a01df03243b48b7b3b5c885418e6677b380725778900e4ce40c25233fe276778` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/seal-listing.py` | `afafc44dd340d950bab64b0698a74f30b5696f76d90efc7c65d59febdafd3a2a` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/seal-listing-v2.py` | `7e1f7d79973566e2a2925db24c68e443a0304690d4cb8584ecea5a66f51aeb1a` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/seal-listing-v3.py` | `7b8503620f53a4ab741c259a30fa01a29673825439321c4fcb07fbae227c905d` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/seal-listing-v4.py` | `dc9faf2a34d04eca339fd74657e27cb202383a39b3a9f6465c4e2297aea48367` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/Program.cs` | `2eb4a1f6471f975b4d1ae6bee664041f38054c535586cebd803cf41bb13bb26a` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/Probe.csproj` | `4f2e2275be63225b7ca777536dfd0af381efb1fe8baa0833e35e32c1d9f8237b` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/run-original.py` | `78c175db69dbc7b71a5d6828db376618334cc144f9d7977f166a694dab363aa0` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/run-validation.py` | `0ca03c11f4c6202167cd8418ab2da4c6c072df1fc221960c0acea1afec490814` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/run-validation-v2.py` | `b355231d95ddda7d0c3eb8db6977519c28951d9fffedda5aa3582cf7c6047eb8` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/prepare-fixed.py` | `90ddf3c835c528c1f8289609583103ccebc546dfd0b3e4dd27a3d124279ca107` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/run-fixed-export.py` | `e3f82d040d45e27964bb2a6ea9a13d7008a61e0adcfe869bae8e215710fcb686` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/run-short-gpg.py` | `326f7fe579eac0df1fccd1770b500a7142842d878d383568cbdef7b807ac2069` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/run-short-gpg-v2.py` | `ab7b3411bea6bbcdfe8dbf13d9467b73d321dea68ecc8b5bd0d7d5cb4a20bef9` |
| `i06-listing-dispose-20261010-v1/original-listing-dispose-v1.json` | `e7afae8ffb0116d6d5160c3f0ef826f1b7f0dbc41cb18c6893acaf1cc5d8eab6` |
| `i06-listing-dispose-20261010-v1/fixed-inputs-v1.json` | `79e8fc239408a69a2ee46450f08601819ee66b0a461a2aabda01fe61955e3143` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/fixed-canonical.zip` | `8f0b3e9e20084464a631bbab45d49f4b7ce51c511b08ac09c86c2e601459643f` |
| `i06-listing-dispose-20261010-v1/canonical-listing-preimage-v4.cs` | `e8a940005112536b48ff52526b52d87b363bd9d742b43cd582c014f654cf1914` |
| `i06-listing-dispose-20261010-v1/canonical-git-blobs-v4.zip` | `8dd4ff5d201cddb1c8030cb5722a317317704be7440137f2d79be1f57eb767f8` |
| `i06-listing-dispose-20261010-v1/validation-original-v2/validation-v1.json` | `12b6bd77ee58395a4fd8c48b4ddfa0d3b8ef794fba34bbb0bd17d440f5ecbad7` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/validation-v1.json` | `c6ca009dad57a22abd87c18323c457995c02d2220b12f2b7c851b170fceb6a92` |
| `i06-listing-dispose-20261010-v1/validation-original-v2/core/results.trx` | `e8c7eab4d3e393ac02b6645d701862117bbcd6e830a25d10382427e10d8b8e4d` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/core/results.trx` | `0a322c3254987c00070635c0c8ed7300a0247cf60fcf9e981b0133f4403aeb2e` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/app/results.trx` | `4e4ece7aad37e507d20de4ffbeae4bc1f2513156d2446a09dd7f406964a4d081` |
| `i06-linux-native-20261010-v1/preflight-final-v1.json` | `757db1221393d44bddc06d6f49dd2d30c92fe20bf8caf6af2e3806ea90c0f8d6` |
| `i06-linux-native-20261010-v1/preflight-final-v2.json` | `77b9c5453d6656db220a5ac63426d03bafa402838de06826d91b96569911bef3` |
| `i06-linux-native-20261010-v1/preflight-v2.txt` | `318c3b14833498204fbd6f8f079dd1c8d09512360646f09eeb4e4b1260b1398f` |
| `i06-listing-dispose-20261010-v1/short-gpg-original-v1/command.json` | `79e67ceaee4f39bd6055d7022225a3561a4125de1c22a420ddf3cbcee2537c38` |
| `i06-listing-dispose-20261010-v1/short-gpg-original-v1/results.trx` | `773c1f11edae7d380eff492548375800bb8720ee5a4b124a80a27080ebf4eea6` |
| `i06-listing-dispose-20261010-v1/short-gpg-fixed-v1/command.json` | `60f3e18d0eabedca600dae34e52fcbd3eacb46ed59494e8a1c7ca63331ae23cc` |
| `i06-listing-dispose-20261010-v1/short-gpg-fixed-v1/results.trx` | `f2f56b42a960d2ac2d86cd6e0e004b634854df17dab3c195435ccc7ec00a3a37` |
| `i06-listing-dispose-20261010-v1/short-gpg2-original-v1/command.json` | `1e2cecd6a849477f3e87b882cbc435f020c51ffbdcc096bd398cc7265952e40a` |
| `i06-listing-dispose-20261010-v1/short-gpg2-original-v1/results.trx` | `90a243242944f17c64ad521e4b1e7873787e08078eb6d6cbf1fe3edf1a32a331` |
| `i06-listing-dispose-20261010-v1/short-gpg2-fixed-v1/command.json` | `637e32d1dcf2406ba31821097e1888d94df9b0bcac23a7a563d9d7bd6c20ab16` |
| `i06-listing-dispose-20261010-v1/short-gpg2-fixed-v1/results.trx` | `497d0d1d03925b6a70737ccc4d0aa8607fca4ed760b187aa1db6f7f4efd76302` |
| `i06-listing-dispose-20261010-v1/validation-original-v2/core-command.json` | `97da6150eeb0f85ae7b4c53f8b5ee1f1dfa2aa4cb67f168ef88185f0a397e878` |
| `i06-listing-dispose-20261010-v1/validation-original-v2/core-stdout.txt` | `656c6730e32971c99afa7e04d5a83971602c3a45448602d843e3c10ae63e4245` |
| `i06-listing-dispose-20261010-v1/validation-original-v2/core-stderr.txt` | `3ee6b1f069bdecdde42ecf4f278c33fd92ccb5fae34df7d1947cefd6cb76f863` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/core-command.json` | `0e34e4d22b5d887777bce1ec9bf53c221a054222446f99cd36ccecb3a32e6fdf` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/core-stdout.txt` | `22afe5317a400372584444d4bc54df8a1ea0fde8f9e4ef2956a25b9b18e44cdd` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/core-stderr.txt` | `08a04b1e03d308e116c4d7a50452b35235c56d1b4d80a604d3c3dd9b993f84dc` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/app-command.json` | `b46f0330cbe4b12dd118788b992415bbe2b1f84dab385073e50a206f68e67a2f` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/app-stdout.txt` | `c7785fa3bb810bc4ae1bd9a5384470c5e3345d5e292676f6e405e89c5a49fe24` |
| `i06-listing-dispose-20261010-v1/validation-fixed-v3/app-stderr.txt` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/fixed-export/src/FileCat.Core/Listing/ListingModel.cs` | `2123a6d5266abbf9fe6fd7126ab4b26f6110b4cd6a7914d40d8a0b9c8f8338b4` |
| `E:/FileCat/artifacts/release-evidence/i06-listing-dispose-20261010-v1/fixed-export/tests/FileCat.Core.Tests/ListingDisposeOwnershipTests.cs` | `348d6b6986bdfb1fda19be22da63a87c422c28d10c2ec1e18040b96abde8035f` |

Ubuntu preparation for the next I06 batch starts the authorized guest headlessly and confirms 26.04.1, benny’s active desktop/X sockets and background VMware Tools at 192.168.58.129. Default PATH has no dotnet; private runtime availability is not yet established. The original inline preflight exits one without an attributed cause; a copied-script version succeeds. No console, host UI, package/account/firewall or persistent policy is changed. The guest remains running for the queued native work.
