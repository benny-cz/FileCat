# Native million-file acquisition and exploration checkpoint

The Windows VM, Mac and Ubuntu VM completed real one-million-file wide and deep workflows. The immutable [source, input and receipt manifest](source-manifest-r1190-native-scale.json) records the exact product, original failures, focused reruns, native controls and cleanup. The [workflow supplement](source-manifest-r1190-native-scale-workflow.json) preserves additional runner/validator sources, including historical staged versions. Public registration remains off; this checkpoint does not complete the feature or its final independent review.

Each case captures every native path, retains the dataset after closing acquisition, verifies both aliases of a hard-linked anchor, builds the explorer, requests six sort/direction combinations, inspects aliases, switches chart modes, applies a Unique-size filter and builds extension groups. It then closes the actual owners, checks zero shared scratch quota and verifies the source anchor. These are native filesystem and headless application checks, separate from physical desktop input/accessibility qualification.

The wide fixture has one million files directly beneath its root. The deep fixture has 32 single-component directories, with the generated files in the bottom directory and one hard-link anchor at the root. Generated names are `row-00000002.bin` through `row-00999999.txt`, alternating extensions; generated lengths cycle from zero through 16 bytes. Two seven-byte anchor names refer to one object. Captured path counts are 1,000,001 wide and 1,000,033 deep.

| Platform | Acquisition, wide/deep | Explorer, sort and charts, wide/deep | Filter, wide/deep | Extension grouping, wide/deep |
|---|---:|---:|---:|---:|
| Windows VM | 427 / 1,312 s | 161 / 154 s | 208 / 189 s | 545 / 458 s |
| Mac | 151 / 259 s | 75 / 77 s | 66 / 68 s | 386 / 398 s |
| Ubuntu VM | 217 / 253 s | 138 / 145 s | 120 / 139 s | 539 / 555 s |

Windows sampled private-memory increments are 182.04/183.45 MiB wide/deep; Ubuntu increments are 279.26/239.68 MiB. Sampling runs every 50 ms and is not an absolute-peak guarantee. Mac's additional native physical-footprint increments are 252.48/207.50 MiB, using the previously qualified ABI/counter controls. Physical footprint is distinct from committed private bytes; the Mac committed-private-byte gate stays open. Every completed case releases its source owners and final scratch quota. Collection-time process inventories were empty.

The first-observation callbacks arrive at approximately 1.02–1.05 seconds on Windows, 0.58–0.60 seconds on Mac and 0.76–0.80 seconds on Ubuntu. This timer includes style/application/private-profile preparation. It does not qualify warm shell startup or user-visible feedback, and it does not close the Windows one-second target. DirectorySizer baselines are retained separately; acquisition additionally performs identity/allocation work, capture and indexing. The proposed equivalent-work overhead target is not qualified by these timings.

The changes remove three sources of avoidable scattered reads. Darwin opens an enumerated child relative to its held directory, retaining the same leaf flags, mount/device guards and full ancestry validation before and after batches. The coalescer carries bounded inert values through authenticated sorts and writes canonical objects in captured-path order. Internal hierarchy traversal follows captured rows while public name ordering remains exact; unique physical identities are derived from the sealed index before using the local object-order path. Duplicate-identity cohorts keep their existing merge behavior.

Focused codec, maximum-field, malformed-payload, root-replacement, symlink/FIFO, canonical-order, uniqueness and accounting controls pass on applicable platforms. Broad core, rendering, settings and Windows native suites also pass with their declared skips. The old-product Mac negative control detects excessive descriptor churn and unordered canonical objects. Its small fixture fits the shared cache; it does not establish a read-amplification failure.

Original interrupted million-file baselines remain failures. An initial verifier mistake and the corrected test's literal-asterisk filter mistake are retained separately. A Mac grouping run was stopped after 310 seconds during active merge work; it was inconclusive and reran with unchanged product bytes. The complete rerun establishes the final result.

Retaining fixtures avoids repeated creation. Windows completed its partial wide fixture, then moved that closed, fully checked tree under the deep hierarchy. The focused deep run reused all 121 verified rendering inputs, transferred an 8 KB driver bundle and reused the source in 0.205 seconds. Its path/marker guards and negative control are retained. The original Windows batch deliberately stops at a recorded deep-case boundary; its nonzero result is preserved, and the separate deep batch passes. Only owned fixtures and instances were modified.

Complete ten-million-entry workflows, global multi-session resource limits, native desktop/theme/DPI/accessibility/package matrices, remaining lifecycle/actions/helper/recipe work and the final independent UX/UI/functional/security review remain open.

## Retained-fixture restart on the merged build

The fixture runner writes an atomic checkpoint outside the scan source after each verified batch of generated rows. During the directory-size baseline, a separate watcher also passes withdrawal to the running test so the guest can yield to another tester.

On the Mac, r1572 deliberately stopped generation after the first checkpoint at row 16,386. The retry kept that row's inode and modification time, completed the 65,541-file fixture and captured 65,542 paths, including the root. The acquisition and exploration case passed and closed its owners. The post-run FileCat and test-process inventory was empty. The render.xml SHA-256 is 656e41ad372fad012ccbbb89251bd3c391c7c0ab5061d677c625dffe85204c4d. The combined interruption and resumed receipt log SHA-256 is ef112a9f65b21ab5ec151b14cbaF4c8d6e4dcce4cce70bfd23008ec08c2f6716.

## Scratch limits and retained previews

The Mac r1592 and Ubuntu r1591 wide fixtures each contained 9,999,999 source files and one root. Both verified their file counts and logical bytes before the scan, then ended without a recorded index. The Mac last checkpoint observed 8,729,311 paths and Ubuntu reached 9,019,510. Both runs closed their owners. Ubuntu detached its owned ext4 fixture image. Their receipts identified the missing index without identifying which resource or index failure caused it. Full ten-million-path exploration remains unqualified.

FileCat now records a private scratch budget limit and a free-space reserve limit separately. An interrupted capture keeps the bounded prefix and describes the applicable limit in the preview and the unavailable recorded-result action. The final telemetry captures observed and preview path counts, coverage, issue codes and sampled peak scratch reservations.

The r1604 checks ran the exact same eight Core and ten Rendering methods on each test machine. All 18 passed on each platform. The Core controls force each of the two limits while a bounded preview exists and verify that the preview remains available. The renderer run covers the indexed preview and recorded-result resource views, settings and fallback regressions. A different tester appeared during Windows result collection, so the broad follow-up retained its existing checkpoints and used its idle checks before running again.

| Platform | Core XML SHA-256 | Renderer XML SHA-256 |
|---|---|---|
| Windows VM | `b90291b14dd0716979caad9cc9d1787d64969e99785740d4f62f8bb15dbfd06b` | `68c590376297b3a53af4e43013ae589fd56787b26c27a98dc1da77d3e2639434` |
| Mac | `a0381b2e1a8273a231b8815ca83340a18b0878e269d8cc172ddead127cd674eb` | `1f7e4b74589d85303e0def8664608d34c03e550dbe4fa5659b1887f6adb19468` |
| Ubuntu VM | `7e5581ba21fb370f47093cc7dd7d560bce6928c4729ad9d58aceee6b8af1ff43` | `a195eb9d1afa3cc666f40905b669cb4cb2c79dc5d166ac4e9c2dc01e7a42b8e8` |

The Ubuntu r1603 rerun confirmed that the 4 GiB scratch budget stopped the capture after 9,015,920 observed paths. The recorded snapshot had partial coverage and retained 4,096 preview paths. It could not publish a recorded index. The peak sampled reservation was 4,294,948,956 bytes. Final shared scratch reservations and source close failures were both zero. The fixture was reused and the owned ext4 image detached. The full-index test failed its dataset assertion as expected for this measured limit. The native receipt SHA-256 is `07f676910df27908fc4a318256cd6371c66f4f6f1df7ff2f9e9513a2dbec55a4`.

The Mac r1610 rerun also reached the 4 GiB scratch budget, after 8,730,481 observed paths on its retained APFS fixture. It kept 4,096 paths in the partial preview and released its scratch reservations. The peak sampled reservation was 4,294,948,370 bytes. The post-run process inventory was empty. The full-index assertion failed at the measured limit. The combined log SHA-256 is `44389347334fed12d65c5ff16d55463c1ff1a41efde89438a8f6e7e1d9945f51`.

The run also reported an indexing failure after the budget issue. The producer now skips finalizing the faulted capture after a known scratch limit. A separate native acceptance case verifies the retained preview and its disabled recorded-result action while preserving the full-index assertions in the original test.

The updated producer passed all 18 focused r1611 checks on each platform. Ubuntu r1620 reran the actual ten-million-path fixture and passed the explicit partial-preview acceptance case. It observed 9,021,421 paths, retained 4,096 preview paths and reported only the scratch-budget issue. The window displayed the budget reason and kept the recorded-result action disabled. The source-close failures and final shared scratch reservation were both zero, the image detached, and the process inventory was empty. The earlier r1613 driver encountered a cast error examining an unrelated control. The r1620 retry used identical application binaries with a corrected test reader. The combined proof SHA-256 is `f2f7f28e2b8e2a96ffe03f933f708cce8eaac0e52214f0987c257a818796123e`.

The Windows r1618 storage sample captured 131,073 native paths, including its root. The source contained 131,072 file paths in a wide directory with 128-name hard-link cohorts. Indexed exploration verified an actual cohort, sorting, charts, a filtered result and unique-byte totals. Source creation used 76,558,336 bytes of free space during the sample. A linear extrapolation gives about 5.84 GB for the source alone, before the 4 GiB scratch budget and the volume's free-space reserve. That estimate exceeds the space available during the sample. The fixture and owners closed normally, with zero retained scratch bytes and an empty process inventory. The proof SHA-256 is `fd0a015b13755e0ece9c439e4f1dde2ace6d9ca849a14baaefc54b8fb1c57a89`.

The Mac and Ubuntu r1635 analyzer Core selections each discovered 2,038 cases. They passed 2,029, skipped four platform-specific or opt-in cases, and found five stale test expectations. Four controls now assert the exact budget exception and preserve their existing reader and quota checks. The Standard allocation control now asserts its recorded component and current description. The r1637 test-only overlay passed the same five cases on each of the three machines. Matching case names and hashes for every unchanged input gives 2,034 passes and four expected skips on each Unix platform.

| Platform | Broad r1635 XML SHA-256 | Five r1637 replacements XML SHA-256 |
|---|---|---|
| Mac | `f533ca1b215ee098f6998a5d41022481ccaeaa08cf56db673cf61d82c20d3298` | `0a9f62b24c983900cac6dcd680733318ff30a62c5bfc47315a0f354463ed37da` |
| Ubuntu VM | `0e2b16cd9f80c8d5f953b4c740e41e1be3595b81a0db5eb96fd9e80d8a390c7b` | `5c27c5882c333d2a4bf22ba08d8369e78193c350eec99ca368922073697937f6` |

Windows r1633 passed its free-space preflight after older, verified task caches were moved off the guest. It started the actual 9,999,999-file linked NTFS fixture and wrote an observed initial-generation checkpoint at 638,976 paths. The fixture records its next position every 16,384 paths and preserves the cursor with its owned source when a cooperative yield interrupts generation. The Windows acceptance case requires a full indexed result or the exact scratch-budget outcome, including its usable bounded preview and disabled index action. The final Windows result is pending.
