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
