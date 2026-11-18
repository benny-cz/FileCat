# Development package qualification

The package sources were pinned to commit 764f878d. The Mac and Ubuntu builds ran on the test machines using the checked-in packaging scripts and a verified sparse source checkout. Windows used the same package recipe with serial MSBuild. The Windows artifacts are unsigned development packages. The trusted signed installer remains a separate release gate.

| Platform | Package startup checks | Standard native desktop workflow |
|---|---|---|
| Mac | App ZIP extracted, strict ad-hoc signature verified, packaged executable returned the expected version | Passed using the verified app-bundle assemblies |
| Ubuntu VM | Self-contained tar, extracted Debian package and AppImage each returned the expected version | Passed using the verified Debian package assemblies |
| Windows VM | Portable and framework-dependent executables returned the expected version | Both passed Standard with their package-specific Fast NTFS explanation |

The native harness keeps the original archives and extracted package files available for verification. It loads a separate copy of the packaged assemblies, verifies the application hash, runs Standard in both memory and recorded-result modes, opens the capacity and resource dialogs, and checks the captured folder exclusion and save workflow. The workflows on all three test machines preserved their source fixtures and exited normally. The Mac also captured its own window through the native Retina compositor.

The Windows r1593 desktop harness used the installed .NET host and loaded the exact application assemblies from both ZIPs. The initial app-host harness launch stalled before publishing its startup report. VMware briefly lost contact while collecting r1593, and the completed guest archive was recovered intact. It contains successful desktop results, normal closure and empty process inventories for both package variants.

The merged renderer suite discovered 989 cases on each platform. The Mac and Ubuntu VM each passed 982 with seven expected skips. The Windows VM passed 981 with eight expected skips. All three had zero failures and errors. The skips cover platform-specific and separately scheduled opt-in cases. On the Mac a second collection retrieved all 16 validated receipts after an unmatched shell glob interrupted the first collection. Windows validated all 40 groups from the r1598 checkpointed run. Its final archive SHA-256 is `d5d6556746188530ab4d0cabbde1c56f27045aacc3fe8c0387e75746187f2631`. The final Windows process inventory was empty.

[Build, package and receipt hashes](source-manifest-r1582-development-packages.json) identify the checked results. Signed installer qualification and the full independent review remain open.

On the Ubuntu VM, r1601 ran Orca with an isolated preferences directory, accessibility bus and speech dispatcher socket. Orca announced “Close limits push button” while the setup dialog remained available. The indexed resource dialog exposed its title, reservation text and focused Close button through AT-SPI. Native Return and Escape closed the dialogs and restored focus. The fixture was preserved and the app, Orca and speech dispatcher exited. The prior driver checked buffered speech output while Orca still held it open. The successful driver checked the saved log after shutdown. [Orca command-line options](https://help.gnome.org/orca/introduction.html).

On the Mac, r1605 used the operating system accessibility API to inspect the owned indexed Resource limits dialog. The title and reservation amount were exposed, and the enabled Close limits button held native focus. The native compositor captured the dialog at 2× scale. The app exited normally, the fixture was unchanged and the process inventory was empty. The result JSON SHA-256 is `2a43aae72fe2e21bb854c71e956a52f21720c5f27a159342684076a623f04a61`. The toolchain selected the installed macOS 15.2 SDK for the probe because the default SDK's framework stubs could not be read by the installed linker. VoiceOver speech and physical keyboard input on the Mac remain open.

On the Windows VM, r1614 ran the desktop harness on the branch synced through `ef79336b` with a signed NVDA 2026.3.1 executable and a fresh reader profile. NVDA announced “Close limits, button.” Windows UI Automation confirmed the enabled button held focus in the owned process. Native Return and Escape dismissed the indexed resource dialog and restored focus to its launcher. The eight-theme compact run preserved its fixture and exited normally with an empty FileCat and NVDA process inventory. The tested application SHA-256 is `16eea8d35c2ba301ec8e09dbee085bd913facbaeb06ed3dbe4a73707a29a336b`. The result JSON SHA-256 is `625f57ed1171ac10b28caa01486290fb0af5e6a28c70fa2846dafd86e060f0bc`.

## Warm Tools entry timing

The r1627 native desktop harness called the Tools entry in an already-running FileCat process. It timed each open until the setup contained a laid-out, enabled Start analysis button, then closed the window before the next open. Each platform recorded one first open and 20 subsequent opens. The Windows VM reused the verified r1614 application. Mac and Ubuntu used the verified development package assemblies above.

| Platform | First open | Warm p95 | Result JSON SHA-256 |
|---|---:|---:|---|
| Windows VM | 185 ms | 171 ms | `5b1f9f3f15f5fb16e274c87f59072a78f95b257316b091841d59c74b58bd4294` |
| Mac | 68 ms | 64 ms | `dbe37f223910191356e0159e3624be844eaca7dfbb69b32809c741bc38cabd37` |
| Ubuntu VM | 53 ms | 73 ms | `dd7eccaa5607628a64f528685188204edc3f6f1edcf7c9e3c95e01d09d59115c` |

All three measured warm p95 values met the proposed 500 ms target for setup readiness. The source fixtures stayed intact and the test processes exited. Cold process startup and physical input-to-paint latency remain separate measurements.

The same native folder workflow measured 160 ms from Start analysis to the first visible, laid-out result row on the Mac in r1643 and 550 ms on Ubuntu in r1641. The harness invoked the command through its native window's Start analysis control and checked the rendered list. The fixtures remained intact and the process inventories were empty. The raw result hashes are `172336172d6a44caf8878a06088849c90fd7a56db883f3c31c8e41166fd28ee2` and `6c6d0a37743a4d02f8b4e936832d8548a669d5f18eff730164d5108ba746c5bd`. The measured Windows first visible result remains open.

The Mac r1643 desktop run also posted Return and Escape through the owned AppKit application queue. FileCat received both events, dismissed the indexed resource dialog and restored focus to its launcher. The external Quartz attempts in r1636 and r1638 posted Return while the dialog held native focus. The r1638 observer recorded zero matching events in the app. This qualifies the AppKit key-handling path. Physical keyboard and VoiceOver speech remain separate checks.

The Mac r1644 rerun used macOS System Events to send Return and Escape from outside FileCat. Before each key the enabled Close limits control held native accessibility focus and the test process owned the foreground window. The application received both keys, closed the indexed dialog and restored its launcher's focus. The native compositor captured the eight theme states. The source remained unchanged and the process inventory was empty. The raw result SHA-256 is `3f80e7b71ee70d3ed204dea555b7e31e770e7376ea2f03c7ed40007f57677df9`. This run used operating system keyboard automation. VoiceOver speech remains a separate check.

The Mac r1649 setup dialog exposed its title and enabled, focused Close limits button through native accessibility. VoiceOver's built-in caption panel displayed “Close limits, button.” The test cropped the sole visible caption-sized window owned by the VoiceOver process, and the caption was verified directly from the image after the automatic text reader timed out. The app exited normally with its source unchanged, VoiceOver returned to its original off state, and the application process inventory was empty. The raw result SHA-256 is `3f8a994211ecabe464b997ffcbb88befc1194b4be02210bea1d3367ed3610119`. [Native caption](mac-voiceover-resource-limits-r1649.png) · [Apple's caption panel documentation](https://support.apple.com/guide/voiceover/use-the-braille-and-caption-panel-unac078/mac)

### Process-start and chart timing

Ubuntu r1658 and r1659 launched fresh FileCat processes with private profiles and used X11 input to open Tools → Disk space. Native captures show the enabled Start analysis control and the intended fixture. The disk space window mapped at 2,267 ms and 2,095 ms after launch, respectively. The operating system's filesystem cache was left in its normal state. The driver's window-close wait timed out in both runs, and it terminated only its own FileCat processes. The source fixtures stayed unchanged and both process inventories were empty. The startup samples are usable as observations. The normal-close check and Windows and Mac process-to-setup baselines remain open.

The r1662 performance probe used both product charts with 700 equal-size entries in a 1,100 × 700 DIP window. Each percentile is calculated from 30 samples. Focus updates retain the layout. Data updates replace the recorded view and recompute geometry. The measurements include the control update and synchronous bitmap rendering at the measured display scale.

| Platform and display | Treemap focus p95 | Treemap layout p95 | Sunburst focus p95 | Sunburst layout p95 | Raw result SHA-256 |
|---|---:|---:|---:|---:|---|
| Mac, native 2× | 4.49 ms | 5.19 ms | 15.99 ms | 15.52 ms | `28095c81fb04fc96cce34d74a37c6a71fc669aedd9aace23de55f5c979b8a699` |
| Ubuntu VM, private X11 1× | 19.65 ms | 13.79 ms | 29.81 ms | 26.28 ms | `51470b41a33e7ed14ca81c4a1780e86227e7d0cc8a3fb446a19d725ad7dc99f0` |

Both runs met the 33 ms frame-work target for this fixture. Their source fixtures stayed intact and all test windows closed. The Mac run also captured the actual native window for the broader folder workflow. The Ubuntu VM used a private Xvfb display while its regular desktop was locked. Physical input-to-paint latency, other chart complexities and the Windows chart run remain separate measurements.
