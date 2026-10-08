# I227 — local links stay outside implicit upload content scope

Producer `9f643d9414641304ec7effd940a1827ffc1f0e6c` passes **256 addition names / 4397 canonical expanded checks / 198 explicit skips**, plus full App **1191/25**. Original CI **37816109750 attempt 1** passes all four required lanes: **768 addition passes / 256 explicit Windows no-sshd skips**. Linux and macOS execute **256 actual owned loopback OpenSSH cases**; Windows executes the controlled-server cases and explicitly lacks the native server fixture. This is preliminary component and hosted-platform evidence.

## Defect and correction

SFTP uploads had a separate local-path branch that could open a selected file link as ordinary content and count or enumerate a link target. Captured flags did not consistently guard that branch. This violates the product plan's rule that links are never silently followed. The unchanged-runtime corrected fixture produces 96 failed assertions, including 24 unrequested publications of empty link-target content and 12 selected links removed by moves. All of those publications are empty-target cases and span both Native and ReadBack verification. The larger targets are prevented from publication by other checks; destination observations alone do not prove that their targets were never read.

Upload selection and totals now exclude captured or currently observed local links before the local-path branch. File admission checks the current metadata again before opening content. Ordinary files still upload, and trees keep their file/directory links out of content scope. Moves retain incomplete source roots. Existing provider and archive link policies remain. This establishes those admission boundaries, not an atomic open or protection against every ancestor/reparse alias.

## Controls and actual-server scope

The 256 additions cross zero/65,537-byte targets, Copy/Move, new/replacement destinations, Native/ReadBack and controlled/native servers. Eight cases cover an ordinary file; selected or unflagged file links; selected or unflagged directory links; a dangling selected link; a file replaced by a native link at metadata admission; and a tree containing ordinary content plus file/directory links.

Fixtures create real native links inside owned temporary directories. The swap keeps the original source in an owned saved file. Native Linux/Mac cases generate their own client/host keys, start a user-mode loopback OpenSSH server, accept only its exact generated host fingerprint and read its actual destination namespace directly from disk after the job. The existing fixture closes its own server and removes its owned directory. No persistent account, system service or machine setting is altered.

Raw observations and independent readers regenerate source/target/saved/prior/destination hashes and expected pattern bytes; actual and expected file/directory namespaces; link/source presence; root completion; state; decisions/issues; copied/verified counters and totals. All targets remain unchanged. Incomplete tree moves retain their links; ordinary content reaches the expected destination. A link root never enters implicit content scope or completes successfully. The selected namespace and progress are checked before fixture cleanup.

This is the current machine's ordinary-user server against its own files. It does not qualify another server implementation or machine, separate account boundaries, server drop/reconnect, arbitrary permissions, broad namespace identity or unobserved path races. Windows's 128 native-server cases per lane are explicit skips, not passes.

## Retained failures and corrections

The first private fixture supplies the wrong owned fake-server password. Its 128 failures stop at authentication before source/root/content processing, alongside 128 explicit native-server skips; those results do not qualify a product defect. A fresh fixture uses the existing fake server's expected password. The corrected baseline has 32 passes/96 failures/128 native-server skips. The private candidate has 128 addition passes/128 skips and all 1375 prior targeted passes. Broader maintained Remote validation has 1568 passes/135 skips, including seven prior skips.

The first reader refuses an anticipated publication/deletion count; the actual raw counts are 24 and 12. Its successor refuses a Native-only publication assumption. Raw observations show publications in both verification modes and only for empty targets. Both immutable readers/refusals and the explicitly superseded speculation remain. The final reader validates the observed dimensions without changing the runtime, fixture or raw results.

The original canonical stage loses its tool session/process before any TRX or completion receipt exists. The cause is unknown. Its 1386 source/archive/output/payload files remain hashed in a separate unqualified interruption record; no outcome or exit code is invented. A fresh exact-source canonical stage completes the full validation.

Canonical Core passes 2218/61, maintained Remote 1568/135, affected App 611/2 and full App 1191/25 at `9f643d9414641304ec7effd940a1827ffc1f0e6c` without overlays. Twenty native artifact digests and every member, fourteen complete inventories, four toolchains and 92 locked graphs verify. I218–I227 repeat with **2790 addition names / 10872 native passes / 288 explicit platform skips**; 256 of those skips are the new Windows no-sshd cases. All 16 current metadata preconditions and older byte/lifetime/root/progress oracles remain strict.

The [I226 record](E-I226-resumed-source-lifetime.md), [I225 record](E-I225-transfer-failure-and-link-scope.md) and [I224 record](E-I224-upload-tree-evidence.md) preserve their own exact qualifications, original capacity failures, historical metadata gaps, timeouts and unavailable outputs. Later results do not replace or qualify missing historical evidence.

## Remaining scope and invalidation

I06 and V02/V07/V08/V12/V23 remain open for wider provider/permission/admission/server/account/resource paths, opaque/ancestor aliases and atomic handles, same-size/reverted changes, check-to-delete intervals, blocking I/O, reference hardware, native desktop/human workflows and candidate artifacts. Component and owned-server checks do not replace physical-source or real consent/handle qualification.

Affected rebuilt artifacts require new identities. No VM/Mac setup, physical source, persistent Git/SSH configuration, freeze, candidate or stable publication changed. The physical HOLD and required explicit human GO remain.

## Selected provenance

Private `FileCatReleaseEvidence/upload-local-link227-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | 695217a0a1016bed5e227a8fc698952a90870bfc8be51206ae835ab025c1df34 |
| baseline-v1/source.zip | c75b8fdd1c8040dd934d69a8d26cd0a7d07a1d27e316a572a6a1d44b25299c56 |
| baseline-v1/remote-stderr.txt | 850b3e3d3d0565eeeab016b5b95974ca158cfa1a161975b3674cd5c3b944a0c3 |
| baseline-v1/remote-stdout.txt | 0c7b4f40076bd15aa22be167f846b0e1b05dc7d50f7b0b6e28ee3bd338182d22 |
| baseline-v1/results/remote.trx | eeafd77beb6ebeb212d796778d03370a1fc10e6acf0e95f5c465ae6bfa08824a |
| baseline-v2/command.json | 6a5b84f8569cecc7236003ee93cf87b85a97d04db2f5b71497e07d57a409880d |
| baseline-v2/source.zip | c1af03010d15d75c386a481048044645ba8c6dd7839f374d1f4b6bdd1e68237d |
| baseline-v2/remote-stderr.txt | 7b4ba88fb39b2cef05db971a1141c9b23ff84198d434f19e471c63cd6d14c6ce |
| baseline-v2/remote-stdout.txt | 8dfe8fc99447792d524c4120d22199efe9bf58d1fa05e665c7005840b8b3bbe8 |
| baseline-v2/results/remote.trx | d615cd14fff602d60c0fa4a4fac6d831ae7f8ace744449f5c8dc091c694b6409 |
| working-v3/command.json | 1c4ac024f23a5dafef8135db40c96ae7b02b78fa1a42ec39818365f6388cea16 |
| working-v3/source.zip | 8aa778ec5ce4e3c229cce77fc8dd58be5276f80bcdc89123d42bbc9f6f48ce99 |
| working-v3/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/remote-stdout.txt | 729abd8ea60268f054dfdaef14a80b8ebd0b838658241849cecef8d4263f5a37 |
| working-v3/results/remote.trx | ebd9a867bc09abfd9d25901307fc56547136e0c4e71f87c73b978c7b061b2193 |
| ../UploadLocalLinkEvidenceTests-preparation-v1.cs | 4ef19d7366a6cb861daa039b2d56523eff1fd909f6d03196c8cfa70b98c54fb2 |
| ../prepare-local-link227-v1.py | 9af7ed769ffb505de697640064a6b6d3ddf43dc5cbe61779ac767660892cf2e3 |
| preparation-v1.json | 55a89b4cc9ef9f29913bf8e9c6c9ec5dc7e9a0e4501b945c2b357a874cc4aeee |
| SftpJobs-before-v1.cs | 19b19af09a62005466149395a534ab25396ece17d7f01865a7aebcbd0dd81148 |
| SftpJobs-candidate-v1.cs | 032f5f5162a1cb6a6b6e49cbe1809c0b5fa0e9720ce1be0d90b49f3a9c0547e1 |
| UploadLocalLinkEvidenceTests-v1.cs | 4ef19d7366a6cb861daa039b2d56523eff1fd909f6d03196c8cfa70b98c54fb2 |
| UploadLocalLinkEvidenceTests-v2.cs | c49b8f4c52fd4f6105058ce72c54cd26393a7389eb277fee699da2a04d2f336f |
| run-local-link-v1.py | 584926e4903db012480aa4ba8b0ab9f939c14c724abee56bc24fae248ae6cd4e |
| run-local-link-v2.py | 1de869b1ad7c0bb63725bd5cb1519d1f44f8cddf286d3445c3f938e1dd716a29 |
| correct-owned-auth-fixture-v1.py | 53212707a274ef8140d693d4fa94debad2c827b74b75af499a6dba010c534aa5 |
| owned-auth-fixture-refusal-v1.json | d6ff8340d869ad0162925fbd831c27841b692706a49eb60db52e4239d1a92d41 |
| seal-local-link-working-v1.py | 8f951cb381e2671e90bb58d6487912607020faa5ad4e1d35dce0ebbba54369e7 |
| seal-local-link-working-v2.py | b7fa37218ce8b9717630e588a79a5c440f99047d05cf6bf13e368b60da9bdd5a |
| correct-working-reader-v1.py | ab54527cb143735c9043684cd1e69fcf5440bc4151b8e91f2e6f19ab172ba037 |
| working-reader-refusal-v1.json | 132a1c1c00b936c7f501d0dd1d9a80e4a05416aad0f805ae6a3d6573a26b0d2d |
| seal-local-link-working-v3.py | 1e0284cddb4a6c6abf2ad587ac44d7eab2aefe24d3a4e25d44e4de4d637d341d |
| correct-working-reader-v2.py | 647e20cd873f2235536d28ab8771f58de5d6878877a911ce933e88d3f3710132 |
| working-reader-refusal-v2.json | 6031099742fa35c7e2a8f96183d989d8a8ea207112bbc5d4b64ec00dd18e5126 |
| working-v4/command.json | cdddf9d8f9f2c1e243272d5aadbde73f1b1f4a4341c49d9e57a4da4b48dd1f2a |
| working-v4/source.zip | b561c723baf7210d4ad2ea07327d8a300d3e14e82a17a0325cb0848e93b9813e |
| working-v4/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/remote-stdout.txt | c5155339624bc4b5c4ad2ff3a542d9c88489aa3efa1b59b1147e2727ab39d835 |
| working-v4/results/remote.trx | 73cf2cf6a8bbecf991c05010c5a5850efc789a7869bb56f6b77fef1e4f2bad11 |
| prepare-broad-runner-v1.py | a532911eb580b4618b899cbc465d0b45e5f144c6e1f5603f364d98ffa620b0cd |
| run-local-link-broad-v1.py | 4fdcb5e07251c525bc8a08abd47fa4edd73625208ab6753642f2f4abbf240151 |
| independent-local-link-working-v3.json | dfbe9cdd6f75827eb4fa2ac3824c7d638cab253b5c51933109895d8061d00a40 |
| seal-local-link-broad-v1.py | b607153bd332ae6ad5d2199a2a9bfc76aef03fa6141c76c63fe486513c13a8ba |
| clean-v6/command.json | a6baadf4c61163915ed6dcf92c3ceb746d4e4d9dc631b98650d41a1422cf03d3 |
| clean-v6/source.zip | 0389e8c7bed4eb71a1ff74aa411b25e7488a0e600d67d2540c738f253bd102d1 |
| clean-v6/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/app-full-stdout.txt | 651a69d8b970c4788ccc3c07e08bcc9143067484bd3107538b2cf4b902a1f6a4 |
| clean-v6/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/app-stdout.txt | e999b040bcfc1ba28369e863dbd203c235f3d2841d965c37b280bb2190608a91 |
| clean-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/core-stdout.txt | a5ec975fc7e5db5d2ab39ab455f1de2989bc336b1bcc48f1b4aa3a3652f29571 |
| clean-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/remote-stdout.txt | 5ef98ad6f85653da4754659f21ea7d888f2fbe85dc70d7f7119ba6dbab0efc85 |
| clean-v6/results/app-full.trx | 286b586ec075c0d399422b9831e3bd57653f5f57ee9d47729257a0857e438383 |
| clean-v6/results/app.trx | 7044af4935587ed920ac6d00fe0bcdae76305fec6f340b8101cf27b2cab4fa42 |
| clean-v6/results/core.trx | 3cb832a68a99f6b653f0679a778c98405b5e88375faf23de3c974ea14860a907 |
| clean-v6/results/remote.trx | 2354f0748b546320ffc1899397a7255ff6e47840f8b1070c0b8dfb87b7051e63 |
| prepare-canonical-v1.py | fb27d9d1b901acc9a47ccb7b2d3e05dc5cae6733696a274022a968cff3182eef |
| run-local-link-canonical-v1.py | 2adf17390a0725991df8b0df9fde896456e001d7cf6701d01817498f181e5cf3 |
| independent-local-link-broad-v1.json | 520841328202dbb6ca848d81c99859329672aba34f3ebd0181cdc6c51c186035 |
| retain-canonical-interruption-v1.py | e710a2640bdbde3601aabd1b9a800e165b9a4a85858a2f7fcf28e0ebc53972ab |
| canonical-interruption-v1.json | 45ca8f90da06fc4fe374792c8302f36982b8b5a356dea87c210eef4b4d91ba0e |
| seal-local-link-clean-v1.py | 1a35778f9893268cb6dbab49f98e1597727c2f8045c9dd7a23ec85b546944975 |
| independent-local-link-clean-v1.json | 8ce3e986909c67cfcb17d83057e0a84770b6202855659f685b4a1667780d5c1c |
| prepare-native-readers-v1.py | 5c54bf076c34d275b224dd7c5d4133334dd7ee990984bc5a5219f6ae9383fffc |
| native-parent-reader-v1.py | c01bffa6ab0b0318ca247e46ca752e142de0fb158b9467f79f03996737ecbb24 |
| retained-transfer-native-reader-v1.py | ec75be4fbeeed6fcf695f451972383b7b4f045fdab11673c3062c055f18bbf5c |
| retained-copy-native-reader-v1.py | ce869907d745ccce963122cdab550ee3a6c6433ed6d15bbf2aa5927c64d657d5 |
| retained-upload-native-reader-v1.py | e9928f11a728d6249f42caf61ca4a0fc1203fce32987ca9b6a54e3ae80f3d833 |
| retained-tree-native-reader-v1.py | 95b97836ee3531830946b007cad0cd7090eeb584266808a21b5fa373f8c60901 |
| retained-failure-native-reader-v1.py | 89917db7a333bc506c18618c4e3928383614c36ccfe28656f01352576547f844 |
| retained-resume-native-reader-v1.py | 81ee26e1fb6eb05fb9f9cd9e5645b8f700dd59ecb333dabf7c80a4b7828260ff |
| seal-local-link-ci-v1.py | 136a7041ae815ab54b60df97545fd030920b229344676d2e1a30d09e74f405fd |
| final-native-source-v1.json | faa2ba2f0c51f27c05850f395e8d5faaf2c9fe17000135959139a02d5b9ae530 |
| final-source-v1.json | 2e620cea27c442e36fc452d1830816ff99fbaca23608fadc0ba317b4ac15e048 |
| install-main-candidate-v1.py | 008234b50829feba8f014ca60bcc15558d86fa556405bae879f7a7a44c04fd2c |
| main-install-v1.json | f08494a045064cf023dbc9132cf1e117cae8330c4bae5c4ac19cc56468980075 |
| seal-main-checkpoint-v1.py | e6b4157959280e446c02bb2abe1a728e9cfe16d7f2242e3fadd85b8e5772c461 |
| prepare-tracking-readers-v1.py | 5b2a632388034d378cc2b8c4d10d99286ea7716f0809ef445fbc49ebba63e98a |
| document-baseline-v1.json | 70e0bf0e76266bfd338538fd707b6303bb492124afa15bf3d55d13c34d279fc5 |
| document-transitions-v1.json | aeade364a98eba5a1f7219a3f6b7b9c8d11513fc68d19a660db197aa4afe6f5f |
| update-local-link-documents-v1.py | a72a6fb4269c90d9fb280c33444890352bbc600895e40c2ccfcd3864b08bac0e |

Private `FileCatReleaseEvidence/ci-37816109750-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | e600b852936ad346b344c7988888e41760b5e89d9966b385fb8c89581c37f2a1 |
| independent-restore-ci-v1.json | 024d96755acfa0a9d113bf7228825de14b7c8eddb6064019b3195010d119df51 |
| independent-local-link-ci-v1.json | d920876a8eb3eae6c7c694e7b2d52834a968cd8dfb157b80919542a2e3072f6d |
| run-native-stdout | 669a4b1078128c9a6b7333c8d1b74c7fc5733c10558ffccad1fbc75f76abbbe0 |
| jobs-native-stdout | 79fd2dde67237668a97e3e0d8d92882c181627a207b342da7ab31bbc77bdd2c8 |
| artifacts-stdout | 37b8dcf6a540e7d180efbab9d4607b107a43267c489391e69daa909c289af5a5 |
| complete-run-log-archive-stdout | 3f7c3c0f9bc0b0f01161ee51971fda00f2874d76b6181992fe1aa762e4e777af |
