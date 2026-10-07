# E-I171 — closed result consumers remain rooted and receive late completion

**Remediated preliminarily in f502fd169478d0631ea4280adbb5b3f6457cda68.** Closing a result tab now unsubscribes its result-change callback and ends its completion watcher. A live tab still refreshes and receives completion; closing a consumer does not cancel the shared producer.

## Proved lifetime and correction

`MainViewModel.OpenResultSet` originally subscribed an anonymous callback to `ResultSet.Changed` without unsubscribing when the tab closed. The callback captured the tab. The provider deliberately keeps result sets for the app session, so its live publisher kept a disposed consumer reachable. When a producer was still running, the completion watcher also continued polling and later wrote the closed tab's banner.

Four durable App controls use actual MainViewModel/PanelViewModel/TabViewModel composition with isolated temporary application state and synthetic result identities. The two weak-reference controls close the tab through the actual panel method, wait for existing finite callbacks, force collection and positively retain the publisher, provider and main view model. They cover both completed and still-running producers. A third control retains the disposed tab and checks that later result/completion signals do not alter its banner. The live-consumer control checks listing refresh and completion. Every control checks that the producer remains uncanceled.

On the canonical 22c263d baseline with only these test controls added, both weak-reference controls and the late-banner control fail; the live-consumer control passes. The correction keeps a named change callback, unsubscribes on the tab's existing Closed event and checks the consumer lifetime before refresh or completion publication. It does not change result-set/history retention policy.

## Revalidation and exact provenance

The first focused working run passes all four controls. The corrected affected-suite filter and a fresh committed-source run each pass all 22 App cases without skips: four lifetime controls, three Find comparison controls, ten Find-window workflows, four archive-result cases and one working-set workflow. Component input simulation is headless; these are not native desktop or participant observations.

The clean locked SDK 10.0.401 build exports all 1,065 canonical Git blobs from f502fd1. Source archive bytes, Git blob SHA-1/modes and before/after source pins reconcile. Actual clean `FileCat.dll` SHA-256: 494f8bd527a249d8f181aed894b7d7977f200bbca7760f89557119871d0e8d46. The independent seal verifies 41 retained files and 564 actual test-payload files across the four executed runs. These are development test payloads, not release artifacts.

The first test preflight failed on a missing namespace import before execution; the second failed when C: ran out of space copying native debug files. Both source exports, receipts, diagnostics and the one compiler-generated coverage mapping remain. Five byte-identical debug copies from these unexecuted builds were replaced by same-volume hardlinks to their verified warm-cache originals, reclaiming about 404 MiB while preserving every original path and byte. All 2,135 pinned source/diagnostic files are unchanged. New builds use the SDK's explicit hardlink-copy properties, recorded in each command; actual payload hashes remain independently checked.

The focused working filter initially named non-existent adjacent App classes; it executed exactly the four new controls, and is recorded only as that focused pass. The corrected filter independently runs all 22 real affected cases. The first seal rejected the working module's CRLF bytes against canonical Git LF bytes. Both actual hashes remain recorded; the successful seal independently proves that the only difference is Git's line-ending conversion. No test result or input is rewritten.

Original [CI 37551264824 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37551264824) at f502fd1 is pending at this local seal. No four-platform completion is claimed yet. Broader I06 consumer/worker/frame and materialized-memory scope, native interaction and exact-candidate qualification remain open. No physical source, persistent machine setting, frozen contract, candidate or stable publication changed.

Private `FileCatReleaseEvidence/result-consumer-lifetime-20261007-v1`:

| Path | SHA-256 |
|---|---|
| baseline-v3/command.json | 18379778050ae45ee6ddf1c582055154cc0b911af4ed56e10dcc61e8d018bda1 |
| baseline-v3/results/baseline.trx | 41b1089a8ce348417b23dca0fef07de48e87e3304c9ea93b5c528d04b6e58893 |
| fixed-overlay-v4/command.json | a2dcd64829953c8ef39c258712d7fbf4a0fe3a72907d6cf47d32f318a0a12e74 |
| fixed-overlay-v4/results/fixed.trx | a5374e3f236b074b9f5cbcb377f8af6d0d6faf89e0cab2ff33d5ff1c8a646c41 |
| fixed-suites-v5/command.json | d4ae7e6c584eb766e73156c8f152ebc304b61231ad7e0414173c54a94d87a216 |
| fixed-suites-v5/results/fixed.trx | d15fd4ab0c3470708e1b2fadf455534e5059067a774ac92a7dbde37cdaf9b253 |
| clean-committed-v6/command.json | c890ad7caece4c1c18c1b6b19ecfb9df8dc3f08795af4c09f6381ae828a58c20 |
| clean-committed-v6/results/clean.trx | 464f1c259e463944e74c1d1fdd5d21bbbb7988be90c6837b42748a14a125ff8e |
| owned-unexecuted-symbol-deduplication-v2.json | 34239a3de72a12ed540837bf0bc4a673f0adaf86956014924bdaa0c5e325c151 |
| independent-consumers-v8.json | 1a2111e9607ce8853a91f77c6d7a28356416cd8132c53f8a78c1a2a5c8eb670c |
