# E-I127 — inaccessible folder counts remain labeled as lower bounds

I127/V12, medium metadata-truth defect. Preliminary Windows host; actual clean production payload
`e406c9649a8f7b0bfc799e12604f41cfeaed20d1`, repository at `ddbf488cfac942e3cfd1216e82c5f6919d140358`.
No production change was made before reproduction.

An isolated headless probe references the retained production DLLs. Each owned real-file fixture has a 1,000-byte
visible file and a 234-byte file in a child directory. Two controls deny only directory enumeration to the current
test account on that owned child, independently verify UnauthorizedAccessException, and run DirectorySizer and
the actual Count command. Two accessible positive controls count all 1,234 bytes. Cases include listing refresh.

The denied controls return 1,000 bytes and one inaccessible subtree. FileCat emits a lower-bound notification,
but sets SizeComputed: the size column, marked total and quick view show an exact size, the stats claim complete,
and Count is disabled. This survives refresh. After access is restored, Count skips the marked folder and keeps
1,000 instead of 1,234 bytes. Both denied controls fail; both accessible controls pass. All file hashes are
unchanged and each owned ACL is independently verified restored.

The original standalone compile omitted the MVVM reference and had an ambiguous Location name; no test ran.
A successor copied an app-local hostfxr into a framework-dependent probe and could not start. The next probe
captured the same product observations but its ACL-restoration observer failed: passing an unchanged loaded
DirectorySecurity object did not mark access rules for rewriting. Those records remain retained. Fresh descriptor
restoration fixes the harness, and the final baseline failures concern the product's persistent false completeness
and refused retry. The earlier owned ACLs were explicitly restored and verified separately.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i127-folder-count-lower-bound-20261004`.

| Baseline evidence | SHA-256 |
|---|---|
| Input manifest | `5008037bc76e62b7f6927f7d0f93ba07a12ac0cd7c0cc342c16ddfe81371cd8f` |
| Production App / Core DLL | `d7a38d5329191e51d920be49410f3b56d8b069ae13194de260aac45a009173c1` / `c1fe27a35686c4c273dea53b0f62c53bd2654a888727e6935ee000c6afb0255e` |
| Valid baseline XML / loaded-copy inventory | `7a0363d8f2b37492a56ad16dd1a31fd0d97fdb5bd606fc99ccf76fe051dcd118` / `cb941470e4252715fed2d802d134fa978efeac5ebfa148c438e93d246f40dee6` |
| Valid baseline probe source | `bce2be3f59c0b3d038f4cad9400abccd9c07172c95e3cb1e1f13cf2cd302b005` |

Working source is `ddbf488cfac942e3cfd1216e82c5f6919d140358` plus six production and two test files.
Counts now carry an explicit SizeLowerBound flag through the listing and its refresh/pending-size caches.
The row prefixes the value with ≥; marked totals and quick view say "at least" and "lower bound". Partial subtotals
contribute to marked bytes without claiming completeness. Count remains available for partially counted folders.
Successful retries clear the lower-bound state. An attached folder quick view updates its caption through the
existing debounce/event path when Count changes the row; ordinary file-preview demand retains its previous route.
DirectorySizer's Complete still means traversal ended; its inaccessible count remains the source of uncertainty.

Identical probe source and setup with only the production App/Core DLLs replaced passes all four cases; every
other loaded-copy DLL remains byte-identical. All sixteen real-file fixture payloads from the valid baseline and
corrected probe verify against independent expected bytes. Seven App regressions exercise actual directory
denial, refresh and restored-access retry, including a zero-byte lower bound, and an already-attached quick view.
Three portable Core controls cover pending-row/refresh persistence, complete retry, invalidation and mixed marks.
All **59 Core and 63 headless App affected cases pass**, zero skips. Full Core passes **752/798 with 46 declared
skips**; full App passes **282/303 with 21 declared skips**. Exact skip reasons match the preceding host inventories.

Independent verification checks **1,800 captured inputs**, nine raw sources and two properties files per capture,
active final test DLLs/shared Core, actual probe DLLs, identical probe sources, complete XML case identities and
multiplicity, expected fixture bytes, owned executable identities/children, twelve restored fixture ACLs and absent
regression temporary folders. The original App run has four failures caused by a misencoded ≥ expectation in the
copied test; corrected UTF-8 expectation passes the same product bytes. Its original source/assemblies/XML remain
retained. Two existing Core theory display names are truncated duplicates; verification preserves case IDs and
multiplicity rather than treating those display names as unique.

Independent inventory SHA-256: `89de70f180b7c0cfcb62c47cd1870c37a46343ca7313a843b151f90996ffdbb3`.
Corrected production App/Core DLLs: `7503f71e0e18f4dbeca3c10c90288a3682fa79e0b0aa5b63d3d02b717631b89d` /
`7b5ab8c00ef11f192443cfd121558054eb2f7406477f3330648b36639a5f2b63`.

| Working evidence | SHA-256 |
|---|---|
| Corrected probe XML | `f8d82df566b7bfd9eb0775414ce006afd4a8198e4aca8ed9e9e518d78ec7f593` |
| Full Core / App XML | `7fcc95eaa7a544672aa78053f476482a6e89ec6068ba4b21f4c03abb138c53f6` / `6213c5eb5a1e728c416638e3aea504225365e49c7aa598e354d8d5a49c0a0ddf` |
| Core / final App input manifests | `043611d8b14421b9ad1be3b387c6c5b7c1b440206c519363ffece2cbf1b06705` / `408d21028df6973c6f5ca76f4ee75d696956684a844e61408361bb67e05a1908` |
| Owned worker/ACL/temp cleanup | `c6644d3236c52d0371d4cdd97c6acdf0412a655df5f2aa574543ca442c3d42fe` |

Clean source `2be20cfb8a647261ad9dad32c114888739e77dae` CI
[37214155885](https://github.com/benny-cz/FileCat/actions/runs/37214155885) fails both Windows lanes;
Ubuntu and macOS pass. All seven new Windows App cases fail the fixture's cleanup ACL-restoration assertion,
including accessible controls. Direct Windows inventories are Core 751 passed/47 declared skips, App 281 passed/
seven failed/15 declared skips, Platform 166 passed/33 declared skips and Remote 88 passed/28 declared skips.
Both Unix App inventories are 260 passed/43 declared skips, with seven explicit Windows ACL fixture skips.
All three downloaded artifact bytes/server digests and complete TRX inventories independently verify;
inventory SHA-256 `93313c556bcc15a738d1a467ff6b3dac0c977b1c867f5caf6408439867129ff1` in private
`FileCatReleaseEvidence/ci-37214155885`. Logs include the failing ARM64 lane. These failures remain open.
Descriptor diagnostics are added without relaxing restoration; all seven host cases still pass
(diagnostic XML SHA-256 `1c402ef5e4d4cc1368a3e07d1583d0eedd2a54a2bc522d90d80b28e5176ebff1`).

Diagnostic clean source `b80f2868d295766e2ceb287439eadb8d4582913a` CI
[37215304292](https://github.com/benny-cz/FileCat/actions/runs/37215304292) retains the same failures.
All seven direct Windows failure messages and seven ARM64 log messages differ only by the DACL `AI`
auto-inheritance marker: every ACE is identical. Three server digests/full inventories verify, and the independent
descriptor/failure inventory SHA-256 is `e55804a146a684b6a78730a6c691965f5de0df9fede6038b655ab9f4245ec93f`.
The comparison now masks only DiscretionaryAclAutoInherited via RawSecurityDescriptor; all access rules and every
other descriptor flag still have to match. Actual denial, restored-access retry and unchanged hashes remain required.
All seven corrected host cases pass. The captured 751 files include thirty source/build inputs;
XML SHA-256 `20134ae417b1eb541121913af093354a874c458f48e979119d263b7e8824119a`, input manifest
`77e342633d8f48d16733174680bf315068bc0a61de6d052f092b1fb9712a53f8`. No production code changed for this correction.

The independently verified SDK-free Windows guest run of clean b80f286 passes **122/122**, zero skips:
59 Core and 63 headless App cases. Guest UUID `9D224D56-1161-A849-ABA7-2581A980895C`, Windows build 26300,
administrator token; private `clean-b80f286`, guest
`C:/Users/Public/FileCat-folderlowerbound-validation-b082369442f44609967e8a030c93688d`.
All 702 payloads/703 archive members/thirty canonical source/build inputs and XML case multiplicity verify.
Controller 1460 and workers 2428/11352 ended; cleanup at 16:10:11 UTC finds no owned processes/children/temp files.
Independent native inventory SHA-256 `50e0e1a441f0175597e0393bc805e5493661715bac3d335c9d9fe49443a0a329`;
ZIP `122e3550618caaf8a33400e74687e9d4ffffc807304fbd36fc76b7e38f7eebd8`, manifest
`c892c35cdb8fbfa1a53cf7dd4e02fd35169f00e84686d2662414bea5ec9b7136`.
This run precedes the CI fixture comparison correction; it does not qualify the successor test assembly.

Final clean source `9d332825237ffab2bb6f329e4a454fe78e3239e7` includes the corrected fixture comparison.
[CI 37216212781](https://github.com/benny-cz/FileCat/actions/runs/37216212781) passes all four required lanes,
including Windows ARM64 tests/package startup/installer compilation; three tag/manual package jobs are skipped.
All three downloaded server artifact digests and six complete TRX inventories independently verify.

| Direct CI inventory | Passed | Declared skips |
|---|---:|---:|
| Windows Core | 751 | 47 |
| Windows App | 288 | 15 |
| Windows Platform | 166 | 33 |
| Windows Remote | 88 | 28 |
| Ubuntu App | 260 | 43 |
| macOS App | 260 | 43 |

All **122 affected Windows cases pass**, zero skips. Each Unix App lane has **50 affected passes and 13 declared
skips**: seven new Windows ACL cases plus six existing Windows-factory folder-identity/leave cases. Every skip's
identity/reason verifies, and the six existing reasons match the preceding CI inventory. Verification retains raw
TRX names/test IDs/execution IDs and full case multiplicity; it accounts only for the native xUnit XML runner's
additional backslash/quote escaping when comparing the complete affected inventories. No case is omitted.
Independent CI inventory SHA-256 `755e1232825832df328883f8411aea2e2710842581dd469caca6e977fca04cf9`;
private `FileCatReleaseEvidence/ci-37216212781`. Both failed CI attempts remain retained.

The successor SDK-free Windows guest run of clean 9d33282 passes **122/122**, zero skips, at the same UUID/build
and administrator identity. Private `clean-9d33282`, guest
`C:/Users/Public/FileCat-folderlowerbound-validation-e924be484b1b4eabb31ff51ef75243cb`.
All 702 payloads/703 ZIP members/thirty canonical inputs (26 C# sources, two build properties files and two test
projects), copied production inputs, case inventories and ownership/cleanup verify. It ends at 16:21:51 UTC;
cleanup at 16:23:08 UTC finds controller 3700/workers 6692/7300 and all owned executable children absent, with
no temporary files. This qualifies only the controlled preliminary folder-count scope.

| Clean 9d33282 guest evidence | SHA-256 |
|---|---|
| ZIP / input manifest | `af02f5f1faaf8a956d61fece14e106927f3532cc289ef29ce6ecec337f127c84` / `0972c6da15f0eee9b68144378c8c53e6e91065a02fcbf7ad8bcea1bbd23d577b` |
| Runner / cleanup script | `e1f25b0ec41134cb9b962e47f44eb0a95c59e196c3d058c0761755d7506acf09` / `586abb72015d10f2d73083ad30f85a9b6a6b9f92bb07a768e987d5d962f17da6` |
| Core / App XML | `60172ca2f6b567e61494eafa1f59b73664f15296062c13ecfc9a9bf7937e035e` / `65cdef2a2ff7de89cc27879523227d9852d7a3ccf80fba88eef167640bd95cc0` |
| Guest identity / exit record | `e48c237dc745019697cfda25a7629021de55f2c07d6043cdd84c49c864d8f2d7` / `26fbb7078cc44c945ba6ecad409356e08926fe26544527e7f54825e8127cf6d6` |
| Owned worker/temp cleanup | `959e3211301ccbecf8d0913a217310138262f5d187e7a1e1a0c1110ec09c9ad7` |
| Independent native inventory | `55f2f1c9d4db65e74949b9dccc8686edd068d037d8973c1562d0328a2451f92c` |

The final independent slice check re-verifies all 751 normalized working inputs/thirty canonical sources,
746 retained clean guest/CI evidence files and every document pin; its retained record is
`final-slice-verification.json` in the I127 private root.

I127 preliminary remediation is verified. Native desktop/AT/candidate remain unqualified. Computer Use
initialization still exits before selecting a host or VMware window after the owner's Codex restart; exact tool
result retained with E-I126. VMware command execution remains usable. No USB action occurred; its source-change
gate remains held. Both VMs remain running. The owner requested finishing this slice and stopping before another
Codex restart/elevated launch; execution stops after the evidence push. Overall **NO-GO** remains.
