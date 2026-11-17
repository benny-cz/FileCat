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
