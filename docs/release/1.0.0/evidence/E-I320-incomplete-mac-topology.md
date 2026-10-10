# E-I320 — incomplete Mac disk replies become a complete topology answer

2026-10-10 CEST. **Potential Critical deleted-data safety; remediated preliminarily.** The production Mac topology interpreter filters out unavailable APFS members and treats a missing whole-disk/bus reply as a physical leaf. Both can present an incomplete list as proof that recovery writes use a different disk. Empty APFS store identifiers also throw an uncaught range exception in WholeDisk.

A declared query/image-resolver seam factors the existing 7b73e2b method without changing its interpretation or lookup ordering. Controlled empty/wrong-type/missing-field replies exercise that production interpretation on Windows and macOS UID501: **12 unsafe partial answers and two empty-identifier exceptions**, with **four healthy controls**, on each platform. These are injected query replies, not evidence of an actual diskutil failure, concurrent native removal or physical data loss. All **1469 canonical raw blobs**, unchanged source outside the two declared overlays, and the exact baseline factoring verify.

The correction requires every APFS member to supply a usable store identifier, refuses a wrong-type APFS list instead of falling back to ParentWholeDisk, and requires a nonempty whole-disk bus reply before classifying a write. Incomplete information remains unknown. The identifier parser refuses strings shorter than diskN before indexing. Known physical writes, complete multi-member image backing and source classification remain usable; unresolved image backing remains unknown. The query seam also supplies permanent deterministic regression coverage for the same production method.

All **18 final Windows controls pass** and all **25 final Mac controls pass**: eighteen new controls, the existing real owned-image mount-replacement regression and all six Unix source-case admission controls. The real Mac regression verifies all **64 KiB known file bytes**, current source overlap and removal of the previous disk from the answer. FileCat opens no device. Detached-image hash differences are not attributed solely to the known marker. All **100 affected tests pass** with the same **34 explicit skips**; every **134 predecessor outcome/message/skip** remains, including the separately recorded Windows native-only and Unix-only skips.

Independent postchecks verify all **86 qualified staged and 534 reused runtime pins**, no owned image attached, payload process, fixture or temporary root. The intermediate fixed artifact's additional 43 staged/267 runtime files and cleanup are also checked. The first fixed compile predates the final query-deduplication edit and supplies no final qualification. The next intermediate Windows/Mac runs still fail two empty-identifier controls; those original failures remain. Fresh v3 artifacts contain the complete correction. An initial reader compares tracked source against the intermediate root and fails; its source and observed failure remain, and the corrected reader verifies the final root. No assertion, skip or safety deadline is weakened.

No sudo, UI, account, power or persistent system changes. [Exact committed/native follow-up](E-I320-native-qualification.md) now passes at 399a829. Original hosted collection, broader native adverse-query/topology races, I106/I110 physical-source HOLD and installed-candidate scope remain. Twenty broader unresolved statuses, all owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i320-mac-incomplete-topology-20261010-v1/independent-remediation-v1.json` | `d5aa07b4623088cf41eb2a4e3cfb0f34e8fe167d49ac24f4ea2806ac2eda3e7a` |
| `V:/FileCat/artifacts/release-evidence/i320-mac-incomplete-topology-20261010-v1/baseline-v1/inputs.json` | `21758d8532671e9e7e9f893509ecb3a828c192ee8b94032cc0ba88a95d4024c1` |
| `V:/FileCat/artifacts/release-evidence/i320-mac-incomplete-topology-20261010-v1/fixed-v3/inputs.json` | `5bcb3755f9f52339d6eea237cf08d5665ae07f882c5f9290bbd393c2852f3400` |
| `i320-mac-incomplete-topology-20261010-v1/baseline-macos-v1/transport-final-v1.json` | `78e1b11cd68e411a5f71ab26ba32589342af94135e3aae3b0628b1d7e9c79b87` |
| `i320-mac-incomplete-topology-20261010-v1/fixed-macos-v3/transport-final-v1.json` | `93c79a38001fc092d768d8f188824b74badca31aafdcf854f904daacf7ea140b` |
| `i320-mac-incomplete-topology-20261010-v1/postcheck-v3/independent-final-v1.json` | `8bc9419996cfa4c07a3218b2cbbf8a161c884de1cf498dfce900217404e6e8b0` |
| `i320-mac-incomplete-topology-20261010-v1/reader-v3-observed-failure.json` | `e53e7956d5de6ab262d6523ed2eeca643ebd7a78d6438c874933e9f8ce976709` |
| `i320-public-20261010-v1/retained-tool-sources-v1.json` | `d6fdf9abc504dc4e7665d5be64713a1df35656152c7ac8247eb99adf3657c9f9` |
