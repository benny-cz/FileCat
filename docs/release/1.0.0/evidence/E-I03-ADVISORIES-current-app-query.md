# E-I03-ADVISORIES — current App package advisory query

2026-10-06. Exact source `850298359fc16cf28503cea42f3ba9c0b1599240` and its
actual owned App restore graph. Preliminary I03/V20 evidence, not audit closure.

SDK 10.0.401's read-only `dotnet list package --include-transitive --vulnerable
--format json --no-restore` query exits zero with empty stderr and no listed
vulnerable packages, using the configured official NuGet source. Its selected
App graph has 38 exact package identities. Project/lock/actual assets/NuGet config/
SDK config bytes remain unchanged. The first private parser expected a frameworks
key; the successful no-findings report contains only the project path. Corrected
interpretation uses the same report without rerunning the query. The command's
[documented transitive/audit-source semantics](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list)
define this limited scope.

Separate no-cache HTTPS requests retain the official service index, vulnerability
index and both vulnerability pages, response headers, request times and exact wire
bytes. Server-declared page updates are 2026-09-26 05:43 UTC and 2026-10-06 05:44 UTC.
The 38 App identities select twelve historical advisory ranges; the pinned SDK
NuGet.Versioning library compares every range against the exact resolved version.
Six inclusive/exclusive/prerelease boundary controls pass; no current version falls
in an observed affected range. This independently agrees with the SDK report.
The [official vulnerability API](https://learn.microsoft.com/en-us/nuget/api/vulnerability-info)
describes the partitioned index/pages; server timestamps are retained observations.

The first private feed observer misread a successful gzip-encoded response as
UTF-8. Original wire bytes/headers/failure remain; v2 decodes those same index
responses and retains subsequent wire plus decoded pages. No product failure,
package update or test rerun is inferred from these observer mistakes.

No findings in this finite feed/query is not proof of no vulnerability. Native
static components, implicit SDK/runtime packs, OS components, unknown/unlisted
advisories, exploit reachability, RAR/license/provider questions and exact-candidate
audit remain separate. I03 remains Open; no dependency or product source changes.

Private `FileCatReleaseEvidence/dependency-advisories-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-app-advisories-v2.json | 95d719b7b9b981820971858a0fb68f0b0a4d4c72c41adfe13861d20f1265a1f3 |
| fresh-feed-v2/independent-fresh-feed-v2.json | fe126a735bf5e2f0577b0bce282e6d273bac18a3b91c692c0178bbe103762a97 |
