# I230 — tolerate unavailable Git during parent status refresh

**Six new controls pass on committed source `cae0f046d541f2d3f75f01e12ab668e72544f0f6`; the full App suite passes 1197 cases with 25 retained explicit skips.** This is a Low–Medium optional-badge resilience correction. I16 and wider browse/launch qualification remain open.

`GitStatusReader.ReadWithParentAsync` previously let executable-launch `IOException`, `UnauthorizedAccessException` and `Win32Exception` escape, although its ordinary status path already handled those unavailable-Git conditions. A missing or invalid executable could therefore abandon a same-folder badge refresh. `FileListControl` already catches/logs this exception: no file-list crash, arbitrary-execution finding or whole security boundary closure is claimed.

The parent path now uses the same unavailable-Git fallback. Cancellation still propagates, and healthy ordinary/parent snapshots retain exact modified/clean badge results. Six controls cover missing executable, invalid executable, cancellation and ordinary before/after/parent behavior.

The source-baseline export plus only the new tests gives four passes/two failures. A controlled baseline retains the identical final test DLL and substitutes only the actual original `9f643d9` App DLL: again four pass/two fail. The corrected working Git suite passes 116 cases with one existing unavailable-share capture skip, preserving all 111 prior Git names/outcomes. An unchanged eight-case native observer substitutes only the App DLL: the two parent launch exceptions disappear, healthy badges remain, and owned tracked/config/index bytes agree before/after. This native observation retains its working-overlay identity; the final canonical App run separately qualifies the committed source and the same six controls.

Original four-platform CI **37833081521 attempt 1** passes on the committed source: all six Git controls pass in each lane (24 executions). Sixteen raw missing/invalid-Git before/after observations, exact badge snapshots and unchanged three-file fixture hashes verify independently. Original artifacts, complete inventories and locked graphs are sealed in [I229’s shared CI record](E-I229-ftp-read-completion.md#selected-evidence). All 21,372 previous case names/outcomes and prior skip texts remain. The FTP fix in the same source batch is independently covered by [I229](E-I229-ftp-read-completion.md).

Both owned native fixture archives retain every member before their exact fixture directories are removed. All 32 previously sealed raw inputs and four restoration inputs verify. No machine, account, global Git configuration or persistent setup changes were made.

Missing/invalid executable controls do not qualify races, every indirect Git effect, unusual configurations, native desktop badge rendering, hostile browse/launch or installed-candidate behavior. Wider I16/V23/V24 and all candidate gates remain. No physical-source, freeze, candidate or stable GO claim is made.

## Selected evidence

Private `FileCatReleaseEvidence/git-parallel230-v1`:

| File | SHA-256 |
|---|---|
| run-git-slice-v1.py | 65548517317f15727f3e3eac53af11ce0e09961bbc6475c4f867b3c8e5ec8b78 |
| run-controlled-native-v1.py | 620650c3537056cd8559f02eaf9b8019467505c09911e1dc39ed60bf13ce5a81 |
| seal-git-working-v1.py | 985871725e3ba58c31c880a15fb6d88e3e8b2d130a285b3ae81badc3b95b731d |
| baseline-tests-v1/command.json | 3ef99a04a85710162628866ce237d0fd7f10b0517a11d66886e675d89c31ba7c |
| baseline-tests-v1/results/baseline.trx | 6c64791951d2fc04cf0628b03c0a667d176b7813d938e3eb2eae56b32c3aa05e |
| controlled-baseline-v1/command.json | 1d16e115555630c215a81ff7efac1b4aa746530ae0211cfb1f5e4676eb063213 |
| controlled-baseline-v1/results/baseline.trx | 4f730f639543cf25adacc185cacd7ce11b06f88460056b3853a2f56219752292 |
| working-tests-v1/command.json | 74db850ee170233a80c5d7b02d1740dde718dafec368697a253d036a57785d3c |
| working-tests-v1/results/working.trx | 0688e33a904e95cf1889eb5176f4b35dc8084db7b09721686bd11186420d9337 |
| native-baseline-v1/native-observations-v1.json | a7979e9f480a4721d28d5f1aee62ca3ef41e669fed9ad3a1415a845037a50f1a |
| native-corrected-v1/command.json | c2c15b8c990c955c7adf3931f74ba6c5fcf78aff59294e801384bd2a46b3e935 |
| native-corrected-v1/native-observations-v1.json | 82d0267c82597b5faca2d48a5ea84e3c98d24774afd745a0933617e2ec320857 |
| independent-git-working-v1.json | 245692382ea50e1e54bff63a1fc4d1c1143eb50aea93b2ab84622fb4d2920cef |
| native-baseline-v1-owned-fixture-v1.zip | 46fddcd358898e79327c1445e881e59e2fc74058c1460d66e90126056fe8feaa |
| native-corrected-v1-owned-fixture-v1.zip | cb128ef4b41552fd1deaf118305e3a3a586953fd2e64496a183e45d5869934d3 |
| owned-fixture-restoration-v1.json | e105e119556082ec8699dfaecfc6734d19ef1f0f16c95fb58d3d88e723fe0fd0 |
| seal-restoration-v1.py | c35a68b603fe6d0efbc366cf388f92b0729124d52cf46e73e5d96cb833fbc879 |
| independent-git-restoration-v1.json | e0d4f6ea6d6986ace8f3d9284807e22e114d7dcb9b3938b865004b403697a7db |

Private `FileCatReleaseEvidence/remote-lab229-v1`:

| File | SHA-256 |
|---|---|
| clean-v4/command.json | 4ee3646b821454256ccdef38d114216a10f2f2540ec1078e372efb6ba7763032 |
| clean-v4/results/app-full.trx | 5aa4960073d52d5dab5fc383599ba4a1c575db1c3c8127ec61db9fea79ec51df |
| independent-final-batch-v2.json | e2e10d6fa715aaffcbdb7db0e50077eac7911d4ee4cbdf8c83233bbb8199e967 |
