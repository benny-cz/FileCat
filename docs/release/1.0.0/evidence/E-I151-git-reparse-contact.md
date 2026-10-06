# E-I151 — Git metadata links contact a network endpoint during automatic badges

Classification: reproduced preliminary component defect; correction and qualification pending. Plan I16/V23 B10/V24. No installed candidate or native desktop observation.

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
