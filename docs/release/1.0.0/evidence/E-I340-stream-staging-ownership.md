# E-I340 — stream staging cleanup requires creation ownership

2026-10-11 CEST. Baseline **c7d17dab4efe8fe848e733efd671a7510c85069a /1553 canonical raw Git blobs**, separately declared test/product overlays. This controlled High/Critical data-loss defect affects stream transfer failure cleanup: the baseline deletes a staging pathname even when the copy never created that file.

An actual `FileMode.CreateNew` collision, or a revision/length/cancellation/unexpected failure before creation, can leave an unrelated pre-existing staging file at the generated name. Baseline cleanup deletes its complete known bytes in all five collision controls. The product records ownership only after successful creation and requires that ownership at all four cleanup paths. Failures after successful creation still remove the copy's own partial staging file and roll back byte progress.

Identical assertions first retain **five unsafe failures/nine healthy passes** per producer, then **fourteen local and 42 native fixed passes**. The fourteen cases cover each pre-creation failure with/without an actual collision, healthy complete publication, and read/close/publication/verification failures after creation. Complete unrelated collision bytes, complete healthy output bytes, exact destination names, source closure, final state and zero failed-copy progress verify independently. No physical source or workstation UI is used.

Every **4032 affected predecessor outcome/message and 107 exact skips** remains: 820 local passes/17 skips, 1047 Windows passes/18 skips and 1029 passes/36 skips on each Unix platform. Windows measures high RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501 with pinned .NET 10.0.12. Fresh postchecks verify **1161 native files** and absent owned processes/temporary roots. Transport listeners stop; no package or persistent setting changes.

Host maintenance retains its original refusals separately. The first bounded cleanup removes two verified SDK logs, then refuses unexpected fixture images. The second retains its inventory/logs but legacy PowerShell cannot resolve Get-FileHash before deletion. A fresh PowerShell 7 check verifies fourteen reconstructable images and two remaining complete SDK logs, exclusive opens, exact inventories and no producer; it removes only those checked files and empty owned roots. Both roots are independently absent. Original failures and retained logs remain unchanged.

Creation ownership is a finite guarantee. Later pathname replacement, cleanup after topology changes, atomic/identity races and later cleanup identity guarantees remain open. [I341](E-I341-local-staging-ownership.md) corrects the separate local CopyExecutor path. [Exact committed/native qualification](E-I340-native-qualification.md) passes at e9f15bf/no overlays; [original hosted CI](E-CI-stream-staging-ownership.md) retains one Mac fixture failure independently corrected by [I342](E-I342-staging-fixture-ordering.md). Installed-candidate qualification remains separate. Physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, whole bytes and restoration.

| File | SHA256 |
|---|---|
| `i340-staging-ownership-20261011-v1/independent-remediation-v1.json` | `ae654348624f51c769aee6eed279a4ecf5e3eef108a90d0074ae4bad5e823032` |
| `i340-staging-ownership-20261011-v1/retained-tool-sources-v1.json` | `683fc0bc624c1c765a70662ed7f9e3bb94cbaedd4c8761f8a58b5e19ce8fff3a` |
