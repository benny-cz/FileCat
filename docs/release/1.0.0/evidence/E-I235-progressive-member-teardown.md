# E-I235 — progressive archive-member teardown and damage diagnosis

Runtime/test source `05832bf6e90ee2877ec728bbf27b91b2204b4533` fixes three reproduced `ProgressiveContent` cleanup defects. A throwing source close left its retired source reference available for reuse; disposal skipped the real spool handle and archive lease callback; and a secondary close exception hid the actual short-member, checksum, expansion or spool-limit diagnosis. Severity is Medium for availability, resource lifetime and damage diagnosis. The operation already failed; these controls do not establish accepted corrupt output or native archive-reader fault incidence.

## Same-input controls and correction

The original production baseline is exact `6ac0b95a41299e71f5159a1a5f8df9052aed3305`. Seventy-two cases use actual progressive content with an owned file stream that closes its real handle before a controlled I/O, access-denied or disposed exception. They cover 24 disposal cases, twelve abandon/reopen cases, eight healthy end controls and 28 damage/limit controls across in-place, spooled and forward policies. Actual source closes, captured product spool handles, callback counts and first exception objects are observed before emergency fixture cleanup.

Both original v1 and strengthened v2 input versions retain **54 failures/eighteen healthy passes before the fix and 72 passes afterward**, with the same 72 display names and outcomes. V2 additionally captures exact first/reopened bytes, remaining-byte hashes and unchanged owned-input hashes; the independent reader checks the deterministic 262,208-byte input. All 288 raw observations remain. The existing archive regression selection passes 129 cases, including these 72 and 57 earlier cases, on the same private fixed payload without rebuilding.

The correction retires the source reference before close. Disposal attempts source close, spool close and lease callback once each, preserving its first exception object and stack after cleanup. End/damage validation retains its primary diagnostic while recording secondary close failures. Existing archive limits, growth policy and production timeouts are unchanged. The private controlled-overlay builds retain their actual producer; only the clean committed export below qualifies the committed binary.

## Committed local and hosted qualification

A fresh export verifies every 1,234 original Git blob, its source archive, all source bytes and the actual Core/Remote payloads. Full Core passes **2,290 cases with 61 explicit skips**; full Remote passes **1,772 with 156 skips**. All 176 new controls independently retain their actual byte/resource/error observations. All 1,824 preceding local Remote names/outcomes/exact skip texts remain. Earlier full local App 1,217/25 remains qualified at `6ac0b95`; it is not relabelled as this source.

Original CI **37857868887 attempt 1** passes all four required lanes. The 72 archive cases execute 288 passes and the 104 remote-opening cases execute 416 passes. All 21,852 immediate hosted predecessor names/outcomes/exact skip texts remain; the current fourteen raw TRX inventories contain 22,556 outcomes. The independent reader verifies 21 original server-digest-pinned archives/every 2731 member, four toolchains, 92 locked graphs and 25 original native API receipt sets. Earlier finite transfer/notice/native byte-oracles retain their own source qualifications; neither aggregate counts nor this correction qualifies the wider campaign or a candidate.

## Retained GPG fixture failure and bounded restoration

The first unchanged committed full-Core run has **2,289 passes, one failure and 61 skips**. The only failure is the existing native GPG fixture: the installed 2.2.28 agent reports an overlong owned home path. A separate unchanged single-case run still fails at an 88-character home. Fourteen native paired automatic/prestarted controls then demonstrate eight successes at the tested 29/60/70/80-character lengths and six failures at 88/96/154. Successful automatic controls also log the job breakaway flag; that message alone does not prove a job restriction. This qualifies this installed native tool/context, not a universal path maximum or a new FileCat defect.

The unchanged single case passes at a 79-character home. Full Core then passes on the exact same committed DLL with only that child's TEMP/TMP/SYSTEMTEMP shortened. All 2,351 original test IDs/display names and 61 exact skip reasons remain; only the GPG outcome changes from failed to passed. Two pairs of truncated Core display names are retained separately by their actual stable test IDs. The initial independent reader's duplicate-name refusal remains, with its fresh correction. Hosted-versus-local path display names and GPG availability are not claimed as identical local predecessor matches.

Twenty-four owned SDK/temp files are archived and rehashed, then removed through checked native literal paths; all five exact owned roots are gone and ten recorded test PIDs are absent. The first removal script refused its unavailable hash cmdlet before deleting any file; that raw refusal and every original file were preserved, then a fresh script used disposed .NET hashing streams. No process was killed or machine/account/VM/Mac/USB setting changed. The native GPG controls separately restore their synthetic keyrings/roots and observe 58 recorded PIDs absent. Earlier nine compiler-cache files, the separate remote-opening three locked files and earlier aborted C-temp namespaces retain their original qualifications.

## Selected evidence

Paths are relative to the actual private `FileCatReleaseEvidence/progressive-close235-v1` root. Nested source, payload, raw observation, command and restoration inventories preserve original evidence.

| File | SHA-256 |
|---|---|
| ProgressiveContentCloseFailureTests-v2.cs | 964f7bdc73b0a55fb45c314ec6a4de128a2806e2db8d05220c1f65414ff63cae |
| ProgressiveContent-fixed-v1.cs | aac2cafda02a5c5e80c7c57e094e7a0ce0acad0036a2997ebdfd9607ee8b7400 |
| independent-progressive-close-v1.json | 161164ee30afe16effad81b8be81a610d9b1d841459ddfa225445207fba78984 |
| baseline-v1/command.json | df16ad80abc4c2bba55a841d88c02198c143132cb4ea04239f96fdf76a9daae3 |
| baseline-v1/results/progressive.trx | 89adc93613eed430585e4415799c6abe64bd40adfaf867030909b39870378ff2 |
| baseline-v1/observations.json | 8036f193620de36efe7e813cbbce4a8d99d82f2db710e06c99f03bb1c45c4df9 |
| fixed-v1/command.json | 6a94b53c427da68323c5d447551ad122b606c79d7a85a9554de01e52155f916e |
| fixed-v1/results/progressive.trx | 68b2282e4165e03929f0fe658780a45e15601b300560109fcd52178e43837f19 |
| fixed-v1/observations.json | daad93a4dcb582c1f024e3b5b3c60b0a253489570e1c0b42e778b799fb2ccb42 |
| baseline-v2/command.json | e5571482bf34fbd6472f710db219b071c864316d65c5c2e0fca3a5a483784bc6 |
| baseline-v2/results/progressive.trx | 4698cf3c1f8bc9da58601788e3d85d66fa8d3f99491c4ef59132f392cda5a6dd |
| baseline-v2/observations.json | 210d89f86047e60d8712bf0579d503387e5bf7926509d7a35ba986196d260518 |
| fixed-v2/command.json | 99ab9f42431761a1eeff1ceb36d27e375f246124a70ef96240dc7b8e22318210 |
| fixed-v2/results/progressive.trx | 92b31f0d3254e8b325308152d24774d199cd8bdcdad18e99f71c20e388e93940 |
| fixed-v2/observations.json | cf3d1e7d99942be9967acd1e9fe4d2e4d2c8afd6f6737c4f9fa4c7168359af23 |
| affected-existing-v1/command.json | 788de10ed73a05be2c1d66478037ef6b91ae59e14f10868b10416eff5e7c6860 |
| affected-existing-v1/results/archive-existing.trx | 62a046c012683b4ec9328de4f2f819a1ad3db8e75c8f201c5231f75896484b90 |
| repository-progressive-mutation-v1.json | d33c5dda905d97a949b2d05fb9aea62a3e9894b3583deebaeeb3ee0fa91b77ba |
| combined-runtime-main-push-v1.json | 82823a367018c5dd80df46ffb1e913644f8df88be49b9e4da7b069df6e77b548 |
| independent-canonical-close-batch-v2.json | 646f770b41ab79bcf9563b42129e6a0ee6a228f01dd2b21d09451f15bdfb56d0 |
| canonical-v1/core-command.json | 9b8dc38ef274a810074e58fbb5991fcf2dab553df81adfa5a664a02b50a948be |
| canonical-v1/results/core.trx | feabc36e831ea301ec5e98fcbfe19d751b1be9c0e3fe830b978cd69ad5118eae |
| canonical-v1/dotnet-info.txt | 7a102c8488fae3f63b997de923545819896c46e22a9d9df7db67cf2ce5dda637 |
| canonical-core-short-v2/command.json | 36318ac21b6331e7463ac85468c005646625d5de18c3df89796b126e787203f6 |
| canonical-core-short-v2/results/core.trx | b198b1a435caeb4de558342a9b04b8b6780a10a44a9b92e0cc3399ae44fd3d74 |
| canonical-remote-followup-v1/command.json | e72417c5e978d336c5807025ab7b82a6a7c46f87ec5ccff63bbded10ced107c7 |
| canonical-remote-followup-v1/results/remote.trx | 03e55487215472b4c015a91d8f917430e297f86bfc8393653759efdb6dc4b311 |
| short-temp-gpg-v1/command.json | 33d2ee391d1de8b597262ad612bca5d2672603c57d345a653cf0a5cc938d150d |
| short-temp-gpg-v1/results/gpg.trx | daf3d30d2e06306e02b36c158e7327e96514e84f9f9cb15dea89277e1578192b |
| short-temp-gpg-v2/command.json | 814456bd3bbb83a06bbf9c56a10d9ede36b1ebdcf14424eae711dfdb73d1f0b5 |
| short-temp-gpg-v2/results/gpg.trx | 7be407c96b0a0590d977c7a7a585fb01690f41cd7d826e22ad4e1728d9edcfd2 |
| ../gpg-owned-autostart235-v2/independent-native-home-controls-v1.json | 067e7a81b4759796239f96d18a0b308cbd70c8dda933a3aa4e773f33c7df3134 |
| owned-progressive-restoration-v2.json | 215edc4bf8d5e006e1d94e03f0f642133d4fd64e7f6162ba3e3b9135f985abc3 |
| owned-progressive-temp-files-v1.zip | 82463c2bed59265a09570a7181725fbdecafa16cdee457a3c4d85ca91caa43e2 |
| canonical-reader-duplicate-name-refusal-v1.json | 52da585663e150707845cbca1752c94eab7ff6fadbeaf3c505eb778c13e1b1f5 |
| owned-progressive-native-hash-refusal-v1.json | 2219b0bdeee8210a594326b5f2d2cc82d67cca88988bdb96dae1b38684c8a284 |
| ../open-archive234-ci-v1/independent-open-archive-ci-final-v2.json | f88f27540ea2d78153d077ebeb43265d56960f8f6f5ad235fd4b15a99f1ea940 |
| ../open-archive234-ci-v1/assets-attempt1-v1/independent-assets-ci.json | d26df1d3e7b4c67051465a5f659ca8cd813ab3d7535629a0d57a517f964f523d |
| ../open-archive234-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json | da55f87195dd66c62a0ab29f4346916b2dfcac82ddbc6eb91332af797dfd5dce |

Broader I06/V07/V08/V12/V23 provider/archive/native/resource/reference scope, all 24 final-candidate campaigns, physical-source HOLD and explicit human stable GO remain open.
