# E-I311 — absolute artifact paths broke the native administrator-helper build

2026-10-10 CEST. **Medium; remediated for preliminary build scope.** The actual helper build using the repository's locked inputs and dotnet --artifacts-path reaches managed compilation, then fails in the native bootstrap. Its target concatenates the project directory with an already absolute IntermediateOutputPath, producing a path containing a second E: drive prefix. The original error and complete command/payload receipts remain. The original project is from **7e6b2d1c92ad14a512e9daf3991c7b919da63940**, with the separately declared [I310 consent correction](E-I310-broker-consent-text.md); that correction does not alter the failing project target.

Both Build and Publish targets now join the project/intermediate/leaf components with System.IO.Path.Combine. Relative paths still resolve under the project; absolute SDK artifact paths retain their own root. No native trust checks, bootstrap inputs or publication policy are relaxed.

Four fresh canonical source exports with declared consent/project overlays pass actual **absolute x64 build, default relative x64 build, private framework-dependent x64 publish and private framework-dependent ARM64 publish**. Six independently read native PE headers verify x64/ARM64 machine types; six compiler/bootstrap receipts, managed outputs, original source inputs and commands are pinned and rechecked. ARM64 compilation is not physical ARM64 execution. These are preliminary private payloads, not a release candidate or publication. Full source/license/load provenance remains I03; committed/hosted follow-up remains separate.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain canonical source, declared overlays, commands, payload hashes, original failures and owned restoration.

| File | SHA256 |
|---|---|
| `i311-bootstrap-intermediate-path-20261010-v1/independent-final-v1.json` | `449c987e409226720dcaaa9f7ce9e6ccb7c1917587d6e736c5b236d6dec0f935` |
| `E:/FileCat/artifacts/release-evidence/i310-consent-text-20261010-v1/fixed-v1/helper-build-v1/command.json` | `ba6799d9a0cc9014b4d7b8cbc0929f64187d1edf0034e5ca992c99d3250133b8` |
| `E:/FileCat/artifacts/release-evidence/i311-bootstrap-intermediate-path-20261010-v1/absolute-x64/command.json` | `68be0b6e7160b560fc56cf86553fe8440ee83b921780618af1bfb1a1f67bd91b` |
| `E:/FileCat/artifacts/release-evidence/i311-bootstrap-intermediate-path-20261010-v1/relative-x64/command.json` | `edfbada97f97f51a6c68e933f5f01287a41834286540b7b8c956c336ad7024b0` |
| `E:/FileCat/artifacts/release-evidence/i311-bootstrap-intermediate-path-20261010-v1/publish-x64/command.json` | `cdac8ce5e081690af31de1e867e80eaf052718878a3972baecee2b4c2f775e08` |
| `E:/FileCat/artifacts/release-evidence/i311-bootstrap-intermediate-path-20261010-v1/publish-arm64/command.json` | `c68667e240250b1aa120f23fbdbd6387a07fac5130adef00d0fbc0b679c735f9` |
| `E:/FileCat/artifacts/release-evidence/i311-bootstrap-intermediate-path-20261010-v1/run-builds-v1.py` | `3565c33dfd22aca13e1b608509f459961f19e865346e4a7830e51cda94142fa4` |
