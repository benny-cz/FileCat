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

## Still to measure

Windows (the AppContainer and TxF route), payload extremes (an incompressible 512 MiB payload through TAR.GZ and a
standalone .zst, and 256 MiB of zeros, which the 1000:1 expansion rule refuses), and the timings and peaks of the final
candidate on all three platforms.
