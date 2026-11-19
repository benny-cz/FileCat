# Windows NTFS listing performance

The Windows 11 build 26340 x64 VM enumerated owned directories containing 10,000 and 1,000,000 empty files on fixed NTFS. The actual Windows provider ran with its native enumeration flag off and on. Each pass counted the expected entries. Four paired warm samples followed the first pair, with the order alternating.

| Entries | Managed warm median | Native warm median | Difference |
| ---: | ---: | ---: | ---: |
| 10,000 | 8.58 ms | 8.79 ms | +2.4% |
| 1,000,000 | 767.69 ms | 768.38 ms | +0.1% |

The measured allocations for one million entries were approximately 56 MB per pass in both paths. The separate 1,302-entry parity fixture passed on both runs and compared names, flags, attributes, sizes and times. It also checked native batching and pre-cancellation. Results are in `.tmp/filecat-enum-scale-10000-tests.xml` and `.tmp/filecat-enum-scale-1000000-tests.xml`.

The initial managed and native results in the paired run were 72.60 and 9.57 ms at 10,000, and 5,275.58 and 812.22 ms at one million. The native pass followed the managed pass in the same VM session and benefited from any cache it populated.

## Independent guest-cold runs

The VM restarted before each of six runs, with distinct Windows boot timestamps and an alternating reader order. Each run used separate fresh test processes for the two directories. The VM restarted Windows between runs while the host-side disk cache remained in service.

| Entries | Managed cold samples (ms) | Native cold samples (ms) | Managed median | Native median |
| ---: | --- | --- | ---: | ---: |
| 10,000 | 70.50, 51.35, 51.48 | 49.71, 46.42, 42.43 | 51.48 ms | 46.42 ms |
| 1,000,000 | 6,840.56, 4,138.22, 3,541.71 | 3,819.15, 3,550.48, 3,531.48 | 4,138.22 ms | 3,550.48 ms |

All passes matched their expected counts. Process handle counts were unchanged from before to after each test, at 256 for 10,000 entries and 257 for one million. At one million the largest process peak working set was 84,000,768 bytes for managed and 83,738,624 bytes for native. Managed and native allocated 56,032,912 and 56,033,280 bytes per pass respectively. Raw results are in `.tmp/filecat-enum-cold-{1,2,3}-{managed,native}.log`. The broader equivalence matrix remains open and the native provider flag remains gated.

## Cancellation at a published batch

The Windows 11 x64 VM at medium integrity on 4 KiB NTFS compared 1,302 entries against managed enumeration. Cancellation from the first native batch stopped at 512 unique entries. Cancellation from the last partial batch stopped completion with the expected cancellation status, including when all entries had already reached the sink. The previous implementation returned success for that last case. The same native run passed the long Unicode path and junction/symlink tag fixture, plus empty and missing directory coverage.

All 16 selected listing, metadata and supplementary-value cases passed without skips. The test bundle SHA-256 was `F3D1E340D069EE16C93A8A31FE0306FBCE4E4C4FB4E1BCD3ACF3D3D92E5E5FBB`. The control and passing XML are `native-details-013c81c9-control.xml` and `native-details-013c81c9-final.xml` under `.tmp/filecat-arm64-efs-copy-3adf9280-r1/`.

The [native ARM64 run](https://github.com/benny-cz/FileCat/actions/runs/44494062330) passed the same 16 cases without skips on Windows 11 Enterprise build 26200, medium integrity and 4 KiB NTFS. It covered native and managed listing parity, terminal and midstream cancellation, empty/missing directories, long Unicode paths and link tags, held hard-link and allocation details, and the selected-value protection checks. `native-file-details.trx` identifies each case. The source commit was `146c1cd906594f1e2aed35f45857b6772981eb93` and the test assembly SHA-256 was `79C1C928533E8E9293A14E4B988A549228A1D96FE2501FC40BCAD8ABCBF5E296`. The scale timings above were measured on x64.
