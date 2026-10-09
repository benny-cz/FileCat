# E-I285 — retire bindings and previews when a panel is removed

2026-10-09. Canonical original **71167b76d8637a4de39cdff4980e02230b016b90**, complete original/fixed 1344-blob exports with only declared overlays. MainWindow removes a dead panel view from its dictionary but leaves its DataContext intact. A QuickView panel's source panel/listing therefore continues to own callbacks into that retired view, and its preview reader/request stay live. The defect also occurs when the view was already hidden by maximizing another panel.

The correction clears the removed view's DataContext before dropping it from the dictionary. The actual existing DataContextChanged path retires panel/tab/source subscriptions and calls QuickView.Attach(null), closing preview ownership. Views only detached for a layout change keep their context and are reused as before. No global cache, picture budget or native policy changes.

Twelve actual headless main-window cases cover visible/already-hidden removals, 5-byte text/65,537-byte binary files and one/four/twelve consecutive removals. Every reader is positively open and reads the complete expected owned bytes first; controls are deliberately held while closure is observed. All **68 original observations** retain source/context bindings; **65** also retain their reader/request and allow WithSource after removal. **Three** readers already closed while their source/context bindings remain; the exact closing trigger is not traced. In the correction every observation has null source/reader/request/context and WithSource throws ObjectDisposedException. The original source panel and tab remain owned; inputs remain byte-identical. Baseline cleanup explicitly clears DataContext only after recording the adverse observation, so repeated controls remain bounded.

Two healthy controls maximize/restore with either source or preview active: the same live control and reader survive, then explicit QuickView toggle closes the reader. Original **12 failures/two passes** become **14 passes**. Full App passes **1369/25 exact skips** without rebuilding the focused payload; all 1380 preceding local logical outcome/message multiplicities and exact skips remain. The prior Core suite applies at its own unchanged backend producer; this UI correction does not claim a new Core replay.

An initial two-case probe retains both raw failures and source/reader/request observations. Its first case incorrectly assumes a removed view has no immediate visual parent; a detached layout grid can still parent that view. The versioned expanded fixture checks actual TopLevel absence, keeping the ownership oracles. Neither that parent assumption nor a mocked exception is presented as a product failure. Exact source/payload/semantic comparisons and owned restoration are in the independent final receipt below.

These checks qualify closure while controls are still held, not garbage-collection timing, total native/process memory, decoded-picture/frame retirement, every removal/race/provider path or native desktop interaction. Exact committed checks are recorded below; original hosted checks pass below; broader I06/reference/human/candidate work remains. Host UI and physical sources are untouched; I106/I110 HOLD and explicit human GO remain in force.


## Selected new immutable receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts preserve raw commands, payloads, failures, skips and owned cleanup.

| File | SHA256 |
|---|---|
| `prepare-i06-panel-probe-20261009-v2.py` | `6188c60aeefce61073153d9fa45ed514fa8ebf1f2cc40b289100f088639626a8` |
| `i06-panel-retirement-20261009-v2/PanelRetirementLifetimeTests.cs` | `9f707f5ee7bb87bd98231792d59cc69edb2410b7e16f49a338f3952411d66193` |
| `i06-panel-retirement-20261009-v2/MainWindow.Layout.cs` | `a0c1be2731afe86bfdc95e4c711a7b068f0b05cc3fe421165ba7d1b070165ef3` |
| `E:/FileCat/artifacts/release-evidence/i06-panel-retirement-20261009-v1/original/app-controls/command.json` | `79f30ff4407e9086943f726b040bc27c192d1e4d3c8e9d8068986aba36703399` |
| `E:/FileCat/artifacts/release-evidence/i06-panel-retirement-20261009-v2/original/app-controls/command.json` | `443b2af112fc84c106eaf643dc8ac230f98051ff5b0edb07b3a6564ac537e8d7` |
| `E:/FileCat/artifacts/release-evidence/i06-panel-retirement-20261009-v2/fixed/app-controls/command.json` | `b3bcc37bdedc0b627dc08c769bfed52801c2684c8e21801390605c9cbb010b0d` |
| `E:/FileCat/artifacts/release-evidence/i06-panel-retirement-20261009-v2/fixed/full-app/command.json` | `afc99bc24947d6ffa477e91c5fafc4f85540bd09423459e3077a823231f3922c` |
| `i06-panel-retirement-20261009-v2/seal-panel-retirement-v2.py` | `9ef645f37283fd0f9baf9fe7991aa93bd303bb5a9503fbe5c87ae80efeb75095` |
| `i06-panel-retirement-20261009-v2/independent-panel-retirement-final-v2.json` | `cb217bfa2cc265db952f6043f80d951c0b5a8e36019e801fdd6636e0da9e7b8b` |


## Exact committed follow-up

No-overlay **1fefd5e269c52206c3be4d6475fdd0584dc1f4be** verifies all 1347 canonical Git blobs and passes all fourteen controls. All private/exact outcomes, messages and semantic observations agree; all 68 held-control retirements pass. Ten owned temporary files are archived/rechecked, one removed and nine compiler locks retained in the exact owned root. This does not repeat the full App suite or qualify native picture/frame/process memory.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i285-committed-20261009-v1/seal-exact-v1.py` | `9fb889d49b86ec897ad3ba630c1c10ed0b68868a1f5ddb3a49d4bfc0fa833c52` |
| `i285-committed-20261009-v1/independent-exact-final-v1.json` | `26791592611ebd1a3b390de11093b0de65ffe1c25a1de35f6ea1effa68f6e5f2` |
| `E:/FileCat/artifacts/release-evidence/i285-committed-20261009-v1/committed/app-controls/command.json` | `4db985116d454129a44b7a26a5f0240fe8ac107f19a630b77f475897d432d16d` |
| `E:/FileCat/artifacts/release-evidence/i285-committed-20261009-v1/committed/inputs.json` | `3053adf5afc273bc717a31d92e23effe74e8e9c07e1437f88234cec4fbf227c5` |


## Original hosted follow-up

[37979617822 attempt 1](E-CI-panel-retirement.md) at exact 1fefd5e passes all four required lanes: 29,764 records/56 new passes; all 29,708 preceding outcomes/messages/exact skips remain. All 272 held-control removal observations match the private controls. Native picture/frame/process/reference and candidate scope remains.
