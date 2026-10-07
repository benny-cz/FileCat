# E-I188 — escaped GNU checksum names preserve actual Unix file identity

2026-10-07. High checksum identity defect under I06/V13/V15; remediated preliminarily at 4c86041d2b6df3b4a22c01299f47a6a5d2b43245. Broader formats, source revisions and final candidate qualification remain open.

Original d04a5d7 treats the literal Unix filename owned\child.bin as owned/child.bin. Two escaped GNU SHA-256/MD5 manifests falsely finish Completed with “1 verified, all match” for the directory lookalike, while the intended literal file differs. Two generated GNU manifests emit the wrong name and verify the directory lookalike instead. All four original Mac adverse cases fail; four ordinary and legacy-unescaped-separator positives pass. These controls use distinct actual 26-byte/28-byte owned files, the actual manifest parser and a read-only JobManager verification job with LocalFileSystemProvider; no worker/provider substitution or physical source is used.

GNU manifest writing now preserves and escapes literal Unix backslashes, and explicitly escaped GNU parsing preserves them during resolution. Windows and legacy unescaped backslash separators retain their behavior. SFV writing is unchanged. This finite correction does not establish all BSD/SFV, ambiguous-name, root-folder, ending-backslash or revision behavior; MD5 retains its existing compatibility warning.

Working Windows and fresh locked committed Windows builds each retain all nineteen prior manifest case names/outcomes plus eight new cases: 23 pass and four explicitly skip filenames Windows cannot materialize. The actual ordinary-user ARM64 Mac payload passes all 27 without skips. Escaped cases now select the intended 26-byte literal file and report Failed/“0 match, 1 do not match”; generated cases select it and succeed. Ordinary/legacy cases, traversal/absolute/invalid-escape controls, byte counts, exact independent Python SHA-256/MD5 and owned-file contents agree. Actual CLI results and before/after payload pins are retained.

The original prototype expects CompletedWithIssues after a single intended mismatch; its original failures occur earlier at the wrong parsed path. Inspection of the actual zero-match job contract requires Failed. Before fixed execution, only that future assertion is corrected, retaining the original prototype and four original path failures. The clean helper subsequently prints an App assembly name absent from this Core payload and fails after saving the successful command and all 27 cases. Its source, exact failure, payload and raw outcomes remain; a fresh reader independently seals them using FileCat.Core.dll without any build/test rerun.

Independent seal e3a636f2700052c0c4bd1a90a36225aa56633fd3c54c47d3eb2608b50e9d31ec checks all 53 retained files, 574 actual Windows/Mac payload references, all 1,098 original raw Git blobs/modes/archive members and all 1,100 clean committed members. Clean FileCat.Core.dll SHA-256 ad3166c8dce73d8e4299a9ad8d4be052e572917b8106e709bc8925aa94148514. Correction changes only the GNU identity implementation and eight new controls relative to parent 55c1383. Both owned Mac stages and bounded awake helpers are removed after nine result files per stage are retrieved and independently hash compared; no persistent settings, SDK, VM or USB changes occur.

Original push CI is pending. These actual filesystem/CLI controls do not qualify native GUI/human/reference/physical-source/installed-candidate behavior. No contract freeze, candidate or stable GO is claimed.

Private `FileCatReleaseEvidence/cm188-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-win-v1/command.json | f7c3b09ef2c8ea201aaf743acc10dabf51c58e7d7956b1d0cf0664c909ca8345 |
| baseline-win-v1/results/baseline.trx | 2c1178bdf1603fd2a6f27a515b99c968cc398f96c3559b7aa96a4638256e6793 |
| mac-baseline-v3/mac-command-v3.json | d51dace98f9552dcd8826f98836c7331994585289226c1cf0e1efde0b6113049 |
| mac-baseline-v3/mac-results.trx | 581cbf3f296572d73a8c656e6692b10056c3d9ef268058033809ef50eb339a23 |
| mac-payload-v1.json | 9ad9494dd029e48cc119df48b27ee2cbb42a4ebb820361ccf38aa5c004b3fccb |
| mac-payload-v1.zip | 5cb13e30b78339726bbfec5ce08911d89b7760b3f7520dbcd856426d800c288b |
| mac-baseline-restoration-v4.json | e7f4e3a9630442f1ac649ab63e5c416cb69a6e43dfcb92998033467d415ad639 |
| working-win-v4/command.json | b2a287fc3331ca1bf99e14b47f9c5473ca093318a6618aa4f92376cb24be1295 |
| working-win-v4/results/baseline.trx | fac6fbdbfa7f08ec0c734997630e577f00ecf895245611dfc6d93336e5c2f041 |
| mac-working-v5/mac-command-v3.json | b54a7fe50c0fdb65680cf82d454162197836187e634980ac1761f73496d94c97 |
| mac-working-v5/mac-results.trx | e46e01ec5039e1b331bb984fabd2b7451e2cbb7fc9ac6dc1e845e23d53644091 |
| mac-working-payload-v5.json | e3fa98900f979e8372b0b61706267e2b1ef725f7c572098db8dc1537ea3492d2 |
| mac-working-payload-v5.zip | d66e1a6315716eeb07d07d1f1b3f0b63b950a87173133feb1f0a736120687692 |
| mac-working-restoration-v6.json | ea8631bb30646cb22c45fcf2f62da04fe286657fd0494b92d3b6e5ff4192bf3e |
| independent-working-v6.json | 6d2869fdd740cedf0397c048b64db23ba875f4dd10a6d01c080ed9b4988e3149 |
| clean-v7/command.json | dd2da83042cbc2d076fa92c04078d7d31db061b5f8ebba7a0a63f9af054f86ce |
| clean-v7/results/clean.trx | 3cb19d3f3e887f2e5e1a2e7f25083099fcc7f72cf4891de0ed70915b86c1e0ca |
| clean-helper-assembly-name-failure-v8.json | 233a9ee3f3744f98cca18dcc31505cd8e8b6700e090c8aa6621423f96eae7a22 |
| independent-checksum-names-v8.json | e3a636f2700052c0c4bd1a90a36225aa56633fd3c54c47d3eb2608b50e9d31ec |
