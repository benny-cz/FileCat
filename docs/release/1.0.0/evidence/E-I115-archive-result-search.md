# E-I115 — searching within results retains and rechecks archive members

I115/V13. Medium, search correctness and missing scope. Preliminary working-source evidence:
base `7648865da64857a030628fadf1cd2e6c4539cba6` plus the six-file overlay in `independent-results.json`.
Windows Insider 26220 host, standard token, cs-CZ, SDK 10.0.401. No candidate or native input/frame evidence.

The original narrowing loop skips every reference without a file-system path. Four controlled failures against
unchanged production prove that matching ZIP members disappear, removed member ordinals receive no log, content
search exclusions receive no log, and an unavailable member lookup silently returns an empty finished search.
The fixture has two same-name entries with different sizes, plus another folder and a local-file control.
Two earlier attempts to replace an open fixture fail in the fixture itself; their logs/XML are retained separately.
The valid metadata control releases the old index before rewriting the owned ZIP, retaining the old result references.

Narrowing now revalidates each original archive parent once through its provider. The lookup retains only requested
identities, including duplicate ordinals; matches use current metadata and retain the original relative folder.
Turning off discovery inside archives does not remove archive members already in the input set. New members and
sibling folders cannot enter the narrowed set. No member content or nested archive is opened by the lookup.
Unsupported scopes and content searches are explicit exclusions. A partial listing reports its warning and leaves
an unlisted member unknown rather than calling it gone. Missing/unreadable parent archives report their scope.
The log retains typed original locations; Find distinguishes warnings from items not searched and navigates back
to the archive parent when a log row is chosen.

All four initial controls pass, followed by **11 Core controls** covering current sizes/ordinals, original content
references, subset boundaries, one enumeration per parent, no content/child opening, partial-listing truth,
unsupported/nested scope, cancellation, and missing/corrupt ZIPs. **Two headless App controls** pass the actual Find
criteria → query → result-provider → content/log/navigation routes. An initial nullable test compilation error and
two assertions made before chooser rows were realized remain retained; the corrected observer waits for the actual
bound text. These are headless binding/flow checks, not rendered-pixel or native desktop evidence.

Full affected suites pass: **Core 725/771 with 46 declared skips; App 258/279 with 21 declared skips**. Independent
checks verify every direct XML result, all affected cases, 1,391 baseline/final input files, eleven final source
copies and 1,423 retained files. A finished result with logged exclusions is not evidence that the excluded scope
was searched. Initial archive-discovery warning propagation and other-format/native/candidate checks remain open.

Private root: `artifacts/release-evidence/i115-archive-result-search-20261004`.

| Evidence | SHA-256 |
|---|---|
| Valid four-case baseline XML | `4332ad5761238bb857f0ee56e2539f3e076a9262034e0adb5191436c7d9f268b` |
| Eleven-case corrected Core XML | `55a42cff998bb2c38c15d74788078f6a8953883d1dad005619c3054f0852adcb` |
| Two-case corrected App XML | `c8c04194990f07666f3a255c3bdbdb70b375ed93e7cd9bd8d85a19c43f49dcc7` |
| Full Core / App XML | `6894151ea06948d332e91437d7dea1c875207212257056ac86101f393c7a5c61` / `b076bce6ebc5f99602a4d45ca07074cc8460a3b8e320779ef1770612ae472d33` |
| Final source/payload manifest | `062d381035b9221443b555d41963e03804b46434f7f2ef20dafc54da7cb5ea4a` |
| Independent input/XML/skip inventory | `c564d67bfb3e003543fa63440b3b4ef913a6a2ab5ba4d815b6f3df69f358a22b` |

Clean successor `ff8746a3dd11c1ee7fe040e1d9263aaa9dbfc468` passes all four required
[CI lanes](https://github.com/benny-cz/FileCat/actions/runs/37174198419); three tag/manual package jobs skip.
The Windows XML directly verifies Core 724 pass/47 skips, App 264/15, platform 165/33 and Remote 88/28,
including all 13 affected cases. Linux and macOS App XML each verify 243 pass/36 skips and both new Find flows.
All six direct XML inventories and three complete artifact ZIPs match their server-reported SHA-256 digests.

The same clean source passes all **13 controls without skips** in the elevated Windows 26300 VM
(UUID `9D224D56-1161-A849-ABA7-2581A980895C`), ending 2026-10-04 03:36:22 UTC. All 683 payload files,
684 ZIP members and eleven committed source-content copies independently verify. Controller 12140 and
workers 652/12520 are absent and the owned temp folder is empty at 03:37:55 UTC. Native OS execution of
headless controls is not native desktop input/frame evidence. A guest-locator separator correction is retained
as an additive request document; package bytes are unchanged.

Private successor roots: this record's `clean-ff8746a` and `artifacts/release-evidence/ci-37174198419`.

| Successor evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `bde51f4b1d25bcf333f900d74171e9badf6d29d8e668155e86a281ceb2742baf` / `e5b299507500ac805d70636f16cff2d565fe222b2816e0d0a42929087afd2fdb` |
| Independent CI inventory | `7b5158bc4cd069fed49063272173636716975e73ce86a66f81898138e91bf2a3` |
| Guest ZIP / input manifest | `424c2704a2fdbf0e20c1035805d001c43146222b58184a72eab12f4961be6ed1` / `75a5905cf20135766be9cf5a00e02052782792f762091fb18a8fef973d992549` |
| Guest runner / cleanup observer | `17d484bf4316b76d10818aa86fa4798d6fecb248e6347ca53d1a678587c64b85` / `a6d3e09fa345a18d14ba1c2cd517769cca8a2d3b43f91cc38e1d907deb230797` |
| Guest Core / App XML | `f1e1e9e226fcdc03c8f1dd206350c3a3584451f7c6e6d332bfe9ec140e0f4dc8` / `bc057d0295be1a1bb9dab4e14ae63384fa5e6a9a3e259c9f200d6d6e83ba54fa` |
| Guest cleanup result | `65d738ad29e330a7d013738301e46e0036efde78147e8e67ecf694d681dfc20e` |
| Independent native inventory | `6791b40ea406d07ef27254456fcd598f31ca0fd2df308126f30aa0a5d6ae7183` |

Remediation verified preliminarily on clean source. Other formats and initial archive-discovery warning propagation
remain to validate. Exact candidate and native presentation/AT checks remain required. USB G6 quarantine and
overall **NO-GO** remain.
