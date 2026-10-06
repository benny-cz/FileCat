# E-I149 — RAR signature detection

Classification: preliminary remediation committed/pushed at
`313d40be9f84babe7bab704ce477f46bf6ca4113`; host controlled/full qualification passes,
clean native and exact-source CI pending.

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
are also listed in [E-I148](E-I148-legacy-rar-secondary-volumes.md). Clean native
publication/CI and the broader actual component probe are next. Broader RAR/topology,
native drawn UI/Find/extraction, distribution/legal and exact candidate remain.
No user interaction is needed for this slice; owner needs stay queued until 08:40
CEST. Mac awake v3 is active with restoration due; both VMs remain running and G:
stays untouched/HOLD. No human stable GO; overall NO-GO.
