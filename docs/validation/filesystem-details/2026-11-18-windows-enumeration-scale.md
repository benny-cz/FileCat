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

## Native ARM64 listing performance

Windows 11 Enterprise build 26200 on the hosted native ARM64 runner measured managed and native enumeration at medium integrity on fixed 4 KiB NTFS. The fixture wrote 10,000 and 1,000,000 empty ordinary files with identical name lengths. Every pass returned exactly the expected count. Each size ran in a separate test process and used four alternating warm pairs after its first pair. The first pair followed fixture creation on the same runner. [Benchmark run and TRX results](https://github.com/benny-cz/FileCat/actions/runs/44508217990)

| Entries | Managed warm median | Native warm median | Native timing difference |
| ---: | ---: | ---: | ---: |
| 10,000 | 4.93 ms | 4.17 ms | −15.4% |
| 1,000,000 | 378.75 ms | 393.92 ms | +4.0% |

The first managed and native passes took 17.56 and 11.18 ms at 10,000 entries and 458.45 and 406.76 ms at one million. Every pass ended with the same process handle count it started with. At one million entries, warm managed and native allocations were approximately 48.03 MB per pass. The highest process working set in the one-million-entry test was 53,817,344 bytes. Both exact test cases executed and passed. The source commit was `11822f0ecd37303007b6cb7c1ba7809f2baa40b9` and the Windows platform test assembly SHA-256 was `EED3DAA7AD27D09AFB3D3C7E5ECD5109B6ACAF55588F96C430CBA13077942A6C`. Independent ARM64 cold-cache measurements remain open.

The native ARM64 rerun after the find API refactor also returned every entry. Warm medians were 4.87 ms managed and 4.13 ms native at 10,000 entries, and 372.84 ms managed and 388.54 ms native at one million. Process handles matched before and after each pass. At one million entries, the peak working set was 53,821,440 bytes and the two paths allocated approximately 48.03 MB per warm pass. The platform test assembly SHA-256 was `21C3ABF378E8B48C611B6D539E9B3A03011E0DB815100C67EF1780958DEDA475` at commit `e0274701743e850f1c0c53db88aeea22e498584e`. [Native ARM64 rerun and TRX results](https://github.com/benny-cz/FileCat/actions/runs/44515650631)

## Unsupported API and partial failures

Eight deterministic tests on the Ubuntu VM supplied Windows enumeration statuses at the find API boundary and drove the actual listing implementation. Initial errors 50 and 87 tried the same native API without large-fetch. A successful retry retained the native tag. When open remained unsupported, the managed provider returned the sole file in the fixture with an unknown tag. Initial error 120 went directly to the managed provider. Each invalid cursor was closed.

The same three unsupported statuses from FindNext after 515 native records each published exactly two batches of 512 and 3 entries, returned the error and released the cursor. No managed entries appeared. These checks used injected statuses. The Windows native parity and throughput measurements above exercised real NTFS.

After the find API refactor, the Windows 11 x64 VM (build 26340, medium integrity, fixed 4 KiB NTFS) passed all 12 selected tests. This included the real 1,302-entry parity and cancellation case, Unicode and link tags, empty and missing directories, and the eight injected error cases. A separate 10,000-entry measurement returned the complete count on all ten passes. The four warm pairs produced a managed median of 7.83 ms and a native median of 7.13 ms. The handle count matched before and after each pass. Peak working set was 81,502,208 bytes. The test assembly SHA-256 was `C52F0EA58B16FF9DCF2C74004FF3FD1E95C73E5456A3BBD6F3CE952843336862`. The two XML receipts are `.tmp/enumeration-fallback-x64-r2-enumeration.xml` and `.tmp/enumeration-fallback-x64-r2-scale.xml`.

## Reparse tag hints in results and working sets

A captured tag follows an in-memory result reference through the result provider and into a working set while its existing metadata refresh agrees on attributes, kind, size and modification time. The tag remains a presentation hint. The refresh uses its existing read, and refreshed metadata disagreements or an unavailable original yield an unknown tag. The result retains its original provider provenance. Reopening the working set from its saved references also yields an unknown tag. The Ubuntu result, working-set and reparse regression suite covered the session round trip, disappearance, metadata changes and reopen.

Search refinement applies the same rule to its refreshed local match. The Ubuntu fixture retained the captured tag and relative folder for a symbolic link, then cleared the tag when a plain file replaced the link at the same name. The replacement was given the captured size and modification time. The target bytes stayed intact. All 29 focused Core cases passed, including this refinement fixture.

The forced-spill test kept the 48-byte records, modified a tag through Update, hid one row and returned the updated tag through the sorted and filtered mapped view. The suite also exercised in-memory sort parity and provider payload ownership. All 34 focused Store, presentation, working-set and refinement cases passed on the Ubuntu VM. The native Windows x64 and ARM64 parity tests verified the 56-byte managed entry layout.
