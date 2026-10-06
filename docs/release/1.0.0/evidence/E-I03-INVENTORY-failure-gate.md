# E-I03-INVENTORY — reject failed or inconsistent package inventories

2026-10-06, source baseline `145f569`, working script correction. I03 remains
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
hosted revalidation continue; no candidate, source device or publication.

Private `FileCatReleaseEvidence/inventory-validation-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-original-inventory-failures-v1.json | eacb15070d969f5d5f6603a7e1bc844ba0ac020513c67bb5d7e26627c7f28694 |
| corrected-controls-v1/independent-inventory-controls-v1.json | bec90f11f93f02225f6d6321e81dedba4ab77ba6f9f11c10d882a20fda422168 |
