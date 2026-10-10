# E-I302-NATIVE — committed comparison ownership on native platforms

2026-10-10 CEST. Product **91eb3443ee8b5aa6c0222a5a5ac01bfe8d49843f**, exact no-overlay build; all **1402 raw Git blobs** independently checked. Baseline **534187f3f2139f1ec7b736805bcb9932aa884c6f** checks all 1397 canonical blobs with three test-only overlays and no product overlay. Native deployment copies existing built binaries, with exact producer/input/archive hashes; no product rebuild occurs.

## Final results

The same observer passes **30 checks per platform /90 total**, with **42 compositor completions per platform /126 total**. Counts are 16, 4096 and 32768 entries/lines. Each platform exercises live/closed file, folder and synchronization views, held replaced folder/text rows, two synchronization borrowers and a late folder result. Closed windows and old rows remain deliberately held. Live borrowers stay readable, checkbox changes still affect their actual items, both closed borrowers retire all three tracked input owners, and late results do not republish or mutate the caller.

| Platform | Actual environment | Result |
|---|---|---|
| Windows guest | Windows 11 build 26300, x64, Admin/session 1, actual HWND | 30 passes; private .NET 10.0.12 /193 runtime files freshly verified before/after |
| Ubuntu guest | Ubuntu 26.04.1, kernel 7.0.0, x64, benny/UID 1000, GNOME/Xwayland | 30 passes; private .NET 10.0.12 /193 runtime files freshly verified before/after |
| Physical Apple Silicon Mac | macOS 27.0.1, arm64, benny/UID 501, Aqua/native windows | 30 passes; .NET 10.0.12 /267 runtime files unchanged before/after |

Finite parent/direct-child process samples retain their own counters and timestamps. They are sampled accounts, not allocation peaks or a whole-process budget. Mac PrivateBytes reports zero and remains unavailable. Compositor completion establishes product render progress; OS presentation, physical input, reference-machine performance, required people and candidate acceptance are separate.

## Adverse controls and observer correction

Three unchanged Windows baseline repeats each retain **18 failures /12 healthy passes**. Four early fixed-product runs each retain **27 passes /three two-preview failures**, including a weak-read isolation control and the heap capture. The final observer isolates its earlier strong `Items` access, mode change, render, toggle and close in a separate non-inlined method before collecting. The exact product is unchanged; assertions and eight collection rounds are unchanged. Final Windows, Ubuntu and Mac checks all pass. This establishes the observer lifetime effect without claiming a precisely identified JIT register/root or silently accepting an early failure.

Read-only bounded object walks cannot find the remaining path within their depth/object limits. An owned synthetic 85,011,260-byte heap is captured before any strong `WeakReference.Target` diagnostic read. Initial filtered SOS output is blank because FileCat metadata is unresolved; exact tested assemblies plus a cache reset restore type names. Positive closure/static-handle root checks and heap integrity pass, but walks also miss the deliberately held windows. Therefore a zero-root result is not accepted as proof of no strong reference. All diagnostics, raw failures and command results remain immutable.

The first observer stack-control build refuses a missing dependency lock before compilation; its fresh standalone retry restores the original isolated build configuration. An unused Unix preparation inherits irrelevant Windows-runtime fields; the executed preparation removes them and retains the unused manifests. The first native sealer guesses an incorrect transport-record filename and stops; the corrected reader uses actual paths over the same saved runs. No earlier output is replaced by a pass.

## Restoration and limits

No workstation UI or foreground VM console is used. Windows/Ubuntu commands run through background VMware Tools. The Mac runs as ordinary benny through an owned Aqua LaunchAgent; that job is booted out, its bound caffeinate process exits, and power configuration is unchanged. No sudo, account/firewall policy, physical source, I106/I110 HOLD, contract/freeze/candidate or publication change occurs. Owned payload/result directories remain as evidence. Broader I06 aggregate/provider/native/reference/human/candidate scope remains open.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain complete source, manifests, runtimes, raw process outputs/failures and exact archives.

| File | SHA256 |
|---|---|
| `i302-native-20261010-v1/independent-native-final-v2.json` | `ab36d0935f43ba3331e6e4f21b7a3f770a1f352baec78be4b7b850965bce46a1` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/seal-native-v1.py` | `3f75c2d9994d258cd248fdeaa4f9498f222ac65ab20a2b4ec71949f0277684e1` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/seal-native-v2.py` | `9ae35a276ed4d69ade0c527beae74c5c77cc620a0bccde9753a8729e30368114` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/analyze-owned-v1.py` | `1d54ca3fd7a062b980c033f25c0c7980a8a7ae4c0743f10261f27db4f235593e` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/observer-source-v6/Program.cs` | `8d31aa7424806e8392582138ff0f5856d2ed7ddd5363ad5677887c82e35d35aa` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/observer-source-v6/Probe.csproj` | `aebc4ef0c4199fab45a57dc09b178daab1a0a5d5d8613aabe312f7288d181be5` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/prepare-stack-control-v5.py` | `80a4041c9a960df2cf91e15016aca0f4fec456a39ee103a568a585fc81fa86f2` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/prepare-stack-control-v6.py` | `bb686b3cbb444e7447950752ccf65834ad316dc9c98e0ed83429ae5a8e6d22e0` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/prepare-unix-v1.py` | `69214f38200da00f2b6776f5cb38ada3033a998b85168aaf32ab1ded3faca136` |
| `E:/FileCat/artifacts/release-evidence/i302-native-20261010-v1/prepare-unix-v2.py` | `e8e49b87b9e5624e6e006b677df1231529995595dad86d8e9de2303282c3548d` |
| `i302-native-20261010-v1/stack-control-v5-build-command.json` | `1604e14143ef3ad300270ab8cb46f82ffc04cb4d43b3b50b6deac3f18979a614` |
| `i302-native-20261010-v1/stack-control-v6-build-command.json` | `ce293a88331439cfb6ba3e6b252ff728d06bff4c1285baa2cbdaed9f09b8c0f6` |
| `i302-native-20261010-v1/original/transport-final-v1.json` | `f9658b32bcd183d148cc7e5ca114909fed85d23f3240a2d2a304a6bc2c476eb1` |
| `i302-native-20261010-v1/fixed/transport-final-v1.json` | `64d887e503f831f26fed1274d10d40f5efb4a69e46cc60353cee764ecd733663` |
| `i302-native-20261010-v1/fixed-v3/transport-final-v1.json` | `5ace2620164d6b6ca4c96fa7725af8124fef5830c24133bde2488d8c05a50da2` |
| `i302-native-20261010-v1/original-v4/transport-final-v1.json` | `2a2ab0f45c3a0689829474cb2e0f81e50fb9e18aa7b354e2891002f28044755b` |
| `i302-native-20261010-v1/fixed-v4/transport-final-v1.json` | `4fc462b6bb2b80c53e82e5a40568d3706e4a8fe4c83557b33b6bfedf76412dd0` |
| `i302-native-20261010-v1/fixed-v5/transport-final-v1.json` | `314f4af4d407c5b1e96d5c415c4618f0718985e74ab7fa20efacc1509ec6e554` |
| `i302-native-20261010-v1/original-v7/transport-final-v1.json` | `5983fadadc785bf823a76ce9749531caeccfca7e470c6d50321bacbdd3ac21ad` |
| `i302-native-20261010-v1/fixed-v7/transport-final-v1.json` | `ba2e97b100179635e191bfcfa2825377cab3db3176282aeeb26ace628ce3776d` |
| `i302-native-linux-20261010-v2/linux/transport-final-v1.json` | `cc8edab7894362f9f766a6c6ee790447d5b0f9ee492d45ecf175f37e4a06cbeb` |
| `i302-native-macos-20261010-v2/transport-final-v1.json` | `2422a7cd1c72f86795f29c4025ac7e5b3c3621e6bfd721e1450d14f7e4b75cc2` |
| `i302-native-20261010-v1/heap-lifetime-v5-command.json` | `7aca7d74d82323b4afc23cef4c42fa0a2eb8a55069a409bed804ce8a794f06e3` |
| `i302-native-20261010-v1/heap-native-roots-v7-command.json` | `364db0eaa6bedcb46e83551a30cd4347429ac9564f0cb6587b2031935e0ef1fa` |
| `i302-native-20261010-v1/heap-positive-roots-v6-command.json` | `24c96df2118f84547780beb289c4c1847fe7d21cf1d078b962fd57a0e4d3957e` |
| `i302-native-20261010-v1/heap-roots-v4-command.json` | `be059eedcda232f9b48fcee63fbbf409238078f32b8cea90aa4485e5d55c6366` |
| `i302-native-20261010-v1/heap-types-v1-command.json` | `c000d9ac356d690a593fbdc83179e9a884779175a172519aff2db1edb1d19847` |
| `i302-native-20261010-v1/heap-types-v2-command.json` | `c52bdbb641d20ed4bf424c1db078ad706da074d27f5c22333508f21e25513d47` |
| `i302-native-20261010-v1/heap-types-v3-command.json` | `80bf5e05bc656b2c3aaa95cad5734114be267d284aec1ee4b8096e3d30f21de3` |
