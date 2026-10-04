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

Clean-source `08acc2f327a99f63e904e9195085d180f55e4447` passes all four lanes in
[CI 37194533201](https://github.com/benny-cz/FileCat/actions/runs/37194533201); three package jobs are intentionally
skipped. Independent checks verify three server artifact digests/sizes, every extracted XML and exact selected
case names: **75 affected Windows Core/App cases and 37 App cases on each Unix lane pass**, zero affected skips.
This includes all seventeen synchronization/comparison/operation cases and the previous I122/I119 controls.
Windows full inventories: Core 744/47 skips, App 277/15 skips, Platform 166/33 skips, Remote 88/28 skips.
Linux/macOS App each pass 256/36 skips. The complete live NTFS-history case passes. The original failed run remains
retained; a green successor does not establish its unavailable interleaving.

The elevated Windows 26300 guest passes **17/17 cases**, zero skips, using a self-contained win-x64 payload:
367 verified files, 368 ZIP members, thirteen canonical source copies. Execution ends 10:16:49 UTC;
independent cleanup at 10:21:40 confirms controller 12512, worker 13792 and all executable paths below the owned
root absent, with no owned temporary files. Guest UUID `9D224D56-1161-A849-ABA7-2581A980895C`; root:
`C:\Users\Public\FileCat-synccheck-validation-efa14131b5624489bd0ad9243055b517`.
Private clean root: `clean-08acc2f` below the working evidence root. Single-worker JSON is verified as an exact
one-item inventory. This is native OS process execution of headless controls, not desktop interaction.

| Clean evidence | SHA-256 |
|---|---|
| Guest ZIP / manifest | `61889d5baa780524778f7b701546089465bc15f82e32bfa43aa3c97810de0420` / `471e9eb9d9b169b8d5749102c77ea4f24d6246e58aed5cff6f174dcf8538da38` |
| Guest runner / cleanup observer | `21757878388cea84f43ee39cb2974708a8419ee3017de2cfc8afd0745a44b060` / `18942aa51729f33d5a294f7d7f8bd76cdb54ddbff4d665d8e62f2537d131300e` |
| Guest XML / cleanup / independent inventory | `c8ed7a08b2017d16f8e65e877afde72158328790875d17283bb469327a185146` / `673f97104ab6125c52b88154ec6c5b84fdeb594f88658b85466d0a94c3df1988` / `1adf4d762fb311f9d08d08ec1965b512a3951a114228d18aa42ce6e2bc6850f8` |
| CI metadata / full log | `0a1ac296ed118ea68eb4f6f32346f13872fd3d9ba17798c0ead13fc7fd1734de` / `506ed7d3c277e8ed6a21301e978a3a8021e4f55b5dbeb82b1bdabfdd25d91bcb` |
| CI Windows / Linux / macOS ZIP | `fd41e7279148cf6d6a3c25aa0c9042a2d195f21a059bb6f01d8c576603502f90` / `ad462dfd7864e410f7d2f7260016e850f18abafb0591bc084d40ea02131ca73c` / `6cc045a467546d8ff817b4041529ab74597dd024512e0ebded2bd507f6e40986` |
| Independent CI inventory | `6fafb12ee41f9c4c1e183f07d694c5fe85c050a2cad37a9252bfcb5ac192e7b7` |

These are preliminary component/headless controls. Native desktop interaction and exact candidate qualification
remain. Physical USB qualification stays held, and overall release status remains **NO-GO**.
