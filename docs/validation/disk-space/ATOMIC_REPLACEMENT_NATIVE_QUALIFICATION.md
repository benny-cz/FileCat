# Private atomic replacement boundary qualification

This packet qualifies a native prerequisite for named inventory replacement. It does **not** implement the complete Save/Replace, prepared-data ownership, previous-copy backup, catalog transaction, interruption recovery or final review workflow. Those remain mandatory work. The tested source projection is the nine files listed in [source-manifest-r932.json](source-manifest-r932.json), applied to commit90aa5fb7. Separate catalog integration work was not part of these binaries or tests.

`OpenReplacementFile` grants an exclusive file lease only in an application-prepared owner-private directory. `ReplaceOwned` accepts a generated incoming/previous `.payload` source and an ordinary `.fcdsa` target in that exact directory-capability instance. Arbitrary paths, control files, comparison files, ordinary files used as incoming objects, and another directory capability are refused. The new internal payload names are separate from user inventory names. Normal maintenance sharing behavior is unchanged.

Windows checks the held files' privacy, names, native identities and read-only state. Its special replacement lease permits delete sharing while retaining the private exclusive range lock. Publication uses FileRenameInformationEx with replace/POSIX flags only; there is no classic-rename or read-only override fallback. Unix checks held-root-relative names/inodes, owner-private regular/single-link shape and exclusive advisory leases, then uses same-directory `renameat`. Both implementations update held-object state after the namespace mutation and verify binding/durability. Unix's cooperating private-namespace guarantee is not a generic source replacement guarantee. This API grants no imported path or generic cleanup authority.

The previous-copy backup is a required **higher-level** protocol step; the primitive itself does not copy or certify that backup. The fixtures prepare an independent previous copy and verify it survives the switch. A generated previous payload can atomically restore the original bytes in the same narrow namespace. All original source/metadata/capacity control protections remain required.

Microsoft documents the extended rename flags and held-file behavior in [FILE_RENAME_INFORMATION](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information). Actual Windows VM tests determine the qualification here; unsupported filesystems must preserve the existing inventory and offer a new name.

All seven focused cases passed on Windows VM, Mac and Ubuntu VM: switch, restore, concurrent namespace observation, active-reader refusal, wrong incoming source, wrong directory and read-only refusal. The observer uses Windows delete-sharing reads or a raw POSIX descriptor without flock; it samples both versions and rejects missing or mixed bytes. A cooperating FileCat shared reader is tested separately and prevents the replacement lease.

Broad r931 results:

| Machine | Core | Windows native | Headless rendering |
|---|---:|---:|---:|
| Windows VM | 1,436 passed / 48 platform skips | 75 passed | 331 passed / 1 Unix-only skip |
| Mac | 1,462 passed / 4 platform skips | — | 332 passed |
| Ubuntu VM | 1,462 passed / 4 platform skips | — | 332 passed |

All original runners and collectors reached terminal success, with no FileCat/dotnet test processes in the final inventories. One result archive was collected per completed machine. Full build r927 and test-only build r929 completed without warnings or errors. Broad batches reused verified product files from r928 and changed only the rendering test DLL/PDB; preparation took7.17seconds. No product code changed after the frozen native projection.

Earlier failed evidence remains: r928 passed all12new core checks and6of7native cases on each platform. Windows correctly returned UnauthorizedAccessException for a read-only target rather than the test's expected IOException; Unix's .NET path-based observer correctly honored flock instead of acting as an external observer. Only the test fixture changed. Corrected r930 passed7of7onall3, including namespace observation. The other archive tester's jobs postponed r930 before extraction; the same staged candidates resumed without recopying once free. None of its processes was stopped.

The manifest hashes9native/test source inputs and99evidence/report artifacts. Qualification covers actual guest native filesystems and headless controls. User-facing replacement, catalog/recovery integration, generic source actions, physical desktop, full persistence, scale and final independent UX/UI/functional/security review remain open.
