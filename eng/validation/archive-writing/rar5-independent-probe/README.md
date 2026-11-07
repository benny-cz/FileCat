# Independent compressed RAR5 feasibility probe

Private validation only. This directory is not a production encoder, worker registration or writable provider.
Run live probes only on the authorized VMs/Mac, after checking for other testers. Batch generation, both readers,
boundary/regression checks and evidence collection in one guarded session when qualifying a new candidate.

`prototype.py FRESH_OUTPUT` generates eight bounded fixtures using FileCat's original fixed-table LZ/Huffman
encoder. Each nonempty fixture is forced through compression, including noise and tiny input; the empty member
is stored. The matcher uses one hash candidate, at most 32 KiB of history per block and 4,097-byte matches. It does
not call a third-party compressor or a vendor executable. A complete canonical code is emitted for each table.
The experiment limits inputs to 1 MiB and outputs to 2 MiB. Its Python assertions and whole-fixture buffers are
experiment checks, not production security controls or the planned streaming C# implementation.

`verify_native.py OUTPUT [EXACT_LIBRARY]` compares content and UTF-8 names, Unix mode and full nanosecond mtime
through an independent libarchive implementation. The Mac system's libarchive 3.7.4 discards RAR nanoseconds, so
it cannot qualify that metadata. The recorded run uses a privately built, SHA-256-pinned 3.8.11. No system reader
was replaced. Use its exact library/build identity from the evidence record.

The portable `FileCat.Rar5Feasibility` project verifies the same archives through FileCat's existing public reader,
including exact content and the reader's documented 100 ns display precision. It references the existing reader
assemblies for testing, including the baseline RAR1.5 assembly; those are not dependencies of the new encoder or
permission to import them into the writing-only worker.

Wire facts were checked against FileCat's existing BSD-2-Clause rardecode port and the published container format.
The new encoder's matcher, fixed-table strategy and serialization are original FileCat work under the repository's
MIT licence. Retain the existing rardecode attribution/licence as recorded in `licenses/rardecode`; this is not a
clean-room claim. No UnRAR sources, restricted ports, vendor binary disassembly or vendor compressor outputs were
used to construct the encoder. `reference-sources.json` records exact inspected inputs; upstream raw snapshots
remain private research evidence, not new runtime dependencies.

See [the feasibility decision](../../../../docs/validation/archive-writing/RAR5_FEASIBILITY.md) for results, the
compression-method coverage gaps, bounds, remaining legal/source obligations and the production implementation gates.

The later [C# checkpoint](../../../../docs/validation/archive-writing/RAR5_CSHARP_ENCODER.md) tests the stream-only
implementation in `src/FileCat.Archives/Writing`. `Rar5WritingTests` exports six 14-member archives when given a fresh,
absolute `FILECAT_RAR5_CODEC_EXPORT_ROOT`. `verify_csharp_native.py` reads those outputs through the exact private
Mac/Linux libarchive; `WindowsNativeRarOracle.cs` plus `verify_csharp_windows.ps1` use the Windows guest's system
UTF-8 C API, avoiding the stock CLI's filename-locale limitation. Neither reader extracts untrusted archive paths.
PowerShell witness/inventory JSON must be written explicitly as UTF-8.

`run_csharp_unix.py TASK PACKET_SHA BATCH [--continue-after-tests]` guards idle machines, uses complete fresh source
roots and batches builds, broad regressions, native verification, source snapshots, evidence and cleanup. Continuation
requires the already passing TRX and retains earlier failures. The Windows script records the v2 packet hash and
task/account paths; it also needs the native helper/script and explicitly reviewed audit-tool source supplied beside
that packet. Update all pins/paths together for a new batch. Do not overwrite prior batch roots/evidence. The committed
runners incorporate harness corrections; the frozen evidence retains the actual executed versions and failed attempts.
No runner or completed codec test registers a production writer or proves password support.
