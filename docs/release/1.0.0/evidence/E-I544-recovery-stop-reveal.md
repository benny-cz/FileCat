# E-I544 — recovery Stop reveal in a small operations drawer

**Preliminary CI/UI remediation accepted at `9c7e2505d5829814b129380c4f2922471428a45b`; exact pushed-source CI and candidate qualification remain open.**

GitHub Actions run [40729054660](https://github.com/benny-cz/FileCat/actions/runs/40729054660) at `42eb82d0677a02fb82f1b7178e63406275062a7f` passed producer policy, Windows x64, Windows ARM64 and macOS, but failed Ubuntu. The Ubuntu App TRX is 5,989,177 bytes, SHA-256 `b0fd08531314c268887e4a4154166efd070f0bbe81bb61014452364f31bd9f01`, with **2,485 passed, 127 skipped and one failed**. The only failure is `OperationsDrawerLayoutTests.Busy_recovery_reveals_stop_inside_the_bounded_drawer_at_small_size`: after recovery review became busy, only 14 of the 29-DIP Stop target was visible in the 35-DIP recovery viewport.

The existing reveal was queued only at `DispatcherPriority.Loaded`. The correction performs the same guarded `ScrollToEnd` immediately when the caller has already measured the busy row, and repeats it at Loaded priority for the production path that starts and reveals review in one dispatcher turn. It does not change drawer sizing, selection, recovery state or the user's saved drawer height.

Validation:

- Windows x64 focused case: 1/1 passed; TRX SHA-256 `567487fe106e2830ca98c488a2339112b0de7f54609fcb31ff23c78dd2462f85`.
- Windows x64 complete `OperationsDrawerLayoutTests`: 11/11 passed; TRX SHA-256 `f8aa33733a1cc1195dc1067b1a5960acf93694f7ad9342f0f8b7bb1b13489e0b`.
- Ubuntu 26.04.1 exact base `5518f8d8` plus only the final one-file diff: focused 1/1 and complete affected class 11/11 passed under SDK 10.0.101. The diff SHA-256 is `b3ba474aa58bd295a6256f617178f1d1bbeea8b5f11e90d6bcfa092f37b6b313`; final archive SHA-256 is `1aeee64c3ef1d38b4f6cacef369d01eff96920e87e3b04e9f043f0cc36f2f175`.
- Concurrent parent `f58f7d09` does not touch the drawer source or affected test; commit `9c7e2505` contains only the ten-addition/four-deletion reveal correction.

The failed hosted run remains failed. Run [40739695900](https://github.com/benny-cz/FileCat/actions/runs/40739695900) is the first exact pushed-source run for the correction and was queued when this record was written; it is not counted as passing. Installed native interaction, a frozen candidate and final qualification remain separate.
