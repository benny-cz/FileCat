# E-I349 — Unix picture decoder lifetime after parent death

2026-10-11 CEST. Actual **97109700ef7345c0ff2493633eae229bc7b6b0b2 /1589 canonical raw blobs** reproduces the gap on Ubuntu and Mac: the original decoder survives three seconds after its owned parent is killed while a separate bounded holder keeps encoded stdin open. Both normal disposal controls pass. Standard output/error use regular owned files, so neither stdin EOF nor broken output explains the result. An earlier eight-case pipe prototype establishes feasibility only.

The corrected launcher retains a dedicated pipe writer and gives only its reader to the Unix decoder. A background reader starts before Linux's all-thread process policy or encoded input; parent-channel loss terminates the decoder process. Kill/disposal and failed launch close the parent writer. The decoder marks normal completion without synchronously closing a pipe another thread is reading; the reader releases its own handle after its read finishes, and normal process exit closes remaining handles. Direct standalone worker invocations keep their existing protocol; Windows token/job behavior is unchanged.

The final **declared four-file overlay at v3** passes **ten host/twenty native lifetime unit results and twelve actual native process controls**: parent kill with independently held stdin, live Kill/Dispose, exact complete two-pixel PNG output, twenty failed starts without descriptor growth per platform, and malformed-channel refusal with zero encoded bytes consumed. The final host picture selection preserves **66 predecessor results: 60 passes/six exact skips**. Native picture selections preserve **128 predecessor outcomes/messages/four exact skips**, giving **148 total native results: 144 passes/four skips** including the new tests. Linux thread snapshots include the lifetime reader under the installed process policy.

The original proposal's broader Windows App child completes **1652 results: 1621 passes/31 exact skips**, preserving the prior full inventory. Its host launcher times out at 900 seconds; independently observed child completion is exit zero, and that wrapper timeout remains adverse. This is not a final-overlay full native App-suite claim.

Every failed attempt remains: the first Mac observer changes descriptor flags incorrectly; a portable owned dup/close observer replaces it. The first lifetime proposal hangs on healthy shutdown until the bounded parent ends. An early Mac payload omits universal runtimes/osx assets and its healthy worker exits 134. New unit fixtures initially exceed Unix domain-socket path limits; short unique owned paths correct setup with unchanged assertions. The first independent reader mismatches xUnit CLI quote escaping/direct reason text; its corrected reader retains raw names and exact reasons without rerunning producers.

Independent postchecks rehash **2050 native files**, verify absent owned live processes and all six final owned sockets, and retire empty scratch roots. Three host scratch roots/seven files are archived, hash/exclusive-open checked and restored. Ubuntu UID 1000 and Mac UID 501 use pinned .NET 10.0.12. No host UI, physical source, package or persistent setting changes occur.

This preliminarily remediates the decoder root's launching-parent lifetime. **I08 stays open** for detached/reparented descendants, exited-root cleanup, filesystem/network/memory/UID authority, indirect/native parser integration and candidate qualification. This overlay needs its separate exact committed/original CI follow-up. I106/I110 physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence; nested records retain exact sources, commands, raw failures/skips, byte checks and restoration.

| File | SHA256 |
|---|---|
| `picture-parent-lifetime-safety-20261011-v2/independent-final-v1.json` | `f59443931e0dcc3dfb1ec468598a2bb98ef1f116ef867ba5a78d173d892d72cc` |
| `picture-parent-lifetime-safety-20261011-v2/retained-tool-sources-v1.json` | `cc891b69020e0bc0359bd3ff19507a893a0e8f47a1a6a5f86a202074d060fce8` |
