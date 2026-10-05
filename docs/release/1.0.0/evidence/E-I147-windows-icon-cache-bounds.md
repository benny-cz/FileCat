# E-I147 — Windows icon retained demand and stale completion

Classification: preliminary committed correction; controlled/host validation passed,
clean native and exact-source CI pending. No release candidate or human GO.

V12/I06 exposes the Windows counterpart of I143. Baseline App source cb85f0a retains
50,000 shared type entries after 50,000 public GetIcon calls. Controlled per-item
loads in the real production PerItem path start 512 held loads; completing 5,000
requests repopulates 5,000 entries after the clear-at-4,096 policy. Changing pixel
size 16 -> 32 -> 16 also lets an old held result replace the new answer. The unchanged
baseline App DLL fails four regressions; its helper-failure/retry positive passes.
These load replies are controlled factory tasks, not observed native Shell calls.
Shared type demand uses the actual Windows native icon source; no desktop is drawn.

Correction committed and pushed at `6cf17e5cfeb055eb0046ab0bc8069450e97bdd11`:
shared icons reuse the 4,096-entry LRU/nonblocking 256-request queue. Per-item plans
have their own 4,096-entry LRU, 256 waiting requests and four fixed asynchronous
consumers; overflow leaves the existing fallback and later redraw can retry.
Entry identity rejects stale completion and stale retry/removal, with unpublished
images disposed and borrowed published images kept valid. Pixel size is captured
for the load; both caches clear on a size change. Windows ordinal case matching,
local-only named-resource policy, helper answer/retry and known-folder initialization
with its existing error fallback are preserved. Limits are per NativeIconSource;
shared/per-item retained limits are separate, not a total 4,096-entry memory claim.

The identical final test DLL fails four cases against the original App DLL and
passes all five against the correction. Only FileCat.dll/PDB differ in those
payloads; all other files and private temp/process exit checks verify. Corrected
finite samples retain 261 shared entries after 50,000 type calls, four held loads/
260 retained requests, and 260 completed/retained plans after 5,000 requests with
peak four. These counts are scheduling observations, not promised fixed counts.
Three portable controls verify rejected demand never invokes its factory, retry and
case matching, a stale failed answer cannot remove a same-key replacement, and
50,000 completed plans keep a 128-entry test budget while recent/borrowed answers
remain valid. All eight new controls pass in the full host suite.

Affected host inventory: 16 pass/one declared network-capture skip, 17 cases.
Final full host App: 365 pass/23 declared skips/388 cases. Baseline 4 fail/9 pass/
one declared skip and intermediate corrected captures remain retained. Initial
portable iterator cancellation warnings are corrected; no production default,
dependency or network/removable consent policy changes. Clean source publication,
native Windows/portable Unix and four required CI lanes are the next validation.

Private root: the authorized second workspace's
`FileCatReleaseEvidence/windows-icon-demand-20261006`.

| Retained item | SHA-256 |
|---|---|
| `baseline-v1/receipt.json` | `20f13bd760deb58c7054c82198a4025566d6019cb16fe580c3a3fed530180728` |
| `baseline-v1/results.trx` | `526f25a0cbeece420c5e237780778279075727fed61ca8440285aef42dffcc4d` |
| `corrected-v1/receipt.json` | `70d7b3a840bd53f079a2146f32412cef8e26f697b35acfde2ebb56d3cb5663b9` |
| `corrected-v2/receipt.json` | `62d79fea943c7efb9b16957844e63bc7bb3a2842ee0c234127912f8a54afc752` |
| `full-v1/receipt.json` | `c50c477c112547af794b3341db03cd6bc445904fc33e29f2405ce24036171755` |
| `full-v1/results.trx` | `e6a4d496acc29c5071b1e7f395c310172dabe58d4fcf75466e7f4f08af03630a` |
| `controlled-baseline-v1/receipt.json` | `529dc18cd0a3bafafd262a4511cc5894cf53f141ca839e5f95a88d6c1845b764` |
| `controlled-corrected-v1/receipt.json` | `b79fb92fceaf9789e1da40f6d714d7611c886b1ee89f0d0def46675d3418b5aa` |
| `controlled-comparison-v1.json` | `216426e9ea2da458eed35afb48a43862f69ac792fd80e9920d7b0788e8510010` |

All-consumer strong references/displayed memory, separate application/source
instances, actual hung-device/native helper load traces, UI frames, AT, reference
hardware and exact-candidate qualification remain. I06 stays open. Both guests stay
running; Mac awake v3 remains active for further validation with restoration due.
G: remains untouched/HOLD. Interactions stay queued until 08:40 CEST; overall NO-GO.
