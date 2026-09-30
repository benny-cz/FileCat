# E-X01 — suite runs on the lent VMs, the owner's Mac, and CI after the campaign's commits

Preliminary automated evidence (plan S10 lanes on non-CI machines). Builds are `dotnet build -c Release` outputs of the
test projects, made on the execution host (the `5c54181` build in a clean detached worktree), packaged as ZIPs and run
with the xUnit v3 in-process runner (`-trx`). Counts are from the TRX files; "skipped" are explicit `Assert.Skip` with a
reason (since `be6ca25`, E-S01). None of these machines is a final target (E-ENV-02, E-ENV-05).

## Test builds

| Build | ZIP | SHA-256 | Contents |
|---|---|---|---|
| `be6ca25` | `tests-be6ca25.zip` | `9730ee0a0fc36210bbcf65984eac0417bb3d10d8b2d0a51b17af32350f4b5319` | App, Core, Platform.Windows, Remote |
| `47c27b9` | `tests-47c27b9.zip` | `217a3c232046c13ad623fb90f38e2801b64d0e7d1db3c81a8d91d136c116b3cc` | App, Platform.Windows |
| `47c27b9` | `tests-47c27b9-portable.zip` | `d6d3ab3c67f321c184c8193c43b1f76faa8f4731d2e4729b7bf8b5ddb10d04ca` | App, Core, Remote |
| `5c54181` | `tests-5c54181-portable.zip` | `89cb7ff3f1c7ff5f2b592fa26915b32d77ae44453f2962a166c7f882dde991a5` | App, Core, Remote |
| `5c54181` | `tests-5c54181-win.zip` | `c8d08d802c8264d77e7027f0251b6e4e2d62b91f6602458c73033716b3d25643` | App, Core, Platform.Windows, Remote |

## Runs (total / passed / failed / skipped)

| Run | Machine, account | Build | Core | Platform.Windows | Remote | App | Findings |
|---|---|---|---|---|---|---|---|
| W1 | Windows 11 Insider 26300 VM, administrator **unelevated** (interactive session) | `be6ca25` | 543/505/0/38 | 102/82/**1**/19 | 43/34/0/9 | 159/154/**1**/4 | I21 (Registry views); I22 first occurrence |
| W2 | same | `47c27b9` | — | 108/89/0/19 | — | 159/155/0/4 | Then the Synchronize test alone 5 × and 40 ×: 0 failures |
| W3 | same | `5c54181` | — | — | — | Synchronize test alone 150 ×: 0 failures | I22 baseline (not reproduced in isolation) |
| U1 | Ubuntu 22.04.5 VM (kernel 6.8), user session over `vmrun` | `be6ca25` | 538/507/0/31 | — | 43/42/0/1 | 159/150/0/9 | — |
| U2 | same, after Samba, vsftpd and OpenSSH servers were set up on it | `5c54181` | 538/506/**1**/31 | — | 43/42/0/1 | 159/150/0/9 | I23 (discovery naming under load) |
| M1 | MacBook Pro M1 (8 cores), macOS 26.6.2 (25G83), over SSH, .NET 10.0.12 user-local | `47c27b9` | 538/505/**1**/32 | — | 43/42/0/1 | 159/150/0/9 | Keychain test failed with -25293 over SSH (test defect; fixed `64ed037`) |
| M2 | same | `5c54181` | 538/505/0/33 | — | 43/42/0/1 | 159/150/0/9 | The keychain case now skips with its reason |

TRX SHA-256: W1 Core `e2d0b4584e75ebccda6b6b98f290fcf0f2bf6f1e03c27bde28186257b5e977fc`, Platform.Windows
`0cdc46ae346266779c89544c0cd318b6045523cb38b44b0f83de1ab4eec967f6`, Remote
`f0dfc02f3c08a5e3690a2576aa25027a64420bd91b0320bcfef18b3aa8da3c91`, App
`9953ee6db49d35ce7029d121cc9544b6338c8aa69bc00210af5f8a293648cc5b`; W2 Platform.Windows
`c4b4fdfae2f23b5de0850ea1d01cf7fde02a00baac78d9ef39dd962a7bf41798`, App
`b200982cd4cf657cfb603acf652a45f8855bd48a16357f6684cd234c4cae161a`; W2 40-run summary
`bd0a2e183a6506768824f5fc0db0e3ec86f19a6fb4d2ea2da86b8396dd5a02d7`; W3 log
`23ea331c96a50e1bc16048d30fc0bab1b739d5ae35433482b86b11516b181fe0`; U1 Core
`80a1f45da0164175ca9286ed77cd44a313a6b606ba3682bf308a7042dadde32b`, Remote
`f476875f04a7f6e608387dd4babd70d9d8d954e87fd13bbd6e23750af93728f5`, App
`954893fd46758079b0167f6434f219fe28ffbb67c220e9e6e40bf34a8cab1865`; U2 Core
`856873aeb663107dd0e7081a82e4357104396b83f680128c2624d1b1d6661f8d`, Remote
`08bf78432bcd119bdb3d3f7b38052e150028f9ce85fec7684a269072c6187e21`, App
`4423fb5dd0a4ee565d99063c02d4a0443516ea3b29e9175e75a4ca664c28ad05`; M1 Core
`d01069598eaae0a3dc9d2620969b05934d3d5ddc244ec82a9538d4f7892ed6b8`, Remote
`8cee4d7a98dd5e8d35d938dd30173c169b85e4219ea403f4d56be56faf8c4ba3`, App
`95e1f1b092d964195738f4e09abc4ecef29b94addc233451b444a750e9b75012`; M2 Core
`0f171c4ad7d2f251fb6fd3791de834f6308b7dd2cec5850bbc906494c566744c`, Remote
`974e4f7d49f0cac4e42240eeb0fdef882b95138ccfa1976784529e3da400dbe1`, App
`99b54317f4caf1367136da13cf8a649665c8983f7c69b469692c91040f2a7556`.

## CI runs of the campaign's commits (GitHub-hosted, elevated Windows lanes)

| Run | Commit | Result |
|---|---|---|
| 36759824994 | `552aa62` (records only) | Windows ARM64 **failed**: the Synchronize test, `changed.txt` read "old" (I22, second occurrence); other lanes green |
| 36760362261 | `be6ca25` | All four lanes green |
| 36763921747 | `47c27b9` (includes `33b7de2`) | All four lanes green |
| 36765116273 | `64ed037` | All four lanes green |
| 36767308673 | `5c54181` | All four lanes green |

## What the runs add beyond CI

- **Unelevated Windows** (W1, W2): CI's Windows lanes run elevated, so code that behaves differently without
  administrator rights is exercised only here. It found I21.
- **Apple Silicon hardware** (M1, M2): CI's macOS lane is a hosted runner; the owner's M1 runs with a real login keychain
  (locked to SSH sessions, hence the -25293 skip) and gave the same App results as Ubuntu.
- **A second Linux kernel and distribution release** (Ubuntu 22.04 with kernel 6.8, CI uses 24.04).

## Limitations

- Test builds are Release builds of test projects, not the shipped packages; one build host (Windows 11 Insider).
- W1 ran four suites in sequence while the Ubuntu VM ran its suites; machine load differed between runs.
- The Mac is the owner's personal machine with its own state (not a clean install); its keychain cannot be unlocked
  without a prompt over SSH, so the secret-store test did not run there.
- Raw outputs are retained under `artifacts/vm/` on the execution host (not the release-owner store, DEC-10).
