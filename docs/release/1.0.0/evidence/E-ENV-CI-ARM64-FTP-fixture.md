# E-ENV-CI-ARM — restore actual ARM64 FTP/TLS coverage

Original CI `37840738731`, attempt 1, succeeds on exact source `67f648ac17b64eddcb0b197e0949dd96d4abe0aa`. All four required lanes execute all 34 [I229 FTP controls](E-I229-ftp-read-completion.md) and six [I230 Git controls](E-I230-git-optional-launch.md): 160 actual passes, zero skips for those forty names. This is a CI fixture correction, not a new product issue or candidate qualification.

## Failure and repair

The original `cae0f04` ARM run explicitly skipped the absent owned FTP/TLS fixture. Adding the other lanes' floating pip command in `fd6b44d` exposed an actual install failure in run `37838418780`, attempt 1: pip tried to build cryptography from source and failed to locate OpenSSL development files. Three other required lanes passed. The quiet failed install log does not expose its exact selected cryptography version; none is invented.

`67f648a` requires published binary wheels for cryptography and cffi, while permitting pyftpdlib's pure-Python source package. It retains the install report, pip version/check and real fixture imports. Tests, production limits, timeouts and every other workflow job remain unchanged. Private cross-target wheel PE checks retain their own download scope; they are not installed-module measurements.

Actual ARM install selects pyftpdlib 2.2.0, pyOpenSSL 26.2.0, cryptography 46.0.3 and cffi 2.1.1 plus four pure-Python dependencies. All eight selected package hashes agree with original public PyPI release metadata. The actual report selects native win_arm64 crypto/cffi wheels; native log proves pip check/import/version success and actual FTP/TLS test execution. Installed module files/PE headers were not downloaded from the runner, so full installed binary composition remains unqualified.

## Exact native transition and artifact seal

All 21,532 immediate `cae0f04` predecessor names are retained. Exactly forty ARM outcomes change from explicit fixture skips to passes: 34 new owned FTP/TLS controls, five older FTP controls and one older FTPS control, with every original reason preserved. All other 21,492 names/outcomes/skip messages remain unchanged. The 34 FTP controls now contribute 136 actual native passes; six Git controls contribute 24.

The independent seal rechecks all 21 original archive server sizes/digests, every 2,731 extracted member, fourteen raw TRX inventories, four toolchain receipts, 92 actual locked-graph receipts and 25 original API command/stream sets. Sixteen Git adverse/recovery JSON observations and 212 rename observations are reread. All 767 tracked product/test source files agree with `cae0f04` after CRLF normalization; the executed CI producer remains `67f648a`.

Original unavailable-fixture reasons, failed `fd6b44d` native logs, the CLI ANSI-output refusal, compatible-wheel preparation and native metadata receipts remain hash-bound. The first available reader refused non-unique names; its corrected version then refused a reason assumption that omitted six older fixture skips. Fresh readers preserve all forty exact reasons. The original full collector source-pin refusal and its corrected version remain. These are observer corrections, not rewritten executions.

The parent independently rehashes all seventy essential receipts after the complete collector seal. Earlier finite transfer/notice/native byte-oracle qualifications retain their actual sources; no fresh independent byte oracle is inferred from aggregate CI outcomes. No native desktop, physical device, contract freeze, release candidate, legal/SBOM closure or stable publication is claimed. The later picture producer `8a5833f` and its CI attempt require their own evidence.

## Selected evidence

Private `FileCatReleaseEvidence/arm-ci-native232-v1`; full nested inventories and the original failed-run receipts remain in the linked manifests.

| File | SHA-256 |
|---|---|
| independent-arm-ci-final-v1.json | 17e94b54ed7c5141956eff053506067804f8d7b214a471ea41376164200eec9e |
| available-native-summary-v3.json | 409686c034d0b875ca291e2512433df32a6e33b068aff0dfe22d771853aebfbb |
| assets-attempt1-v1/independent-assets-ci.json | f78de3e5c21eb605ed160911d26cf9f80ca27ed9cd8e73bd453dc03a50e2da9f |
| available-reader-refusal-v1.json | 2bbfc20b12e9182f364e82ed293b4ff212986587147298e1d165b4b04a43e2ed |
| available-reader-refusal-v2.json | 29cb8f8967e5ec42f6b0c591f0715b58885c3f86ffef8c3c7204795c97d13df8 |
| full-collector-refusal-v1.json | 5d9e1879c047f2aa54e00847455137c8b7a08c5313f365013361dd9fdc45257c |
| parent-independent-reread-v1.json | 528a2ac68cb0895604522cf86f36485858678c0d5071b9bcf66f5be15ef4a69f |
| ../remote-lab229-ci-v1/arm-fixture-followup-v1/failed-arm-job-log-v2.json | b87c5bec38176460ef443159f9308cd6ba0a04ac870f382dfcd8e26d7e160d4b |
| ../remote-lab229-ci-v1/arm-fixture-followup-v1/failed-arm-job-log-v2.txt | 2c2aabe879a86164f1a6c7acbfbdf8beac0d66a780088c77cac7451579e8018e |
| ../remote-lab229-ci-v1/arm-fixture-followup-v1/independent-wheel-policy-v1.json | aec31b842bbe5b34ea65c638522a43e68df939bdad9ce13d5befebba062be8e6 |
