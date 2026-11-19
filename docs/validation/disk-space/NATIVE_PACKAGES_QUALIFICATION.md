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

Ubuntu r1658 and r1659 launched fresh FileCat processes with private profiles and used X11 input to open Tools → Disk space. Native captures show the enabled Start analysis control and the intended fixture. The disk space window mapped at 2,267 ms and 2,095 ms after launch, respectively. The operating system's filesystem cache was left in its normal state. The driver's window-close wait timed out in both runs, and it terminated only its own FileCat processes. The source fixtures stayed unchanged and both process inventories were empty. The startup samples are usable as observations. The Ubuntu driver still needs a normal-close check, and the Windows process-to-setup measurement remains open.

The r1662 performance probe used both product charts with 700 equal-size entries in a 1,100 × 700 DIP window. Each percentile is calculated from 30 samples. Focus updates retain the layout. Data updates replace the recorded view and recompute geometry. The measurements include the control update and synchronous bitmap rendering at the measured display scale.

| Platform and display | Treemap focus p95 | Treemap layout p95 | Sunburst focus p95 | Sunburst layout p95 | Raw result SHA-256 |
|---|---:|---:|---:|---:|---|
| Mac, native 2× | 4.49 ms | 5.19 ms | 15.99 ms | 15.52 ms | `28095c81fb04fc96cce34d74a37c6a71fc669aedd9aace23de55f5c979b8a699` |
| Ubuntu VM, private X11 1× | 19.65 ms | 13.79 ms | 29.81 ms | 26.28 ms | `51470b41a33e7ed14ca81c4a1780e86227e7d0cc8a3fb446a19d725ad7dc99f0` |

Both runs met the 33 ms frame-work target for this fixture. Their source fixtures stayed intact and all test windows closed. The Mac run also captured the actual native window for the broader folder workflow. The Ubuntu VM used a private Xvfb display while its regular desktop was locked. Physical input-to-paint latency, other chart complexities and the Windows chart run remain separate measurements.

The r1667 live indexed deferred-selection runs reached an active scan of a 50,000-file fixture. The guest keyboard delivered Space to the focused Pause button. From the application's native key event to the laid-out Resume control, the Mac measured 16.08 ms via System Events and Ubuntu measured 14.03 ms in a private X11 display. Each run then completed the scan, reviewed all 50,000 matching observations and closed its owned instance with its source unchanged. These single samples do not give the 95th percentile or establish hardware-input-to-compositor latency. The result hashes are `64147b30b2b5fae84ddcb7f02db8b4f2bcee37e53b3a690862158e02512fe78f` and `732826d716fb2fd670e81b8949c42f827ad32cbccb1d79e043aeab387e508724`, respectively.

The r1674 Ubuntu run sent 42 native X11 Space events during a real 200,000-file indexed scan. It recorded 21 Pause and 20 Resume samples. Pause p95 was 59.46 ms and Resume p95 was 42.48 ms. The final Stop key reached the laid-out Stopped status in 21.84 ms. The overlay captured that state, the source stayed intact and the process inventory was empty. These are application key-event-to-layout measurements from a private X11 display. The earlier 50,000-file series reached the end of its scan before all samples could be taken on either Unix platform. The raw r1674 result SHA-256 is `52e34ccb6189c1b9df34be4d40857f10cb6c508be9fb9331ec2c0df8d5106983`.

The Mac r1674 run completed the same 42-key sequence and 200,000-file fixture through System Events on its regular native desktop. Pause p95 was 10.15 ms, Resume p95 was 4.86 ms, and Stop reached the laid-out Stopped status in 5.54 ms. The app exited with its source unchanged and its process inventory empty. The raw result SHA-256 is `bdb8ef17dda72dee19b253bba1a77607ffad9d257521951c30ae073e3955bdce`.

The Mac r1680 hierarchy fixture supplied 4,096 file observations across eight folders at the same 1,100 × 700 DIP size and 2× scale. The charts applied their normal visible-area grouping rules. Treemap focus and revised-layout p95 were 12.04 ms and 14.16 ms. Sunburst measured 2.17 ms and 4.27 ms. Both met the 33 ms frame-work target for this fixture. The app exited and the source was unchanged. The raw result SHA-256 is `bcb02c1ce6c69a9a9d7dfa147c371319c874eaf7eaad7e636f2b4da77337a403`.

### Merged regression run

Revision r1676 used one verified compact overlay of the merged build on each Unix test machine. Each ran the analysis Core selection and full Renderer suite, saving each suite's result independently. Core passed 2,034 of 2,038 cases on each machine, with four expected skips. Rendering passed 982 of 992 on both, with ten identical expected skips covering platform-only and opt-in measurements. All failure and error counts were zero. Both terminal process inventories were empty. The Windows repeat remains queued behind its large native fixture.

| Platform | Core XML SHA-256 | Renderer XML SHA-256 |
|---|---|---|
| Mac | `beb6e935328617dce4a0fb4ee9c1c5a7bf36adb48c48708af0e66775d7091565` | `86fc5246c09fd0def773af4dbeaca319ef5587a8c5015ca4ba72e8eab143afab` |
| Ubuntu VM | `6dfc263dcf6f2f6e464bdc4eca12deb9b92781853af453adae1e68c423b93549` | `18e9167618643eee35ffca62b43749828a6fe95f4f54dd1edb5a982b6ce3f39b` |

Mac r1686 opened and closed FileCat normally in three fresh package processes. Native command search and Return opened Disk space, and the operating system exposed the enabled Start analysis button with laid-out bounds. The three process-to-setup samples were 2,448 ms, 2,038 ms and 2,055 ms. The fixture used ordinary filesystem caches and disabled automatic update checks in its private profiles to keep the startup update notice out of the measurement. All three instances closed normally and the source remained unchanged. The raw result SHA-256 is `d67e1f5c0c3c5dec7b1d70c71b0dc7bfda0a89b66af81cfc376913f9105282fd`.

Ubuntu r1683 measured the flat and hierarchy fixtures in one native application session. At 1× scale in its private X11 display, the 4,096-observation Treemap exceeded the 33 ms target with 38.26 ms focus p95 and 39.95 ms revised-layout p95. Sunburst on the same hierarchy measured 2.75 ms and 5.33 ms. Both chart fixtures completed with an unchanged source and an empty process inventory. The performance fix is being qualified separately. The baseline result SHA-256 is `262b83c1e842d600b2fdd05335b2947ed99cd30215eabb250a8903e0198d4ef0`.

The chart renderer now computes the focused item's ancestors once per frame and reuses immutable fills and borders for the active theme. The eight-step ancestry boundary and border selection for focus, ancestors, marks and dashed entries are preserved. Revision r1687 measured the original dense Ubuntu Treemap at 37.41 ms focus p95 and 39.90 ms revised-layout p95 on the merged product overlay. The ancestry change alone measured 36.65 ms and 40.41 ms. The combined fix in r1690 measured 19.91 ms and 25.53 ms. All four Ubuntu workload and chart combinations met the 33 ms target in that run.

The same fixed assembly on the Mac measured 10.89 ms and 10.37 ms for the dense Treemap. All four Mac combinations passed, and all eight focused renderer checks passed across themes, chart modes, grouping and input. Both guests closed their application instances with source fixtures unchanged. The matching Ubuntu focused renderer repeat also passed all eight checks. The Windows repeat remains queued.

| Verification | Result SHA-256 |
|---|---|
| Ubuntu r1687 before and ancestry-only comparison | `6a7a99f606a2fa0b266cc282ac63a8cd1f5638db635f7f7c744cdbeb69305c77` |
| Ubuntu r1690 combined fix | `646925b802321b9067a9fe3b52014e503998ec72d9fe8f27c079ce5162cac8d1` |
| Mac r1690 combined fix | `d450394ed16c8ec48c14acd50e44885289570d44da9b65de70c1d5758880f693` |
| Mac r1692 focused renderer XML | `74ff888d8181260ca6d976d5b2461ee492fdcac962cbd00a7522fe39a7aa9072` |
| Ubuntu r1692 focused renderer XML | `fb1dfae9a8b47db20bdb4c6338e78da85a44f2f334f1216fd020c2eb3741ba72` |

### Native window transitions

On the Mac in r1700, the 2× Disk space setup window expanded from 380 × 430 DIP to 1,440 × 766 DIP and restored to 380 × 430 DIP. The enabled Start analysis button fit inside the window in each state. Native compositor captures recorded the maximized and restored windows. The rest of the folder and chart workflow completed, the source stayed unchanged and the app exited. The raw result SHA-256 is `75bfbbf4cc67dda0e905089ff55ad3d447b1b1b77bac5e192d06525b166b1252`.

Revision r1709 checked the latest merge of main `2d1f12a2`. The full solution built with zero warnings and errors. The Mac and Ubuntu guests ran the AnalysisScanningTests, FileDetailsTests and UnixFileDetailsNativeTests slices. The Mac passed 86 of 95 cases and Ubuntu passed 89 of 95. Platform-specific tests and tests requiring prepared native filesystem fixtures account for the remaining cases. Both had zero failures and errors. Raw XML SHA-256 values are `d7d0afd051a26414a719bea7dfba17a7cd8dbc4c4f586b2600bb4bebb5654843` for the Mac and `aa07ad9c6f2026bab35cd755af214bfd9a66d96915fc8f9642b1e19386383132` for Ubuntu.

The Ubuntu VM's r1714 private X11 session used Openbox to exercise actual window-manager transitions. At 1×, the setup went from 380 × 430 DIP to 1,600 × 979 DIP and restored to 380 × 430 DIP. Start analysis fit throughout. The native application closed with its source unchanged after the complete layout and chart workflow. The layout receipt SHA-256 is `0ab30741d38fdc7bdf8617fce85c8cf59ba3201e5bec5cd60e7b19e3d55bea86`. The same session also observed one fresh process reach the Tools setup at 2,565 ms and exit normally. A later sample invoked Tools before its initial location loaded, so the startup series needs its corrected readiness check.

Ubuntu r1721 passed three fresh launches of the verified Debian package in a private Openbox session. Native Tools input opened Disk space at 2,486 ms, 2,910 ms and 2,583 ms after launch. Each capture showed the setup, the fixture was unchanged and all test instances exited normally. An earlier attempt sent the command while the workspace still showed its startup panel. The successful run waited for the rendered fixture listing and the owned Tools popup. The receipt SHA-256 is `044fc34dfec1593e788943de5fa9ecc508c87b5f8b1e63fd6972188f631788d8`. The OS filesystem cache retained its normal state.

After merging the new Back and Forward menus, r1719 verified that temporary analysis listings stay out of both directions of navigation history, persisted folder history and bookmark slots. It also checked that leaving a listing releases its provider lease and that a previously saved bookmark is preserved. The Mac and Ubuntu each passed all eight cases, including held-source panel reveal and changed-source refusals, with empty process inventories. The original collection scripts compared bare XML method names with fully qualified selectors. A separate check recombined the XML type and method attributes and verified all eight exact case identities and outcomes on each platform. XML SHA-256 values are `cb99b5a428a7490daf66ab588c79bdff25ad9aaca4dad6f82f780a935017a67d` for the Mac and `9d8943e98e74c641fa5a3989c335983ce915b64ff1801d356b68038c91a66900` for Ubuntu.

Mac and Ubuntu r1738 each passed all 26 cases covering panel navigation, native reveal, keeping files during cleanup review, the two chart modes' shared references, and the distinction between missing allocation and partial traversal. Both used application SHA-256 `90eb133b6c29dc868c66780270ddf9460d090bb603073ed1fc06cf227cc53bdb` and verified empty process inventories. XML SHA-256 values are `681bd2f597836c41c66051466cd085785d889410213093ead36a9cd00c783912` for the Mac and `1e4b924e0d86e362567dec9a6919ba32d23ba35552524a96ba1422741a0462ab` for Ubuntu. The same build produced the native Mac [comprehension screens](UX_COMPREHENSION_STUDY.md).

The post-merge r1748 batch used source `4b685da4` and application SHA-256 `3e63bc8d8659401274e4f10dd242f4642d2a403b57054e741030f9c07962824f`. The Mac passed 2,066 of 2,080 Core analysis and details cases, including the real APFS clone-sharing fixture. Ubuntu passed 2,068 of the same 2,080 cases. The remaining cases were platform-specific or opt-in. Both passed all 14 navigation cases and 983 of 993 Renderer cases, with 10 expected Renderer skips and empty process inventories. All three suites had zero failures and errors on both machines.

| Platform | Core XML SHA-256 | Navigation XML SHA-256 | Renderer XML SHA-256 |
|---|---|---|---|
| Mac | `022f785eb2370a6377a81e181ce0a9fa0cab9494a4dfb8166005e35ab9818fbf` | `3546ad584bfe7e4051046c130801c13bbe09ec1ae7bd4829805078a3c5d05cbc` | `96533cfff47c0b71e3c56b1fcf730edab1a9e94b5f8211c77026d6bd3b65ce56` |
| Ubuntu VM | `dbf09fad6c2b6142371def3f19a0f135f7098b9e5d82696c31b4f8248e8907f0` | `a8fa2f8d0855cc4d2577997ae3daa0d79b1a853c13cddde98b99484172018b4e` | `7d8a5bf1bd3ae77e17a40cb0e6d84a3948e0d21a440f3b5a3a0a35db3102fb5b` |

Windows r1746 measured 808.05 ms from Start analysis to the first laid-out result. At 1× display scale, setup maximized from 380 × 430 DIP to 1,004 × 519 DIP and returned to 380 × 430 DIP. The setup action fit throughout. The complete folder workflow exercised the eight themes, left its fixture intact and exited normally. The folder receipt SHA-256 is `aa6b086f157a45edaf7df8047a8e15a51fef01bd2a2022e4cfcf8b155dd0c258`.

| Windows chart workload | Focus p95 | Revised-layout p95 |
|---|---:|---:|
| Treemap, 700 entries | 5.02 ms | 5.92 ms |
| Sunburst, 700 entries | 1.23 ms | 1.87 ms |
| Treemap, 4,096 entries | 24.07 ms | 27.04 ms |
| Sunburst, 4,096 entries | 1.60 ms | 3.00 ms |

The same Windows batch sent native input to 21 Pause, 20 Resume and one Stop actions during a 200,000-file indexed scan. Key-event-to-layout p95 was 51.15 ms for Pause and 34.03 ms for Resume. Stop reached the laid-out status in 43.66 ms. The driver collected the completed timing series and Stopped frame, then exceeded its 65-second wait for exit and terminated its owned process. A focused follow-up records shutdown and source-verification phases. The feedback receipt SHA-256 is `e6c2f56dbbd946f3e280a6b3ff96cf2f907d2c97d81d81f980ab8465c54928cf`.
