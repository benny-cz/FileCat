# E-I141 — Picture lifetime fixture holds the read by ordinal

Verified preliminary test correction, 2026-10-05. Required CI on clean
`8f75856802668f7d21c09f33aa876e4fdc4409d3` fails its Windows ARM64 App lane:
Closing_a_picture_retires_demand_before_its_active_source_read_returns(false, true)
expects two reads at held entry and observes three. The other three required lanes pass.
ARM64 App reports 346 passed, one failed, 17 declared skips; package startup/installer
checks are skipped after failure. This run does not satisfy the required CI gate.
Three server artifact digests/six complete TRX inventories independently verify. Native
ARM64 has the full job log totals/failure, without a per-case TRX artifact.

The test holds and fails the second source read. Viewer text rendering and encoding
detection can independently request the same uncached 64-KiB header; PagedReader admits
both before cache insertion. Thus an absolute read ordinal cannot identify the decoder
feed. The historical ARM64 ordering is not traced and is not claimed as known.

A controlled prior-header read against the original fixture reproduces three host failures:
two viewer cases hold the header before PictureLoad exists, and quick view waits on the
wrong held read. The unchanged original file is restored before the correction. These
controlled failures establish the fixture assumption, not the exact ARM64 interleaving.

The fixture now identifies the actual 1-MiB decoder feed separately from 64-KiB header
calls. The four original combinations and four prior-header controls still require one
held feeder call, prompt close/cancellation before release, no disposal during the held
read, one disposal after return, no further source reads, no published bitmap after close
and unchanged owned PNG bytes. Injected read failure now targets that feeder call.
Production behavior is unchanged; no assertion is reduced to a timing retry.

Affected picture lifetime/device-demand/direct-content tests pass **20/20 with no skips**,
including all eight held-feed cases/four new prior-header controls. Full host App passes
**345/368, 23 declared skips**, command exit zero. Complete case inventories and outputs
verify independently. Clean successor CI and native ARM64 revalidation remain required.
This headless host evidence does not supply native desktop or candidate qualification.

Private evidence lives in the authorized second workspace's
FileCatReleaseEvidence/mac-resume-20261005/i141-host-v1:

- `independent-host-v1.json` SHA-256 `814bfb9f495c26c368baf87debc0678d1373a9b46dce1145f9d7476a8cf41256`.
- Controlled test SHA-256 `3e7e4e9652beb33510c12e451ef2e074fc022c2bbf359e3cc29323a5a90c05fc`.
- CI 37354452437 independent proof in FileCatReleaseEvidence/ci-37354452437:
  `ee60fcb662a62e1e8582ac73193e43cec4b24a8765010de5bcd11671fb378516`.
- Preceding documentation commit `300c52ada77244b0679c05b1874ee4a4c5306c7c`,
  CI 37350516988 passes all four required lanes, four server digests/six inventories;
  proof `87e23f774db19799eb5777d9500747494f558633b1f33036a4b63c0090f146b4`.

I141 is remediated preliminarily, not Closed. No candidate or human GO; **NO-GO** remains.
