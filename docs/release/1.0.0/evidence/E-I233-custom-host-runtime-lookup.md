# I233 — resolve automatic runtimes explicitly under a custom host

**All six additions pass on committed source `6ac0b95a41299e71f5159a1a5f8df9052aed3305`; the canonical full App suite passes 1217 cases with 25 retained explicit skips.** This finite Low–Medium correction leaves broader I16 browse/launch qualification open. The working Git/picture/context-menu subset separately passes 183 cases with one retained unavailable-share skip.

`WindowsContextMenu.HostStartInfo` and `PictureDecoder.WorkerCommand` previously selected bare `dotnet` when an unrelated custom native host loaded FileCat without a sibling FileCat apphost. Packaged apphost and true `dotnet` process routes already selected their explicit executable. The fallback now uses the existing absolute-PATH resolver, preserving those healthy routes; an unavailable runtime returns no menu command or a picture-runtime error.

Identical six-case regression inputs on the original `c17cac54b58d9860e8df70afb4821b34bd4ac5bb` source give four failures and two healthy sibling-apphost passes. The corrected working export passes all six. Controls record actual executable selection, refuse missing runtimes and relative-only PATH, and restore the exact owned apphost bytes and process-local PATH/current directory. They never execute their lookup bait.

An unchanged native observer separately exercises production command selection and child transports under owned custom hosting. The original menu factory actually starts the owned current-directory `dotnet.exe`; its C observer records medium token RID 8192 and inherited job membership. Original picture transport instead refuses with Win32 error 2: picture execution is not claimed. The corrected menu starts the absolute installed runtime and completes a native no-display probe without invoking a verb or creating a lure marker. Corrected picture transport returns the exact empty-input `FCPE` failure frame and reports low integrity and exit through the production API; this observer does not independently query its PID/token. True-dotnet and sibling native controls verify selection only.

Those native payloads remain qualified as original c17 source or the declared private working overlay. Normalized source equality to the new Git blobs establishes source identity, without relabelling a built DLL. The full `ShowAsync` original attempt retains its real menu launch and subsequent unhandled picture error. A later working reader incorrectly expected `FCEF`; its original refusal is preserved and a fresh independent reader verifies the actual `FCPE` bytes without rerunning or changing executed inputs. The first apply guard also refused a raw-CRLF/normalized-LF hash mismatch before mutation; a fresh guard verified exact source identity before exclusive writes.

The unchanged native observer is also rerun on the exact completed canonical 6ac App payload: 1230 Git source blobs, all 141 producer payload files and 140 copied product members rehash independently. The same three cases give an absolute-runtime no-display menu probe, the exact picture `FCPE` frame and unchanged healthy selections. All seven known owned PIDs are absent. One SDK log is retained and the exact E temporary tree is removed. The original observer's final `rmdir` refused four empty SDK directories after all native case predicates passed; a fresh reader seals the existing outputs and removes only those verified empty descendants, without an execution rerun or product-defect attribution.

Original [CI 37852919347 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37852919347) succeeds on exact 6ac source. The six new App cases produce 18 actual passes and six precise Windows-menu platform skips across the four lanes; all twelve picture-selection cases run, and all six Windows x64/ARM64 menu cases run. Linux/macOS each retain three exact “The Windows context-menu child is unavailable on this platform.” skips. The companion I232 controls give 240 passing logical-profile executions. All 21,588 immediate 8a case names/outcomes and previous skip texts remain unchanged; 21,852 total cases, 21 original server-digest-verified archives, all 2731 extracted members, fourteen raw TRX inventories, four toolchain receipts and 92 locked graphs independently recheck. The ARM Python fixture report retains eight packages matching the previously verified public metadata; extracted installed-package or whole-candidate identity is not inferred. The first final-reader source-scope guard omitted the `Sftp/` segment in two expected Remote paths; its refusal is preserved and a fresh reader verifies the actual declared paths without modifying or rerunning CI.

All 101 sealed working raw files, producer sources, payload manifests and fixture inputs remain. A bounded actual query finds none of 21 known owned observer/compiler/menu PIDs; picture exit remains its recorded API observation. Both native sibling copies and test renames are restored. Two owned SDK logs are retained before the exact disposable E temporary tree is removed. No global environment, network, account, policy or external source changes were made.

This does not establish ordinary packaged-app arbitrary-execution incidence, every indirect Git effect, every runtime/path identity race, native desktop menu behavior, whole worker containment or installed-candidate qualification. Physical-source HOLD, freeze/candidate checks and explicit human stable GO remain.

## Selected evidence

Private `FileCatReleaseEvidence/app-child-host-c17-v1`:

| File | SHA-256 |
|---|---|
| run-child-host-controls-v1.py | f96a600725461e6452c3420b3003ff02bb93d36a60a4ea506357565e846918b5 |
| inputs-v1/ChildHostLookupTests.cs | 7d38c021d454f8710f37bd2cf4546a63fc9964412dbd2d7cbc2557cb7611fa8d |
| fix-v1/WindowsContextMenu.cs | dafe71790ad54425f448feeb9891fd3010b574ffadfcdea7c00d05691bf45d11 |
| fix-v1/PictureDecoder.cs | 8659a90ce9b8bcc912e8e5c295f026d110f40693e3da6100d7848d3c1f7cd2bc |
| baseline-v1/command.json | 833866dd037a16f70245644d1b9175127621bab3b6c4dcddee015aacb35c219d |
| baseline-v1/results/baseline.trx | 31245d0f1b7dc276956d29d5b110c834541cbf7cabd65b36e4191c5cce6cf018 |
| working-v1/command.json | 72f8f835d3748a71fae62ddc0fa29d71614e414a141442f349d94663e7aabfde |
| working-v1/results/working.trx | bcf24bea0d2d7c2c3ab3eab4d81aa84d892eefad80ab7dc53baf91b80f24fcd4 |
| native-inputs-v2/Program.cs | 69f27720108115befff3aba5a1a3670cfdea8b7f5c06053ed4dbb06ef0044411 |
| native-inputs-v2/dotnet-lure.c | 60a9ca07863a2b13443f709cbe1eb502ab2fc610863ecd5a740a1a2ca6b62056 |
| native-original-incomplete-v1.json | a55a8fcbd4cfe6ce28d1ed61d54386a31ccd37f6a5a0b06c09e4bc98fe7846b8 |
| native-baseline-v2/native-observations.json | 8267600d662c72807118c335a865ed3a0eb796a50a75f7c2f6efa24b0e5f32f8 |
| native-working-v2/independent-native-working-v3.json | 9a80b7e450eb5e3b8ab8208e518bf6cbb52269faa91a46da64d10a881302c8d7 |
| apply-original-line-ending-refusal-v1.json | c74683df9e65fa91a89140869b0e9097eeceeb671b208d83debef7b9a843b182 |
| applied-exclusive-app-fix-v2.json | 400e33593938262ff62b23f9265aaf250d36ec6212ebe73f4a40de7323ae8be0 |
| independent-owned-restoration-v1.json | e59b77128ba2d2cbc062912acfd0d1b155062cc29118c67645d5419afa33e975 |
| independent-i233-working-evidence-v1.json | b76e9530a6d6b65eb5e2e6417734ace1c7e97dd278b0323005dcffd9bfacd7e0 |
| run-canonical-native-host-v1.py | c7ca2a4ee8654fb7a7ac242ee888e5b4fa645b52eb88e96fa0401b86dd2b4833 |
| native-committed-v1/independent-native-committed-v2.json | 1fa05d795ba56d9ff0bc63c74f2a2eab457c129690ab16cbabd729f678aefdbf |
| native-committed-v1/original-empty-directory-refusal-v1.json | 84f0ddd1d4606972c02d319f1713ebb886994038b06ce7c30de9635dc6dfa6c1 |

Private `FileCatReleaseEvidence/remote-lease232-v1`:

| File | SHA-256 |
|---|---|
| canonical-batch-v1/command.json | b87821acc20ec3fe265823cddaf85bab5e94ea13574d3284b1f1b866341010b7 |
| independent-canonical-batch-v1.json | 3b9d0ae7312444b158788b49bf4f1b0780639b20a168cc5bf6095f48e5a6f579 |

Private `FileCatReleaseEvidence/runtime-lease-host233-ci-v1`:

| File | SHA-256 |
|---|---|
| assets-attempt1-v1/independent-assets-ci.json | 71225bd1713eda7eb20d6fb6f6b104dda27f046952d947d988624c665eeb90a9 |
| independent-runtime-ci-final-v2.json | d55db9d3578eab91479fcd62a2a6901048aaf596ca63731d08a4f4941bb31b60 |
