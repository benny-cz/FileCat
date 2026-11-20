# Rolling-history policy and diagnostics subset

`AnalysisPreferences` contains diagnostic-safe preferences only. Rolling history defaults off, with10 successful analyses,30 days and1GiB as the fixed defaults. Configured caps remain finite; implementation ceilings are the4096-entry catalog bound,3650 days and the8GiB inventory-byte bound. This packet does not yet capture or prune history or expose a public enable action.

The bounded planner combines age, count and byte pressure, with deterministic oldest-first proposals. It validates identities, positive complete lengths, duplicate/incoming IDs and both declared and enumerated collection bounds. Arithmetic uses wide counters. An oversized or expired prepared incoming inventory preserves every existing record; no truncated inventory is substituted. Future timestamps pause automatic changes. Active/protected entries are never eviction candidates, and an unsatisfiable proposal preserves all existing records. Disabled policy does not propose deletion or claim the caps were assessed. Proposals contain data IDs rather than filenames, native handles, runtime approvals or source authority.

25 Core controls cover defaults, generated-settings round trips and legacy defaults, configured finite bounds, all cap boundaries, protection, clocks, order independence, large byte sums, malformed/lying collections, identity conflicts and cancellation. One actual diagnostics-ZIP control seeds private inventory/query sentinels, exports through the production bundle API, checks the four numeric/boolean policy fields and confirms private sentinels were neither exported nor modified. This is a privacy control for the new preference surface, not the complete SG04 audit.

All new controls pass Windows, Mac and Ubuntu. Applicable final broad results are:

| Platform | Core | Windows native | Rendering |
|---|---|---|---|
| Windows |1528 passed,48 platform skips,0 failed|75 passed|396 passed,1 platform skip,0 failed|
| Mac |1554 passed,4 platform skips,0 failed|not applicable|397 passed|
| Ubuntu |1554 passed,4 platform skips,0 failed|not applicable|397 passed|

The first broad run exposed test interference: catalog tests deliberately reserve the full64MiB process budget, while other catalog/maintenance tests expected a free allowance. Windows had3 failures and Ubuntu7. The production budget was not changed. Catalog, maintenance and owned-store tests now use the existing collection that disables parallel execution for shared-capacity controls.233 related controls pass on Mac/Ubuntu; Windows has232 passes and its exact Unix-only skip, followed by a passing full Core rerun. Windows' intermediate preflight stopped because it originally required that Unix-only case to pass; the final driver explicitly checks its exact skipped name/count. Failed reports remain in the evidence.

Only the Core test DLL/PDB changed for isolation retries; every product/native/render input was verified and reused. Windows' final driver-only rerun verified231 inputs, changed no binary and transferred8.9KB. Native/render broad suites were reused from the unchanged product candidate. [Source/build/results/cache/process evidence](source-manifest-r988.json) records all scopes. All task test processes exited.

Next implement the isolated native history store and exact guarded pruning, complete memory/indexed snapshot capture on successful completion, private-boundary exclusions and leases, preserving enable/change/delete reviews in Settings, history browsing/pinning and actual native lifecycle/negative tests. Do not treat `Enabled`, serialized preferences, proposed IDs or a passing planner test as mutation authority. The complete history/persistence/resource gates and final independent review remain open; public feature registration stays off.
