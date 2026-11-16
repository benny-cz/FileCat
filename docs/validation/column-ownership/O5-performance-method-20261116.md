# O5 performance method (declared before measurement)

One bounded control/candidate set measures O4 production e4804f03 against O5, using the identical baseline test
in column-ownership-o5-control and column-ownership. Build both first. Run sequentially, without other builds/tests.
SDK 10.0.101, Release, Windows x64/runtime 10.0.15; one warm-up and five timed runs, fixed 100k/1M listings,
one/two tabs and a 2 ms superseding sort. Preserve all samples, CPU/GC, allocations, memory and admitted coverage.
The original O1 and O4 measurements/misses remain intact. O5 specifically avoids building/publishing obsolete
intermediate views, and freezes keys in bounded chunks with a fast dense prefix and a sparse remainder.

Keep the existing timing allowance: max(control median +15%, +50 ms) for PF3. Added immutable key storage is expected:
allow at most 160 allocated bytes per admitted row per capture, plus the pre-existing allocation allowance of +5%.
For superseding runs charge every actually started capture, including canceled/replaced captures; record that count.
The allowance covers the 64-byte result object, chunk references and bounded auxiliary bookkeeping with room for GC
and construction overhead. It does not allow uncharged retention: reservations remain at or below the shared 512 MiB
hard limit, including existing 40*n indexes, simultaneous tabs and retiring work. No unbounded listing-sized key array.
Original feature-off PF1 and Owner-on PF2 bands stay unchanged; the full baseline fixture also records PF1 after O5.

In addition, an opt-in fake ownership analysis fixture measures the actual MetadataService/retained-analysis path
at 100k and 1M rows, one and two tabs, with one warm-up and five samples. No native ownership/name latency is inferred.
Measure read pass plus accepted sort, allocations, CPU/GC, private/peak/retained memory, actual sort coverage and the
shared reservation high-water mark. Fake processing must stay below 50 microseconds per listed row plus 2 seconds
per pass (7 s/100k; 52 s/1M per tab; two tabs have twice that total allowance). This bounds the complete background
pass rather than comparing a full read to the earlier fixed-key-only sort. Every row must be counted truthfully;
capacity-limited sorting is allowed only with a reported limiting resource and fixed unknown remainder. Reservations
must return to the pre-analysis baseline after retirement and the actual workers' return. PF4's small deterministic
capacity/lifetime tests remain the mandatory proof for each independently shrunk cap; large fake fixtures supplement it.

Repeat only after a concrete fix or to diagnose a failed metric; do not widen bands after observing results.
