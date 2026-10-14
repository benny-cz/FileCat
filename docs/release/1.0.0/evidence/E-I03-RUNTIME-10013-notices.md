# E-I03-RUNTIME-10013 — what a refresh to runtime 10.0.13 would need

I03/I14/V20, 2026-10-15. **Measured, not applied.** The packages stay pinned to the reviewed 10.0.12; this records what stands in the way of 10.0.13 so the decision is a short one.

## Why it came up

`global.json` now selects SDK 10.0.101, whose bundled packs are .NET 10.0.1, so `Directory.Build.targets` pins self-contained packages, apphosts and ReadyToRun code to a serviced runtime. The first attempt pinned **10.0.13**, the current release, and the ARM64 CI job refused it: `eng/DependencyNotices` requires every published runtime pack to have a reviewed notice snapshot in `licenses/dependencies`, and that snapshot holds **10.0.12** for all five RIDs. The pin is therefore 10.0.12, which the notice gate accepts (50 verified notice files, locally confirmed).

## What was measured

The five 10.0.13 runtime packs were downloaded from nuget.org and their notice texts compared with the texts the repository already retains for 10.0.12:

| RID | `LICENSE.TXT` | `THIRD-PARTY-NOTICES.TXT` |
|---|---|---|
| win-x64 | 1,139 bytes, **identical** | 78,041 bytes, **identical** |
| win-arm64 | 1,139 bytes, **identical** | 78,041 bytes, **identical** |
| linux-x64 | 1,116 bytes, **identical** | 76,623 bytes, **identical** |
| linux-arm64 | 1,116 bytes, **identical** | 76,623 bytes, **identical** |
| osx-arm64 | 1,116 bytes, **identical** | 76,623 bytes, **identical** |

**Ten of ten texts are byte-identical** to the reviewed 10.0.12 texts, by SHA-256. A refresh would therefore introduce no licence or notice text that has not already been reviewed.

## What still stands in the way

Two fields of each `RuntimePacks` entry are provenance values this campaign did not set itself and should not guess:

- `NuGetContentHashFromMetadata` — the package hash as nuget.org's own metadata states it, which has to be read from that metadata rather than computed locally.
- `OriginalPublishedDepsSHA256` — the hash of the `.deps.json` a package publishes with that pack, which only exists once something is published against 10.0.13.

The archive hashes themselves are already measured and retained with this record. Beyond the fields, the snapshot is a reviewed artefact and `index.json` says a changed one must be regenerated **and reviewed**; that review is the owner's, which is why nothing here was edited.

## What it means

Pinning 10.0.12 keeps a serviced security release — it is nine months newer than the SDK's own 10.0.1 packs — and loses only the 2026-10-13 patch. Moving to 10.0.13 is a decision plus a snapshot refresh whose licence review is already satisfied by the byte-identical texts above. I03 and I14 remain open.
