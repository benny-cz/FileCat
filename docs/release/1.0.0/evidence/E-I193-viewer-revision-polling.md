# E-I193 — viewer revision polling on the device queue

Recorded 2026-10-07. **Preliminary remediation; I06 and native/network/candidate qualification remain Open.** Original producer 4734e2e3e4631834ed05494be0911b0ac39b8446; correction c073192d26b063e0086e6740b9ad1010ab4175c1; parent/discovery abbf437c1ea9e93d4587f900b4acc0fdd5cc2ad6.

The viewer's periodic change check calls source revision and length synchronously on the UI thread. A stalled metadata call therefore stops queued UI work, including close; an IOException escapes the timer callback. Eight controls reproduce this with actual FileContentSource, PagedReader, ViewerWindow and AppServices I/O: hold revision or length on an owned 29-byte file, keep the viewer open or close it, and return normally or throw. Every original control blocks the UI marker until the controller's three-second safety release; all four injected errors escape the caller. The safety bound is not a reference-performance benchmark.

The correction queues the poll at background priority on the viewer's shared device queue, coalesces checks while one is active, catches supported metadata errors into visible status, and suppresses completion after close. PagedReader retains its existing Refresh() API and adds a cancellation-aware overload with checks before/between/after metadata calls. Cancellation keeps an active provider call alive until it returns; it stops the next query. Cached-picture and structure-report change warnings remain intact.

| Control, both metadata boundaries | Original | Corrected working and committed source |
|---|---|---|
| Open, normal result | UI work blocks; forced safety release | UI marker runs while call is held; other-device work completes |
| Open, IOException | UI blocks; error escapes timer caller | UI remains available; visible “current state could not be checked” status |
| Close, normal or IOException | Close only executes after forced release | Close executes during held call; no disposal during call or late status update |

All eight original controls fail the supported UI-thread oracle. Working and fresh committed-source builds each pass 76 affected cases without skips, preserving all 68 preceding selected names/outcomes. The twelve existing picture/Info revision controls now await the asynchronous poll; their oracles remain unchanged. Twenty-one paging/cache/scheduler cases also pass and retain preceding names/outcomes.

The independent local reader verifies 1,111 original and 1,113 corrected Git blobs/modes/archive members, all five changed source/test paths, three actual 141-file App payloads and the actual 82-file Core payload (505 references), forty retained local files and 24 raw observations. It reconstructs the owned input/hash, metadata order, actual Dispatcher.UIThread.CheckAccess values, named device threads, another-device completion, all 25 coalesced checks, cancellation boundaries, single deferred source disposal and status after close. Fixtures remove their owned temporary roots; no owned executable remains at sealing.

Original [push CI 37621063540](https://github.com/benny-cz/FileCat/actions/runs/37621063540), attempt 1 at c073192, is pending in the latest exact-head capture. The producer-reference policy already passes. Last sealed original CI is I192 at 4734e2e with 591 App cases per lane. No rerun substitutes for an original result.

These are actual owned-file calls under bounded synthetic metadata holds, observed through the headless dispatcher on the elevated Windows Insider host. They do not qualify native GUI frames, a real unresponsive SMB/removable source, initial viewer-construction metadata, every consumer/worker, same-size/time-restored undetectable changes, reference hardware, human/assistive-technology tests or an installed candidate. Earlier native records retain their exact producers. No VM/Mac setting, physical source/USB, frozen contract, candidate, GO or stable publication changes. Next autonomous work continues remaining Page/source/checksum/worker boundaries.

Private `FileCatReleaseEvidence/rp193-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 19e804d2eadb880535a317f6183038d76f5a4c56583b0b41cde9a653b7f0d218 |
| baseline-v1/results/display.trx | 20515e226afbb9f1d07331dfa5e4a06b6f66a8d30c208f04e083fa0d0a409cb2 |
| working-v2/command.json | 9f72f6c0b10f05ae960448bc58ccfc75bf70fac217671cbc3d1da53fec45d696 |
| working-v2/results/display.trx | ab2cf7e7f811e19437a76873b22ed3d0b463b347f0f42d2404fcb3a36643fb68 |
| clean-v3/command.json | 7d629c222a039cc62ed4e9c6d00f77a4b70889d77b1d5cf4f13c51b1c8bbf48b |
| clean-v3/results/clean.trx | eef80c040048b7c4bea0b2cf23afb1ff214cd714f444941afa375337b039dbbd |
| core-v5/command.json | a472db78a5500b0565950d929dc54d8892189c9a9b4405193f9afe59fe2713c2 |
| core-v5/results/core.trx | 268d71a4b4a0142ad01bfa798d9fc2c0986d6812346eda57bf62e4d280f70272 |
| ViewerRevisionPollingTests-v1.cs | c70b1e7e6039b0936e4c67719ee4c4acfd3725bf4309017727bc18ece9344480 |
| seal-revision-polling-v6.py | 046d5164e1fa820c384ada5b1e3c4f439a6293b2b63b2a8ab5e8a14578939a30 |
| independent-revision-polling-v6.json | ddcdd291f17c80f360870f7884f44005ea77fc82f5dbe1fd7fdd00cf5f014c9d |
| owned-process-absence-v6.json | c084b1e022eab7ad8b2325b4ae93ade31bc24a7183122429f2e44064db0a6518 |
