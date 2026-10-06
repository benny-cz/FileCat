# E-I03-INVENTORY — reject failed or inconsistent package inventories

2026-10-06, source baseline `145f569`, committed correction `1d6d53f`. I03 remains
Open; this App package list is not a complete per-artifact SBOM.

The original Windows inventory tail pipes `dotnet list` straight into its final
JSON file, does not check the native exit code or parse the output, then reports
Done. Two isolated controls execute that unchanged production tail with a native
substitute command. Exit 23 plus malformed output, and exit zero plus malformed
output, both write invalid inventories and return process exit zero/Done. This
proves the failure-propagation gap; no actual SDK or full-publish failure is
claimed for those substituted controls.

`eng/Write-PackageInventory.ps1` now checks native command exit, schema, exact
project/framework, transitive scope, unique package identities, resolved versions
and direct/transitive categories against the committed net10.0 lock. Listing
uses `--no-restore`. It validates before atomically replacing the previous file;
temporary output is removed on failure. The Windows packager calls it before
creating either ZIP. Its comment no longer asserts completed OSI eligibility.

Two actual SDK 10.0.401 list commands pass with all 38 packages (four direct,
34 transitive), using the previously verified raw-source export's final App
restore graph. Those commands share that graph; the case labels do not establish
independent per-RID graphs. Twelve fresh substituted-command controls reject
nonzero exit, malformed JSON, changed schema, missing/wrong project/framework,
missing/extra/duplicate/version/category package and missing transitive scope.
Every refusal preserves the exact prior inventory and leaves no temporary output.
Project/lock/assets/new scripts stay unchanged through controls. Independent
ordering and PowerShell parsing pass. Committed full production packaging and
hosted revalidation follow below; no candidate, source device or publication.

Private `FileCatReleaseEvidence/inventory-validation-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-original-inventory-failures-v1.json | eacb15070d969f5d5f6603a7e1bc844ba0ac020513c67bb5d7e26627c7f28694 |
| corrected-controls-v1/independent-inventory-controls-v1.json | bec90f11f93f02225f6d6321e81dedba4ab77ba6f9f11c10d882a20fda422168 |

Exact source `1d6d53f07235cc87f84dfeee91b1a7f66d237a24` runs both authoritative
Windows production packagers from a verified raw Git export, development-only
version `0.0.0-i03inventorycheck`. Both commands exit zero and record the new
38-package validation message. Independent JSON inspection matches every
locked ID/version and four direct/34 transitive categories. All four SC/FDD
payloads and portable/FDD ZIPs preserve the 50 raw committed notice files;
portable broker exclusion/FDD inclusion and all original input pins verify.
No installer compilation/installation or native UI qualification is claimed.

Original push CI 37487426601 attempt 1 passes all four test lanes and ARM64
startup/draw/installer checks. Nineteen server digests, four clean SDK receipts,
fourteen complete TRX inventories/all App identities and picture/reference/draft/
set controls verify. Four builders retain 92 actual locked graphs for all 23
projects, independently matching source. Package/draft jobs skip. Independent
development 37487590559 has passed all test lanes and continues actual Unix
packaging; its final artifacts are not yet qualified here.

Private `FileCatReleaseEvidence/inventory-production-windows-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-windows-inventory-packaging-v1.json | 44aeb681232d7a663165fc01b5cec0cea9fcf2371e9d5adcccdc0d44ff44fabc |

Private `FileCatReleaseEvidence/ci-37487426601-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 5f64d302f41912649d057f0216df54d2af7b590b8cc4f522193b66cececcec26 |
| independent-fixture-ci-v1.json | ef4a3e24618ce0996f5c6a5b5288ae56e36e5351e108a25d4c2ae62135f4567b |
| independent-producer-policy-ci-v1.json | f2d2a027a9cb6f3669916fc6d03a00531780d192193f394d14a527aa40dc0ec8 |
| independent-draft-guard-ci-v1.json | 1f3cdb8390a109dd894e61a420be2c2042f570288a44604ed100071ce6606133 |
| independent-separation-ci-v1.json | 0f3ebc22c45fe95e5c9fbfc45c1344df674c6d3d84cb1b5c888c0c466611f986 |
| independent-restore-ci-v1.json | 6fad2bae55eeeaeced03455dd52ad9b483e5f529eda285a94abe4086a30daaf9 |
