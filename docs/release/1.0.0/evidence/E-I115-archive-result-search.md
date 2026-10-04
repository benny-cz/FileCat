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

Remediation verified preliminarily on the working source; clean successor CI/guest execution is next. Exact
candidate and native presentation/AT checks remain required. USB G6 quarantine and overall **NO-GO** remain.
