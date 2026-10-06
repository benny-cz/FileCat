# E-I149 — RAR signature detection

Classification: preliminary remediation committed/pushed at
`313d40be9f84babe7bab704ce477f46bf6ca4113`; host controlled/full qualification passes,
clean Windows/macOS/Ubuntu controls and exact-source CI independently qualified.

Actual 9da5738 signature detection returns null for real RAR 4/5 and both modern
and legacy secondary-volume fixtures. It also returns null for the complete minimal
RAR 4/5 markers. Eight of fourteen fresh controls fail; six malformed/truncated
controls pass. The detector compares seven input bytes with a six-byte literal,
making the RAR test impossible. Known-suffix opening still works, so the earlier
forced-format I148 controls did not qualify signature-based opening. I149 is Medium
functional correctness, Must fix V13/explicit Ctrl+PgDn detection.

The correction compares the six-byte common prefix and then requires the complete
supported version suffix: zero for RAR 4, or one/zero for RAR 5. [RARLAB's format
technote](https://www.rarlab.com/technote.htm) specifies these seven/eight-byte
markers. The four real fixture controls, two renamed-file controls with independently
verified JPEG bytes and eight supported/malformed/truncated marker controls pass.
With identical final test DLL and only the archive DLL changed, original 9da5738
fails eight cases and passes 22 existing/new positives; correction passes all 30.
All payload pins, exact process exit and verified empty fixture-container cleanup
pass. Affected host 77/0 and full Core 818/56 declared skips/874 unique cases pass.
Signature recognition does not validate every archive byte or discover embedded SFX.

The first test compile fails on a nullable-value warning before any test executes;
the test-only correction uses GetValueOrDefault after the explicit kind assertion.
Original tool output/annotation remains; fresh baseline then reproduces eight actual
product failures. No failing product run is overwritten or relabeled.

Private evidence root: the authorized second workspace's
`FileCatReleaseEvidence/archive-variants-20261006`. Retained test and receipt hashes
are also listed in [E-I148](E-I148-legacy-rar-secondary-volumes.md).

The clean committed producer verifies 885 raw Git blobs; source ZIP SHA-256 is
`a6585645e5e782ac70e189e661e51ee68398b9bf1c506b40ae777b5db48418fe`,
producer SHA-256 `d284a46dc2782fbe0de0f3d152ba9d0a9376981da7c91f3b14cb920f3fe20d13`.
Windows 26300, macOS 27.0.1 arm64 and Ubuntu 26.04.1 each pass all 77 archive cases,
including all 30 new signature/legacy/UDF controls, with zero skips. Unchanged
331/330/331 native payload pins, exact inventories, owned process absence and empty
test temp verify. Native proof SHA-256 values (Windows/Mac/Ubuntu) are
`70d9a64397b75ae9dd93e1e418173cb28d71e32d16d890edfd2b610aba5c3c0b`,
`44c321b83f0668bf870396f3bdeba4acc0c6c0088b96912a0f2900fac8632f6f`,
`27a636ea5fd4dbdc95310f0acfe8f329d690517bc472bebd387422c2793d56f0`.

Exact-source CI [37394739441 attempt one](https://github.com/benny-cz/FileCat/actions/runs/37394739441)
passes all four required lanes. Four GitHub server ZIP digests and six complete TRX
inventories verify: Windows Core 817/57 declared skips/874 unique cases, Platform
166/33, Remote 88/28; App Windows 371/17, Mac 340/48 and Ubuntu 338/50, all 388 App
names exact. All 77 affected Core names and 30 new controls pass exactly; 868 Core
names match the host, with six source-declared native PE paths retained explicitly
as differences (five system-path casing and one test-assembly location). No names
are normalized. ARM64 Core 817/57 and App 371/17 plus package drawing/installer
checks pass by complete logs; no ARM64 per-case TRX or physical qualification is
claimed. A timed-out Ubuntu artifact download is retained before its successful
read-only retry. Independent CI proof SHA-256:
`e866795092964527199e1bf1afb54daf81205a534108ac2f428764353016fca0`.

The wider legacy actual component probe now verifies 42 complete search and 42
exact member-read controls across all seven aliases. Partial sets yield eight exact
reads and two explicit damaged-member refusals, with source/artifact pins and
process/temp cleanup unchanged. Observations SHA-256:
`ae6db9e1a8e6ddb66ab9fd624d86bdbccb934e395fe516d46c093b808d673143`;
receipt `25b8c43e92a83d2dd3249a53204cb7bb4745fbae90f794ba1fb6811e550d19c7`.
This keeps the explicit alias-opening scope separate from folder discovery.
Broader RAR/topology, native drawn UI/Find/extraction, distribution/legal and exact
candidate remain.
No user interaction is needed for this slice; owner needs stay queued until 08:40
CEST. Mac awake v3 is restored to the exact original settings; both VMs remain running and G:
stays untouched/HOLD. No human stable GO; overall NO-GO.
