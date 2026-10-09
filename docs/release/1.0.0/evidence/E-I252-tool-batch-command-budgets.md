# E-I252 — Windows batch command budgets

I252 is remediated preliminarily at runtime/test source `d0d33af36d40f1d1c811fbc572e727c94c0d58d4`, direct parent documentation `9859b082bbadfce05b215269af1aa6ba14612e8d`. That parent's product bytes equal the original `225029c0d209f1fd433cc16690e514233effcf91` producer. This is a functional launch/planning correction under I06/I16/V11/V23. It does not close ordinary tool, UI, shell security, physical-source or final-candidate qualification. The [issue register](../FILECAT_1_0_RELEASE_ISSUES.md#i252) and [dashboard](../FILECAT_1_0_RELEASE_EXECUTION_REPORT.md) retain remaining scope.

## Observed defect and correction

On the exact completed 225 Windows Core payload, harmless `.cmd` and `.bat` scripts receive all 64 ASCII argument bytes in the healthy controls. With 9000 ASCII bytes, the old planner still returns one invocation. Actual `ToolLauncher.Launch`, entered directly without a preceding public `Plan` call, reports one returned invocation while no script receipt or completion marker appears. In separate direct-child controls of the same planned command, the native command processor exits 1 with the actual message “The command line is too long.” These direct controls supply the child PID/exit evidence; the production Launch API returns no child PID, so its child lifetime is not invented.

Bounded native calibration records the actual immediate parent executable and full wrapper command for owned receipt helpers. The installed command processor path has 27 characters: the implicit wrapper adds 32 characters to `CommandLineLength(script, arguments)`. Actual measurements through 8159 (8191 including the wrapper) deliver the exact full bytes; 8160 fails. A child-only `ComSpec` override selects an owned byte-equal copy of Microsoft's command processor with a 55-character path and 60-character overhead. Its measured healthy/failing boundary shifts accordingly. The copied executable's adverse missing-message-resource text is retained as actually emitted, separately from the installed processor's “too long” message. Three calibration sets retain all 42 original measurements. No global environment variable or machine policy changes.

The correction bounds Windows `.cmd`/`.bat` invocations by `8191 - actualCommandProcessor.Length - 5`, using the process's configured `ComSpec` and the existing system-directory fallback when absent. The same allowance applies to fixed arguments, empty selections, per-file refusal and batch splitting. Real executable and non-Windows plans keep the existing 32767 budget. Argument order, repeated file-token regions, focused-file substitution, resolved single-list ownership and list bytes retain their existing behavior. Primary Microsoft command-limit/CreateProcess documentation is archived in the pinned receipt; the precise actual-wrapper/path observations come from these finite native controls.

## Same-input private and committed validation

Twenty-one identical durable Windows controls change **12 failures/nine healthy passes to 21 passes**. They cover `.cmd`/`.bat`, fixed and empty-selection arguments, a single file that cannot fit, ordered batching with repeated tokens/focus/one list, a large selection through one list, the exact boundary, an unchanged real-executable allowance and native owned custom-command-processor boundary/refusal. Four custom controls explicitly skip off Windows; the other 17 execute portable planning/owned-IO behavior. Process-level `ComSpec` changes run only in a dedicated nonparallel collection and are restored in `finally`.

The paired unchanged native observer executes eight bodies against the original canonical payload and eight against a declared private Core DLL/PDB overlay. Original results give four adverse contract failures/four healthy controls; the private correction gives eight healthy controls. Short scripts preserve complete 66-byte receipts (64 ASCII bytes plus CRLF). Long fixed arguments receive a typed refusal before a child starts. The observer's historical 225 source field remains unchanged; actual loaded DLL hashes identify each payload, and the private overlay is not relabelled as a whole committed payload.

All 39 selected predecessor cases retain their exact identities/outcomes/skips on the unchanged 225 Core test DLL with only the private Core DLL/PDB overlay. The first filter actually selected 36 cases and passed them before an incorrect expected-39 reader guard refused. Its original subprocess PID/exit receipt was not saved and remains unavailable; closed native output and TRX preserve the actual 36 passes. A fresh execution runs only the missing three `ApplyCommandTests`, yielding three passes; the original 36 are not replayed. The combined reader checks all 39 prior identities. Separate initial observer-import and unavailable-xUnit-attribute attempts stopped at compilation before native/test execution; their sources/streams remain pinned.

The exact pushed d0d33af Core payload then supplies a fresh committed native repeat: **eight/eight controls pass**, using all 82 canonical payload members and the byte-equal five-file previously built observer, with no rebuild or product overlay. Two direct and two actual-Launch short controls deliver every byte; four long controls refuse before a child. The unchanged observer still says 225 in its historical source field; the explicit current 82-member producer and actual loaded Core hash establish d0d33af. Actual native proof keeps this distinction.

The full immutable LF source export checks all 1266 Git blobs and actual E payload paths. Core **2573 passed/64 skipped**, Remote **2020/156**, App **1217/25**. All 532 actual affected raw observations verify, including 16 new solid-cursor controls from I251 and these 21 batch controls. Every preceding local outcome/exact skip remains; one passing PE display argument moves with the owned artifact directory and is explicitly qualified by unchanged test-source blob plus actual old/current DLL pins. All other prior names remain exact. The hosted native Git archive retains its own actual line endings rather than being relabelled as the canonical LF export.

First original CI **37886618093 attempt 1** at d0d33af passes all four required lanes. Its 14 original TRX inventories contain **24692 records**, retaining all **24544** previous identities/outcomes/exact skip messages. New I251 controls give 64 passes. These 21 controls give **76 passes/eight exact Unix skips**, whose reason is `Windows command-processor control is unavailable on this platform.` The other portable controls establish plans/IO/bytes; the Windows custom controls additionally retain actual copied-executable hashes, known native child identities/exits and full receipt bytes. They do not turn all planning tests into native launches. Twenty-one server-digest archives, every member, four actual build/toolchain receipts, 92 locked graphs and 25 native API command sets are rehashed. Existing byte/resource/error/rename/overflow controls and original failed/cancelled run qualifications keep their exact producers; no earlier finite byte oracle is replayed merely from counters. The first semantic reader refused only its unchanged historical comparison counter (24488 to 24544); fresh v2 checks the actual 24544 to 24692 inventories and preserves the original closed reader/command/stderr. It reruns no product test or CI job.

## Restoration and remaining limits

Private fixtures archive/recheck 46 temporary files, remove 44 and leave two compiler/analyzer files individually pinned; 11 of 12 exact roots are absent and all 83 known process IDs are absent. No process is killed. The remaining files are `E:\FileCat\obj\bb252v2\VBCSCompiler\AnalyzerAssemblyLoader\63c491ae1b624df6ba29de4c37d4f4ab\1\xunit.analyzers.dll` (302344 bytes, `594a2c95c3e3b2a0e77000cb812f083bc05a4905a11fe16ac95cda1b580124e0`); `E:\FileCat\obj\bb252v2\VBCSCompiler\AnalyzerAssemblyLoader\63c491ae1b624df6ba29de4c37d4f4ab\1\xunit.analyzers.fixes.dll` (239880 bytes, `b292552003ef9ac3990e9d3b377081b7b4126f3ec22dd5f9fef19a9c61881ff5`). The final reader freshly checks all 6591 retained evidence inputs and exact archived members. It does not claim the final locked root is gone.

The committed native repeat archives/rechecks all 24 raw fixture files; they and its exact observation root are absent, while its 87-member executed payload remains pinned. Its removal script completed the removals but exited 1 at a mistyped `-Depth30` receipt formatter. The original script/closed stderr/command remain; the missing original restoration receipt is not invented. Fresh v2 performs only an archive/payload recheck and a read-only snapshot of the three recorded process IDs, all currently absent. It does not repeat native tests or deletion. The production Launch API's unavailable child PID/lifetime remains unavailable.

Current canonical restoration archives 11 files, removes 11 and retains 0 individually pinned files, according to its exact receipt. Earlier aborted namespaces, compiler locks, the 55-CRLF I246 document qualification, unavailable original inventories and prior reader refusals stay at their original producers. No VM, Mac, USB, account, network, global environment or security-policy changes occur in this work.

The actual native launches use owned ASCII scalar arguments and harmless receipt scripts. Large selected-file batches are verified through portable plans, ordered tokens and exact list bytes; no actual long-selection native launch, metacharacter/injection behavior, ordinary application/UI incidence, blocking native IO policy, whole helper security, physical-source validation, candidate or stable-release approval is claimed. Physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Small relative paths below are rooted at `C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence`. Heavy source/export/assets use their explicit `E:/FileCat/artifacts/release-evidence` paths. The pinned proofs inventory every nested executed source, payload, native stream, observation, archived fixture and unavailable/refused attempt; the table selects essential receipts without discarding those nested inventories.

| File | SHA256 |
|---|---|
| `batch-command-budget-followup-v1/primary-documents-receipt-v1.json` | `9ef9c8d39bf3ae15f3a77944f2c046922fe1eec5afe473b7704247b64a7796e9` |
| `E:/FileCat/artifacts/release-evidence/batch-command-budget-followup-v1/source-original-v2/src/FileCat.Core/Tools/ToolLauncher.cs` | `81a00d264abd7b02a2862737cb9f341f7bda39ffcc396aaa0178117cfd6e4d81` |
| `E:/FileCat/artifacts/release-evidence/batch-command-budget-followup-v1/source-fixed-v2/src/FileCat.Core/Tools/ToolLauncher.cs` | `37eb0a5fd2ff5589159b15b35ff2eef297d20803cbb51a7ba8f15fe920fdd89b` |
| `batch-command-budget-followup-v1/ToolBatchCommandBudgetTests-v2.cs` | `961cffaf5f6b9cf35eb3171bdd33e2a965d0e160081707e4e323029fec99db76` |
| `batch-command-budget-followup-v1/independent-private-batch-budget-v1.json` | `f3c1a61f40c18e4cd2170c579c92151e75eb3e9f44013acfe608cbf7e562db6c` |
| `batch-command-budget-followup-v1/same-private-budget-tests-v2.json` | `26c461637daa6fa3eeb668c42a7590e54789b3cf3f96093aefd7c50bb33c4554` |
| `E:/FileCat/artifacts/release-evidence/batch-command-budget-followup-v1/original-v2/command.json` | `499294bcd9bf8629e2bfb09c21ffde7a6b44831cc11696deeb72576499237def` |
| `E:/FileCat/artifacts/release-evidence/batch-command-budget-followup-v1/original-v2/results/budget.trx` | `f38f08f8dbe4004cee6674fd41dde82bf263ca21ba44519bb2bef7aec7b18e3d` |
| `E:/FileCat/artifacts/release-evidence/batch-command-budget-followup-v1/private-fixed-v2/command.json` | `cd2dfd2dc783603a04abc00088c01b152fa87111d034dcdd3bcdf528e636f5c1` |
| `E:/FileCat/artifacts/release-evidence/batch-command-budget-followup-v1/private-fixed-v2/results/budget.trx` | `32fc12ae07766d161f0746f5688fb3ee1e7da85ccbf113b251da40168ac664d7` |
| `batch-command-budget-followup-v1/original-native-batch-v2.json` | `c7f3f0dcd70c7344b85a65bc688ebe1883355071ced14cf2713bcc0a68f55527` |
| `batch-command-budget-followup-v1/original-calibration-batch-v3.json` | `71147aaf369d56134e2e4d8be2f356b3c8e503da13806a40b3f4a1c42fdd6a3e` |
| `batch-command-budget-followup-v1/original-wrapper-calibration-batch-v4.json` | `34bab4c54c7c741c570a0df09cfcd66cf76b3f525e8370c8a8b6918481d487bf` |
| `batch-command-budget-followup-v1/original-comspec-calibration-batch-v5.json` | `ebd5c9ab97cf28bfff83990072e6adafaf42c6e1877c3377ad0bf7bbb69241a5` |
| `batch-command-budget-followup-v1/paired-native-launch-budget-v6.json` | `807044c8d87d01d97aee498f0719543da900e297725c65e59c998b345ed0c9d3` |
| `batch-command-budget-followup-v1/Program-v4.cs` | `d9a0efc1332ee5c67519e40e8a318ff4acf91ab8badca5cc54391adefa2634b1` |
| `batch-command-budget-followup-v1/original-launch-native-v6-command.json` | `376ccf56156644182ddbcece1c8589c47aaf380987063fa08a6c8ecd9f7eeeed` |
| `batch-command-budget-followup-v1/private-fixed-launch-native-v6-command.json` | `a07ebe4e8401b56cfc1ec71e55494083a9a57f91635b5030e9cfdfd6f08c3683` |
| `batch-command-budget-followup-v1/owned-observer-import-refusal-v1.json` | `b830093a951035d1395e131ac9900c579293f9e2e37069ed1fb57c9d9b5a6192` |
| `batch-command-budget-followup-v1/xunit-skip-compile-refusal-v1.json` | `b042c87c6567f486b4b9e5347913247704bf61a83a0c28a8e7ad146397499a53` |
| `batch-command-budget-followup-v1/prior-tool-filter-count-refusal-v1.json` | `7fa1c356dd653a82a89577ef835498da3966c9503e27238f03a86ceec6f0177f` |
| `batch-command-budget-followup-v1/unchanged-39-tool-regressions-v2.json` | `7765b9878847f757e535cc0456ca700e5deead6a25b70e1ac48fd170575ab203` |
| `batch-command-budget-followup-v1/owned-batch-temp-restoration-v1.json` | `a9255b62db8e734959d706190b1d994174540f7ee961f83605c77ab7b0f1578b` |
| `batch-command-budget-followup-v1/owned-batch-archive-v1.json` | `9423e512059311931b2c1c6a10fb730a7478ba4669c01e102c218d88727e8595` |
| `batch-command-budget-followup-v1/independent-owned-batch-final-v1.json` | `eb90dff3e28e097ddcbd4934919b419c33cf08ae01b2b6d94028a17dea1b3475` |
| `batch-command-budget-followup-v1/independent-committed-native-batch-v1.json` | `b559376a729a6fc895d15fd1ef081d5127f3ad221eaed39805b401d1073681e8` |
| `batch-command-budget-followup-v1/committed-native-v1-command.json` | `ce552b322a9bed77563141009d492848413f3162c0439cb2e46d2b05cb2807a8` |
| `batch-command-budget-followup-v1/independent-committed-native-batch-final-v2.json` | `9a31edcb9fc8681189eeb543badfbeba00de5e86cede188bd153dc6552dea9db` |
| `batch-command-budget-followup-v1/owned-committed-batch-restoration-v2.json` | `fae0a00ef0d8c163434da43216867d88be111152c375ca869a2853270c386090` |
| `batch-command-budget-followup-v1/committed-restoration-v1-command.json` | `4eab271e254a49e57c59c0317abf951846706b27af742bf0fb0fd37b79a63af3` |
| `batch-command-budget-followup-v1/committed-restoration-v1-stderr.txt` | `b85b938228a56f014d16cd2c7e8c5f343d640fed260b0e562e830099531ef423` |
| `cursor-batch252-v1/independent-cursor-batch-canonical-v1.json` | `42a118d46407c49a5222cd27a46046723fc34fda1a790427b84943a43b0ad221` |
| `cursor-batch252-v1/canonical-v1/command.json` | `2f2bf7764b658f2d16f1fc57aca665babbea2ed73b0c43cde522ad7f36df2518` |
| `cursor-batch252-v1/owned-cursor-batch-canonical-restoration-v1.json` | `c41e4de6ab9c667839d07bde740f5098de1e2b8f4763463a8da614d42d65b4f3` |
| `cursor-batch252-v1/cursor-batch-runtime-main-push-v1.json` | `99d999fdf78eb2424ad453abe63fb3e54faa7e78dd21bc905b4d354b570e0c7c` |
| `cursor-batch252-ci-v1/independent-cursor-batch-ci-final-v2.json` | `fa2728ab33f35f5aaa9d2895fc95d70cd510f5fde9023fbb226a9112d94ede19` |
| `E:/FileCat/artifacts/release-evidence/cursor-batch252-ci-v1/assets-attempt1-v1/independent-assets-ci.json` | `0757836b91965f907331472da5a44dfcfaca3908d1f21ef9dcca0440aa6f0153` |
| `E:/FileCat/artifacts/release-evidence/cursor-batch252-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json` | `f73850b8a9507a0fdefa6ece3e294adf3ea9b9f5d7c846cfd863ec9a47da60f2` |
| `cursor-batch252-ci-v1/preparation-v1.json` | `9e0c1b9c06c3fdce080baebbf72ed9d91570fb33ebeb6f0195ae49360e204b1c` |
| `cursor-batch252-ci-v1/seal-cursor-batch-ci-v2.py` | `ded359968456001d28814b2092f37d38a80bbc2d321e8ad64381ba3e16c5ee8d` |
| `disc-list250-ci-v2/independent-disc-list-ci-final-v1.json` | `45bfc51a3d2f234cb210f0f97760219844bd13b129f571d5c89f986eef710c88` |
