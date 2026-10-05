# E-I147 — Windows icon retained demand and stale completion

Classification: preliminary committed remediation; controlled/full host, clean
native Windows/portable Unix and exact-source four-lane CI independently verify. No release candidate or human GO.

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

## Clean native and CI qualification (2026-10-06)

All 870 raw Git blobs/modes/source hashes at committed 6cf17e5 verify, including
all five affected sources, before clean self-contained publication. Native Windows
guest passes 16/one declared share-capture skip, 17 exact cases; all eight new
controls pass. Mac UID 501 and Ubuntu UID 1000 each pass ten/ seven explicit
Windows-or-capture skips, 17 exact cases; all three new portable controls pass and
all five new Windows source cases explicitly skip with their native requirement.
All 354 Windows/350 Unix payload files remain unchanged before/after; command/test
process and owned temp cleanup verify. Windows also observes all owned payload
processes absent. No native drawn UI, real held Shell call or frame timing is claimed.

Exact-source [CI 37389900000 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37389900000)
passes all four required jobs. Four server ZIP digests and six full per-case TRX
inventories independently verify all 388 App case names: Windows 371/17,
macOS 340/48 and Ubuntu 338/50 (pass/declared skip). Affected icon inventories are
16/1 on Windows and 10/7 on each Unix lane; all eight new cases have exactly their
expected outcomes. Windows Core 787/57, Platform 166/33 and Remote 88/28 also pass.
ARM64 App logs report 371/17/388, with package start/drawing and installer compilation
passing; no ARM64 per-case TRX or physical ARM64 qualification is inferred.
One read-only GitHub metadata request times out; its tool output is retained.
The bounded readiness driver retains every subsequent read before collecting the
successful exact-source run. It does not retry or replace any failing product test.

I147 is preliminarily remediated. Wider displayed/borrowed memory, source instances,
actual native helper/device traces, native UI/frame/AT, hardware and exact candidate
remain. Both VMs stay running, G: stays untouched/HOLD, Mac awake v3 is active for
the following disc-image work with restoration due. Owner gates remain queued
until 08:40 CEST. No candidate/human GO; overall NO-GO.

| Retained item under the same private root | SHA-256 |
|---|---|
| `clean-v1/source.zip` | `d60857f4bfaba90e112e33cdd162adb7332768c5db829f1f3040c2954f8b922a` |
| `clean-v1/producer.json` | `256d84e2b86374949bf3f52a2a38a8f8f5235aac246ed80331938019279f6bfe` |
| `clean-v1/windows-executed/independent-guest-v1.json` | `0eba31229d3f7f0cb991cac4a9acf853c63bd93c7e78fb56fe5aa2f57a2df73f` |
| `clean-v1/mac-executed/independent-native-v1.json` | `1c16290b692c9ec2ecbeeac05b80dab1f19fa9f6b241a2961833cc5804282f87` |
| `clean-v1/linux-executed/independent-guest-v1.json` | `4fcb0bc60c5d8e465278a6bf882e8bdb8c77f8836e55b6c0c7426607788fdbf1` |
| `../ci-37389900000-attempt1/independent-ci.json` | `d1a0e4d6f6604676f155906ea0b5688cb783768d46f68d2cc365c915fd03f641` |
