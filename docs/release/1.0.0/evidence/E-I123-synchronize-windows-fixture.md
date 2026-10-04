# E-I123 — the synchronization fixture uses FileCat's Windows adapter

I123/V13/CI, low validation-reliability defect. The original
[CI 37192262649](https://github.com/benny-cz/FileCat/actions/runs/37192262649) on
`82f7488e4e9ff81a59a14b83171016eeec7c6e2a` has one Windows App failure: synchronization leaves
changed.txt "old", with its replacement job AwaitingDecision. Three other lanes and all I122 affected cases pass
([E-I122](E-I122-picture-device-demand.md)). The original decision request was not recorded; its precise failure
mechanism remains unproved.

The headless test application initializes its theme, but never executes `App.OnFrameworkInitializationCompleted`,
where the shipping application registers the Windows platform factory. Its synchronization services consequently
use `PortableFileOperations`, including ordinary .NET replacement, instead of the native Windows adapter's
I22 replacement fallback. A deliberately held `FileContentSource` target makes that difference reproducible:
the unchanged fixture fails, with the target still "old" and decision `error-access`, "Could not replace the existing
item: Access is denied … Controlled Folder Access …". The failure is an observed pending replacement, not a fixture
timeout or a claim that the original CI interleaving was traced.

Only `DirectoryDiffTests.cs` changes. This test now registers the native Windows adapter in a disposable scope,
restoring the previous factory even on failure. The held target remains open through synchronization. Afterward
the target contains "newer", its open reader still reads "old", the new subfolder file is copied, the deselected
extra file stays, and no staged sibling remains. Existing Update/Mirror preview and keyboard choices remain tested.
Linux/macOS retain their existing platform path. No production copy, comparison, scheduling or replacement policy
changes.

All **17 affected comparison/directory/operation cases pass**, zero skips. Full App passes **271/292**, zero failures,
21 declared skips. Working source is `82f7488` plus the one test-file overlay. Three immutable captures each retain
734 inputs/thirteen raw source files: **2,202 verified inputs**. Production source matches canonical Git blobs;
Core, Windows platform and App production DLLs are identical across all captures. Active final test/production
assemblies and direct XML inventories verify.

Retained intermediate harness failures:

- The first native-adapter run passes sixteen controls but fails the new file-list assertion: default sorting follows
  the host's Czech culture. The successor uses explicit ordinal comparison.
- A full run under the long private evidence temporary path fails Git's fixture with "Filename too long".
- A shorter E: temporary path is inside the checkout, violating that Git fixture's outside-repository premise, and
  lacks space for the 384 MiB copy fixture; that run retains both failures.
- The final full run uses identical captured assemblies in the normal system temporary directory,
  `C:\WINDOWS\Temp\FileCat-I123-7e60a0979b21452d990d5484cfdabf99`, outside a repository with adequate space.
  No Git configuration or production changes were used to make it pass. Raw XML escape sequences are preserved;
  the independent observer normalizes backslashes only for its quoted baseline-message check.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i123-synchronize-windows-fixture-20261004`.

| Evidence | SHA-256 |
|---|---|
| Controlled fixture baseline XML | `07209140cf95f81b18aad525611e60251ee5ae25b98f2bc1e2bbf23d0ce1b230` |
| Seventeen affected / full App XML | `8b2fc27839a6bb1ae0546d000b1efae07ce611251e6cb71027f7df59c0e41405` / `2c14be646cddc002f76f68996298b71a878b38c869d7c5425b3e7e1e8895a672` |
| Baseline / intermediate / final input manifests | `fe33a26cdf2111e73e5a8bf8e35731d250a5bd0cebd78fa18bbb571d5627cb00` / `3a2b21aa9e100188689d6986f6623202b3d33e07d68644f22cf5348abe683364` / `594d9b13885af61bd79c853d8e2c6c6742a7d8801e919092b3051aa8bc06a0df` |
| Final full-run environment | `15a8f2568a2b619181a3a22acb783e7127ae26362af2ed31ddf20237a7c48750` |
| Independent source/input/assembly/case inventory | `e4a06d2a3ba778f4ec81a0c2f58ec19f85a2dbc6944fc71cf4deff249d942898` |

Clean-source CI and Windows guest validation are next. These are preliminary component/headless controls;
native desktop interaction and exact candidate qualification remain. Physical USB qualification stays held,
and overall release status remains **NO-GO**.
