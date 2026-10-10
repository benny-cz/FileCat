# E-I310 — broker consent text could conceal later operations

2026-10-10 CEST. **High; remediated for preliminary controlled scope.** V06-CONSENT, V23 and AI-13 require the administrator helper to display the immutable plan it will run. Original product **7e6b2d1c92ad14a512e9daf3991c7b919da63940**, with the explicitly declared new test overlay, accepts Registry names containing NUL. Its consent page then contains NUL, so the native UTF-16 string ends before later operations. The runner rejects that first invalid name but continues to subsequent steps. An owned per-user Registry control proves that a later value commits even though the original native text projection conceals it. No UAC bypass or actual human approval is claimed.

The same unchanged 22 controls produce **19 failures and three ordinary/international-name positives** on the local original producer and again in the Windows VM at measured medium integrity 8192. Newline, carriage return, tab, Unicode line/paragraph separators and bidirectional controls also allow names, paths, destinations or value previews to break or rearrange step text. NUL controls cover the first, twentieth and twenty-first steps across a page boundary. Full plan serialization stays unchanged.

The correction escapes control/bidirectional characters in each described operation before adding trusted step/page separators. Native dialog titles, introductory text, footers and refusal messages receive the same conversion, including the message-box fallback. Original plan bytes and execution semantics remain unchanged; ordinary international names retain their exact spelling.

Declared corrected source/test overlays pass **22 targeted tests and 99 affected broker/Registry tests with one exact existing benchmark skip**. All **78 predecessor names/outcomes/messages/skip observations** remain. The identical compiled fixed tests pass **44 Windows VM controls**, 22 each under measured medium 8192/high 12288 parent and child tokens. All three owned Registry fixtures are removed; an independent native query finds no fixture roots or owned child processes. All 150 staged product-file pins and all 193 reused private .NET 10.0.12 runtime files remain unchanged. No workstation UI or VM console is operated.

The independent reader retains its first transport-schema refusal and uses a fresh reader with the actual PayloadArchive field; guest/test results are unchanged. The actual corrected helper compiles through the separately recorded [I311 build/publish controls](E-I311-bootstrap-intermediate-path.md). These controls verify native string termination and ordinary Registry effects, not actual dialog layout, mouse/keyboard/assistive technology, full installed-account consent, candidate or whole-I17 acceptance. Committed/hosted follow-up remains required. Physical-source HOLD and explicit human GO remain.

Committed follow-up: [e0bee14 exact/native and original hosted qualification](E-CI-broker-consent-bootstrap.md) is sealed. Original overlay/build identities and all limitations above remain; the follow-up does not establish whole-I03/I17 or candidate acceptance.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain canonical source, declared overlays, commands, payload hashes, original failures and owned restoration.

| File | SHA256 |
|---|---|
| `i310-consent-text-20261010-v1/independent-final-v1.json` | `7a532e42010f5bdb398c085f01f04a1c19059d6ec35ad723dab6cd23e5e79d20` |
| `i310-consent-text-20261010-v1/independent-restoration-v1.json` | `d8f0c8cfcc5baf854dd877460e2f6dc6f66f84235d763e5464e5e73c17bde664` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/baseline-v1/inputs.json` | `2590c79798c8b5ee5d7c1e277ca1318886fe7b8809a3a84a38a8c3935c6f1adf` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/fixed-v1/inputs.json` | `f8057e1f7274fc6f2215f250c194929d50c06ecf8950e600683441d5c7df98f5` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/baseline-v1/controls/command.json` | `41763eb3542400a303c87d9a46a54efaaf63068271cd1d645976e4e75e7829b8` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/fixed-v1/controls/command.json` | `4aae1e2b62d8bc4c782123ff467b9bd1fc1805b266f70bb9b3657590f220bf13` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/fixed-v1/affected/command.json` | `34729e0e6e0489aea64f78bb9cf6cb9e8a6d719a2e703ef8b79a14ae710184ea` |
| `i310-consent-text-20261010-v1/native-baseline-medium-v1/transport-final-v1.json` | `0fed3cd7a89517140eeab1672050416cf420cf1aa450b1428c326a1bcf6faffa` |
| `i310-consent-text-20261010-v1/native-fixed-medium-v1/transport-final-v1.json` | `dcd7debf148bf09a5f1470bed7d096d3b541d7125864dd50bec943363e4b3e2d` |
| `i310-consent-text-20261010-v1/native-fixed-high-v1/transport-final-v1.json` | `a174a80a5efac2bcd506811b622a513e6b7dc0e7b05c7e107d6673b8030de7be` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/seal-v1.py` | `eb3ceabb5ec72839f826e914e22650a8d5a7e05b5bc940a8774531931e8ccdca` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/seal-v2.py` | `3999827ec8d5a824978d9be2517507328b993b0d4d9375f62f2944ba8eff68ef` |
| `i310-consent-text-20261010-v1/reader-v1-refusal.json` | `f5ad7b4800b5a15b7b670d161081984197ce8921f247e614a589c9f35fee9868` |
