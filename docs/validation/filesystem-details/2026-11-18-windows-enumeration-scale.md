# Windows NTFS listing performance

The Windows 11 build 26340 x64 VM enumerated owned directories containing 10,000 and 1,000,000 empty files on fixed NTFS. The actual Windows provider ran with its native enumeration flag off and on. Each pass counted the expected entries. Four paired warm samples followed the first pair, with the order alternating.

| Entries | Managed warm median | Native warm median | Difference |
| ---: | ---: | ---: | ---: |
| 10,000 | 8.58 ms | 8.79 ms | +2.4% |
| 1,000,000 | 767.69 ms | 768.38 ms | +0.1% |

The measured allocations for one million entries were approximately 56 MB per pass in both paths. The separate 1,302-entry parity fixture passed on both runs and compared names, flags, attributes, sizes and times. It also checked native batching and pre-cancellation. Results are in `.tmp/filecat-enum-scale-10000-tests.xml` and `.tmp/filecat-enum-scale-1000000-tests.xml`.

The initial managed and native results were 72.60 and 9.57 ms at 10,000, and 5,275.58 and 812.22 ms at one million. The native pass followed the managed pass in the same VM session and benefited from any cache it populated. Independent cold runs, startup and peak resource measurements, and the broader equivalence matrix remain open. The native provider flag remains gated.
