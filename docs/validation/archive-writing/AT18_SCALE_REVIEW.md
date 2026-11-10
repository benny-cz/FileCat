# AT18 scale review: the 100,000-member limit

10 November 2026. Plan §9.2 says the initial write budgets "require measurement" and that changing one "requires resource
tests and an explicit profile version/limits review". This note is that review for the findings of the first AT18 runs. It
records what failed at the member limit, what changed and why each change keeps its safety property. The evidence of the
runs that pass after these changes is in the implementation status.

## What the first runs found

The opt-in scale test creates a TAR from a tree of 99,999 members (the tree, 498 folders and 99,500 tiny files) through the
real Unix job route with real worker processes, assesses it, adds the 100,000th member and checks that one more is refused
before anything is written.

1. **The heap charge made the limit unreachable.** Capture, planning, review and the TAR plan each charge their retained
   objects against one 192 MiB ceiling before allocating them. At 100,000 members those conservative charges add up to
   about 3 KiB per member plus names, so creation was refused as over budget well before the member limit. The measured
   managed heap of the same creation was about 240 MiB at its peak, below the charge, so the charges stay conservative.
2. **Every recheck of an input re-hashed it.** Capture observes each input completely (identity, metadata, content
   SHA-256). Preparation and publication then rechecked every input about six more times, each by reopening it through its
   folder chain and hashing its bytes again. On Linux each recheck also re-read and parsed `/proc/self/mountinfo` into a
   fresh 1 MiB buffer, several times per input.
3. **The worker's CPU allowance had no per-member term.** It was 30 s plus 2 s per MiB of real input: 100,000 tiny inputs
   received about 31 s.
4. **The worker transport cost about 1.5 ms of CPU per tiny member.** Each protocol frame (header, payload, flush) was
   three synchronous handoffs to the output thread, and every input read allocated and cleared a fresh 64 KiB array.

## Changes and their safety arguments

- **Retained-heap ceiling 512 MiB (charged).** Every per-object charge is unchanged; only the ceiling rises, so the model
  remains an upper bound of actual use (measured peaks are recorded with the AT18 evidence). The ceiling is a finite
  admission bound, as before; the worker's own memory limits are unchanged.
- **One input recheck before publication, by metadata.** Capture still hashes every input completely; the transform's
  receipt and the fresh verification bind the archive's bytes to those digests, so a later content change cannot reach a
  published archive. Plan §7.1 ReadyToPublish rechecks provenance and outer metadata: each publication path (new file,
  Unix exchange, Windows transactional update) now reopens every input exactly once, immediately before its claim,
  exchange or commit, and compares identity, length and the complete outer-metadata digest (owner, mode, times, link
  count, flags, ACL, extended attributes and provenance; on Unix also the change time, which a write always updates).
  The other steps keep their structural checks without reopening. F6 source removal still reviews content completely
  before it removes anything.
- **Mount recheck without mountinfo.** A held directory pins its mount, and the kernel frees a mount ID only when that
  mount is destroyed, so the recheck compares `statfs` type and flags and the `statx` mount ID with the observed volume.
  A folder opened below the job's selection root, which stays open for the whole job, shares the root's verified volume
  when its mount ID matches. A first observation still reads mountinfo, now into a buffer that grows from 64 KiB.
- **CPU allowance per member.** Transform and fresh verification add 5 ms per member of the parent's own expectation
  (output members plus edited source members); the 2-hour cap is unchanged. Assessment of an existing archive keeps the
  byte-based allowance because the parent does not know an untrusted catalog's size in advance.
- **Transport.** Writes are buffered until a flush or a full 64 KiB chunk, so a frame is one write and one flush of the OS
  stream; a flush still returns only after the OS operation completed, and the output thread clears exactly the bytes it
  wrote. Input reads reuse cleared buffers; a consumer clears the bytes it was given before a buffer is reused, and
  cancellation never clears a buffer the OS may still be using.

## Measured after the changes

The limit run (99,999 members created, assessed, the 100,000th added, one more refused before anything is written) with
real worker processes, opt-in lane `FILECAT_ARCHIVE_SCALE=1`:

| Candidate | Create | Assess | Update | Managed heap (sampled, includes garbage) | Result |
|---|---|---|---|---|---|
| Mac, p4 (before) | 975.5 s | — | stopped by the 20-minute guard | — | Failed |
| Mac, v14d | 519.7 s | 19.2 s | 153.1 s | 106 MiB before, 696 MiB peak | Passed |
| Mac, v14e | 518.1 s | 19.3 s | 151.4 s | 99 MiB before, 702 MiB peak | Passed |
| Ubuntu VM, v14d | 541.3 s | 129.4 s | 534.0 s | 49 MiB before, 722 MiB peak | Passed |
| Mac, v15c (other suites in parallel) | 507.9 s | 19.2 s | 149.9 s | 102 MiB before, 712 MiB peak; retained 506 MiB | Passed |
| Ubuntu VM, v15c (other suites in parallel) | 544.8 s | 131.1 s | 611.4 s | 51 MiB before, 791 MiB peak; retained 294 MiB | Passed |
| Windows 11 VM, v15b (AppContainer, TxF) | 2,176.3 s | 310.7 s | 1,117.7 s | 13 MiB before, 511 MiB peak; retained 257 MiB | Passed |
| Mac, v15c, alone | 478.8 s | 19.2 s | 146.0 s | 16 MiB before, 713 MiB peak; retained 492 MiB | Descriptor bound exceeded by one (no warm-up) |
| Ubuntu VM, v18, alone, warm-up | 519.8 s | 126.2 s | 575.0 s | under 1 MiB before, 621 MiB peak; retained 243 MiB (checking) | Passed |

The archive is 204,543,488 bytes. Phases on the Mac (create): Preparing 220 s, Rebuilding 110 s, Checking 120 s, Saving
69 s; (update): reading the archive 18 s, Preparing 31 s, Rebuilding 69 s, Checking 33 s, Saving and finishing 1 s. The
Ubuntu VM's worker phases run about four times slower than the Mac's while its capture is faster; the update spends
120 s reading the archive, 266 s rebuilding and 133 s checking. The sampled heap includes uncollected garbage (it drops to
97 MiB at the next phase), so it is an upper bound of retained state, not a measurement of it; the charged retained
ceiling of 512 MiB is the admission bound. The Ubuntu peak working set (2,139 MiB) is the whole test process with other
tests running in parallel and is not attributable to the job. Descriptor counts return to their starting value.

The payload extremes first ran into an AW7 defect: the review of an existing `.zst` (and every standalone file) refused
before it could be replaced, because the review's preservation check built a change-free plan, which a compressed file
cannot have. The review now admits the one payload and its wrapper directly (fixed in v14e; the scale lane and a new
always-on native test cover it). On the Mac (v14e) a 512 MiB incompressible payload then took 30.7 s into a new TAR.GZ,
43.8 s for the added 64 MiB file, 12.7 s into a new standalone `.zst` and 7.1 s for the replacement, and 256 MiB of zeros
(beyond 1000:1) was refused by the fresh verification in 1.5 s with nothing published or left beside the inputs.
Ubuntu (v15c) took 75.3 s for the incompressible TAR.GZ, 92.6 s for the added file, 29.9 s for the new `.zst`, 20.1 s for
its replacement, and refused the zeros in 2.9 s.

## Retained heap and the charge model

"Retained" is the managed heap after a full collection, sampled every 15 seconds (and, with
`FILECAT_ARCHIVE_SCALE_RETAINED=1`, at every phase change); it includes whatever else runs in the test process, so only the
runs alone measure the job. On Ubuntu (v18, alone) creation retained 212–223 MiB at its phase changes and 243 MiB at the
sampled peak, while checking: about 2.4 KiB per member, below the charges of about 3 KiB per member plus names, so there
the charged 512 MiB bounds what is retained. On the Mac (v15c, alone) the sampled peak was 492 MiB with a 16 MiB baseline,
about 4.8 KiB per member: above the charges. That run predates the phase attribution, so the macOS-specific part (a
larger origin record per input is the first suspect) is being measured before either the charges or the code change; the
shipped ceiling stays as reviewed, and macOS writing cannot be qualified under the current plan anyway (platform position).
Measured alone, the first job also opens the runtime's own descriptors, which exceeded the descriptor bound by one; both
scale tests now run a small warm-up job before taking their baselines.

## Windows

Windows creation took 2,176 s in the VM, four times the Mac's: 1,056 s were the capture alone. Each access to a captured
input pinned the selection root's full path from the drive root three times (a new lease and two namespace checks, about
thirty opens per access for a tree in the temporary folder), and every input is accessed several times. From v17 the
folders below the held root are pinned relative to it, each verified against its captured identity, and the root's full
path is re-pinned at most once a second: its lease holds every name of that path without delete sharing, so it cannot
change while held, and the held handle is checked on every access. The VM's numbers above predate this change and were
taken with Defender's real-time scanning on for creation and assessment; the measurement on the changed code is open.
