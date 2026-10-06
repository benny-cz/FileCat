# E-I151 — Git metadata links contact a network endpoint during automatic badges

Classification: committed preliminary remediation; wider native/candidate qualification pending. Plan I16/V23 B10/V24. No installed candidate or native desktop observation.

Actual source `52df3d77844c79290289892a9ef8b26b18c38423`, FileCat.dll SHA-256 `448bb5c0ced81eb4940b652c10a97865fc4053b28dd4d44a2d199dc31dc5d83c`. A private self-contained probe calls production `GitStatusReader.ReadAsync` by reflection in the elevated disposable Windows 11 VMware guest (build 26300, Admin). Explicit installed Git SHA-256 `fec691d80fccc35fcc309fbc9f720536c1d795b8a562ec169f28c9923da9600f`. All 354 production and 192 probe files verify before/after; owned payload processes are absent afterward.

The ordinary repository returns Modified in 139.4 ms. An owned local `.git` directory symlink names a fresh share path at the owned Ubuntu 26.04.1 guest `192.168.58.129`; its link attributes and target are retained. The actual API returns no snapshot in 104.4 ms, but the independent Ubuntu root tcpdump records **14 additional TCP flows / 126 packets** to that endpoint's port 445 between the two unique exact-byte TCP controls. All 14 client payloads carry the SMB negotiation signature; the independent receiver records the same bytes and ports. Total capture 328 packets, all untruncated; native decoder count matches and reported kernel drops are zero. The receiver sends an ASCII acknowledgement only, with no SMB authentication challenge. No credential exchange is claimed.

The guard classifies textual drive paths as local, then probes metadata beneath the link. Missing badges therefore do not prove absence of contact. The Windows kernel's SMB packets have no user-mode PID in this capture: attribution is limited to the owned fixture and bracketed production API phase, rather than an all-process or all-network claim. Packet sequence/known tuples avoid cross-machine clock subtraction. Local link-attribute inspection occurs before the first control; all 126 additional packets occur after that control and before the second. [GetFileAttributesW documents that a final symbolic link returns its own attributes](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileattributesw), supporting an ancestor-by-ancestor refusal before probing beneath it.

Both owned root capture processes and listener are independently absent after stop. No firewall or system service changes. Both VMs remain running as authorized. No physical USB activity. Wider Git path variants, races, protected effects, native UI and candidate qualification remain open. Earlier E-V24-D2 literal-path results remain historical finite observations and do not cover this indirect case.

Original private preparation rejected a clean payload against a different controlled manifest before staging. A fresh producer verifies the clean native manifest. The first executor encoded a Windows path with a Python vertical-tab escape and VMware rejected it before starting the native script; its receipt/output are retained. The corrected raw-string executor starts the unchanged staged native probe. These are observer failures, not product executions.

Raw root: `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\browse-traces-20261006`. Retained file pins:

| Relative path | SHA-256 |
|---|---|
| `git-reparse-producer-v1.json` | `f552a7756952f89e2203d13cd1138768030e74e1609ef409daacb8e3632b6fba` |
| `native-git-reparse-v1.ps1` | `525f17b82ff50d1b2f0193bd5f4f2d91fe48ed73f5d91ba6761eaa37b3913e33` |
| `git-reparse-probe-v1/Program.cs` | `22a7f54b0ca168a261d6a209115fc54cfe435d698a2b580cd8ac37f81ef782e8` |
| `git-reparse-probe-v1/GitBoundaryProbe.csproj` | `c45fb49dd17c0258bec5db485afbeb26ac19e89fdc9d29e2d8aa4b7c02174de6` |
| `windows-git-reparse-v1/manifest.json` | `d2cb59e947c59ab2497286e34591ade28b22a736886d5b20bd716d08efedf515` |
| `windows-git-reparse-v1/outputs.zip` | `ce0813004de191c582b1fc80f9c66e8301aec09d6c2bce2a5d9136c9ca1751a7` |
| `windows-git-reparse-v1/transport-proof.json` | `e23265ab28f8f581ed5462066a0b5097d0e8563ff98ee7648fe1f21924de0951` |
| `windows-git-reparse-v1/retrieved/probe-result.json` | `8f7076ab642f0d67a885c4a250adee93759bd43143b4ee9b6ee842ab4100e109` |
| `windows-git-reparse-v1/retrieved/fixture.json` | `af9c3cef30adaebe07c572289e4c819e92921277bb7bd7851617571007c7e604` |
| `git-reparse-network-v1/capture-manifest.json` | `530b9947f821fcd3e2660627e1a53f69d268c0f6b13b13f5d9c2bf3dbcf0cc6f` |
| `git-reparse-network-v1/outputs.zip` | `b64218da9bd40641d2b1d4363d5df1b98df61936d083c7dfa7550db76fff0a8a` |
| `git-reparse-network-v1/retrieved/capture.pcap` | `76614e60e8f0dc7669b2ce23fdaa8712f8604d9d8b07e29477b2919715c4cde6` |
| `git-reparse-network-v1/absence.json` | `6d7a8033608bde2c9cc37d2a5adaf9dfcccbe9e2c4f752b8a7c6142c458fc77d` |
| `git-reparse-network-v1/independent-git-reparse-v1.json` | `0808ae3074abf9fd622bf8c017aae570a0aa53b4b915c62104220aca1434d15e` |
| `reparse-preparation-failure-v1.json` | `7aecb86bf5f942f8c2e9088372d00cf76219c39aa61d3b785c0ffcd51f5fd1c4` |
| `execute-git-reparse-v1.py` | `a6ab41476b760264b22714c054b3f4ef63622d73dfe7f0f484ddc2bf5eb32af1` |
| `execute-git-reparse-v2.py` | `ea883e5cade7339c01af7f4cf0c0c87c04fbb16bb87f7d6609d8667028ce194b` |

Correction `483032ac44e8f6d2f60f46413682548bc0dd5a7c` checks Windows path components' own attributes before probing beneath them. The folder check precedes Directory.Exists; child `.git`, linked/shared directories, config and alternates metadata paths are checked. Local links also leave automatic badges unavailable. Missing paths remain harmless after existing ancestors pass. This is a conservative stable-path policy, not race-proof handle-relative validation of every file Git might read; swaps, other child-read paths, DOS/device aliases and Unix mounts remain in broader I16 qualification.

Seven durable controls cover five actual local junction placements (worktree, `.git` directory, gitdir target, commondir target and objects directory), paths beneath a junction/missing descendants and an ordinary/regular-file linked-worktree positive. Identical before/after test DLL `3c589e892e40b831e646ab89c9b485e245562430f3e8d41bc0699f1ad951682b` gives six failures/one pass before, seven passes after. Affected host 20 pass/1 capture skip; full App 378 pass/23 declared skips/401 exact cases. Original failures retained.

Clean source exports independently verify **891 raw Git blobs**, each Git object SHA-1, size and SHA-256. Clean native Windows runs 21 pass/3 declared capture skips; Ubuntu 14 pass/10 declared skips, including six explicitly Windows-only new junction cases. Each has all 24 selected names, all existing six Git environment controls pass, and Windows all seven new reparse controls pass (Ubuntu one portable positive passes/six declared skips). All 354/350 payload hashes, six retained outputs, owned test processes and empty test temporaries verify. Mac native payload upload fails with SSH timeout before test launch; one further eight-second read also times out. The owner connectivity gate is queued until 08:40 CEST, no result fabricated. Previously restored Mac power settings remain unchanged.

The unchanged original private native probe is repeated with all 546 payload paths identical and **only production/FileCat.dll changed**, now SHA-256 `b5a58cf6a31fc185c0a9384f292585bc9dfd02d72997d4e9ec0d2af72999cb3b`. Ordinary badge Modified remains (144.0 ms); the symlink case returns no snapshot in 3.254 ms. Fresh independent capture: **55 untruncated packets, two exact controls and zero additional named-endpoint packet or connection**; reported kernel drops zero. The other packets remain retained without attribution. Both Windows payload processes and owned Ubuntu controller/tcpdump/listener are absent. CI 37407598742 attempt 1 is running on exact committed correction; full inventories/digests will be sealed when complete.

Additional retained pins:

| Relative path | SHA-256 |
|---|---|
| `independent-reparse-host-v1.json` | `487573191c9250be65ce1ef6593cf6053a74e3b8c795004364fe0f46536a96dc` |
| `independent-reparse-comparison-v2.json` | `5506bbb217bcad6e44771cf635a34b1268231c2f38cb7b30d16d9e5d0c3f0b55` |
| `reparse-clean-v1/producer.json` | `b5cb4ea2a619236e571a3ac6cfc9e6f5c75e0c0528c9f21a7fa6966f4ce5ff32` |
| `reparse-clean-v1/source.zip` | `c449f0215aa60b851e90ffb2d9f8ad62082e7317e8674b86a7ae6fbfb922f56f` |
| `reparse-clean-v1/windows-executed/independent-guest-v1.json` | `4840b077e650b103c1e96c717fcb6412c00f661937859c22481b9d4a65697eba` |
| `reparse-clean-v1/linux-executed/independent-guest-v1.json` | `25e801448d8fcca7c4be4523fe3b89510543ff55886e215df2cb74891f5da5ef` |
| `reparse-clean-v1/mac-executed/upload-payload-stderr.log` | `4ccbc688400288e58cd5293523a6785b050593ec32473586397d765965f8ed52` |
| `reparse-clean-v1/mac-executed/upload-payload-exit.json` | `4ceee1e065769b56acb0a2b6758f50cbddc7c8c246379c725e27e7edc2385495` |
| `windows-git-reparse-v2/manifest.json` | `06b44bea6535d9fbd8253c750d353cd849d17a97d5fcce5864a4a02ae2c60ca2` |
| `windows-git-reparse-v2/outputs.zip` | `616b5e3a8afaf8312b74a2ea51d1071189c4facae6a5952c2d6ed7f322618dd7` |
| `windows-git-reparse-v2/transport-proof.json` | `fa8f3034cc1f15e0c2336afbd8e89b9810c0eae1b94b4cf2f81324b31599e1fc` |
| `git-reparse-network-v2/outputs.zip` | `44bb2d9f6354093df99c44b50dc8d017dc74f5e925e8e551d2bf5ba04065be2b` |
| `git-reparse-network-v2/retrieved/capture.pcap` | `075682a61a535467b04b98988d4a118a207db2595d753985a624004841672857` |
| `git-reparse-network-v2/absence.json` | `54e4bd2f4e4acff31e5cf6c8d60da9536b04a639731fbd6e0e1a865321592d11` |
| `git-reparse-network-v2/independent-git-reparse-v2.json` | `522e0d8f35e8cf4a1486a843ede43e4df08b5614f9b3f0c1d35f56d52abd2366` |
