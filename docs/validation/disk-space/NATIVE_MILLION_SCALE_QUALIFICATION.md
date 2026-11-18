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
