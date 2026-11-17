# Progressive preview and main integration qualification

The internal indexed preview and recorded-result handoff passed native filesystem and
Avalonia headless checks on Windows, Mac and Ubuntu. The branch includes main commit
`40480c8e`. The [r1298 packet](source-manifest-r1298-progressive-main.json) retains
37 executed phases from 17 runs, their original failures, subsequent scoped checks,
clear post-run process inventories, build inputs and exact source bytes.

## Qualified behavior

- An internal indexed scan publishes a bounded preview while its native producer is
  still blocked. The full recorded result opens after actual scan/source completion.
- Opening the recorded result preserves the preview's displayed revision, marks and
  basket. The new tab receives its own selection and the actual runtime owner.
- Native view preparation finishes before ownership moves. Failed or canceled
  publication restores the owner when possible; closing the source model cannot
  resurrect it. Capacity refusal happens before queries or ownership transfer.
- Stop opening retains the captured result and permits retry after outstanding work
  retires. Private-storage fallback retains the bounded memory behavior.
- Verified folders and existing group scopes are retained. Opening a preview's Other
  subset explains its return to the captured parent because the complete ranking can
  differ. Imported observations cannot create this runtime handoff.
- An epoch with an existing successor offers rescan from the newer result. Constructor
  and root-validation failures still roll back reservations.
- Nine analyzer live regions use stable automation IDs while their spoken names follow
  their text. A real automation peer checks successive status changes. Source audit
  checks all application sources; this does not establish physical screen-reader support.

| Executed check | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Epoch controls | 21 passed, 2 declared skips | 23 passed | 23 passed |
| Full analyzer rendering | 824 passed, 1 declared skip | 825 passed | 825 passed |
| Lifecycle subset | 39 passed | 39 passed within the full rendering run | 39 passed within the full rendering run |

Windows used r1295; Unix epoch checks used r1283 and full rendering used r1285.
The intervening Unix changes affected test fixtures only. Source/input hashes bind
these runs, including the cached payload overlays. Native producer blocking establishes
progressive publication on the small fixture; it is not the reference first-visible
latency qualification for a million-entry physical desktop.

## Main integration findings and focused resolution

The broad Core runs passed: Windows 10,672 passed with 1,146 declared skips; Mac
11,517 passed with 296 skips; Ubuntu 11,546 passed with 267 skips. The new Core change
is the read-only successor-state query, covered by the epoch controls above.

Original broad runs retain these findings and their exact failed XML:

- Eleven rendering cases exceeded the 128-handle limit when the test assembly moved
  deeper in the payload. Restoring the shallow rendering assembly layout resolved
  them. The native handle limit remains 128; App assemblies keep the canonical layout
  required by their worker probes.
- Windows had an existing OneDrive directory without an available registered provider.
  Its test now checks native registration independently, keeps the ordinary-folder and
  junction checks, and checks a plain directory named OneDrive. The five targeted
  cases passed three with two declared skips; the registered-provider positive remains
  unqualified on these machines.
- Unix App bundles lacked their native FileCat launchers. RID-native apphosts generated
  with the SDK resolved the identity checks; the Mac sandbox probe also confirmed its
  fork and network denials.
- The source audit identified fixed names on live regions, including saved-analysis
  details. The source and peer checks now pass.
- Rapid headless drawer inputs needed layout retirement between keys. The storage
  fixture now observes posted paging-button readiness and waits for its canceled native
  reader before simulating journal retention. The original assertions remain.

Mac's final targeted App run passed 21 with one declared storage-platform skip; Ubuntu
passed 20 with two platform skips. Windows r1291 passed 20 with one platform skip and
the paging-readiness failure; its repaired storage case passed in r1295. Its 18 drawer
cases and source/launcher checks passed in r1291. These are scoped resolutions of the
retained broad-run findings, not a synthetic clean rerun of the entire App assembly.

The packet preserves checkout line-ending differences as exact byte deltas against
the Git base, so reconstruction does not depend on local newline conversion settings.

## Remaining work

The public command stays disabled. Full indexed after-scan selection/review, branch
refresh/watchers, remaining W0–W8 requirements, shared resource and private-memory
qualification, reference 1M/10M performance, native desktop/input/theme/DPI/accessibility
and package qualification remain open. The independent final UX/UI/functional/security
review has not been performed. This packet does not complete W1 or the user goal.
