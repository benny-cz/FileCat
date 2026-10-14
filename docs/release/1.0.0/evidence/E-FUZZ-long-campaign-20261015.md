# E-FUZZ — a long fuzz campaign over the untrusted-input parsers

V09/V07/V10/V23/V24, 2026-10-15, at commit `50d57c33`. **Four harnesses, far past their default depth, with no failure.** This is depth on the existing harnesses, not new coverage of new structures, and it qualifies no candidate.

## What ran

Each harness derives its damage from the round number, so any failure is repeatable by number and is kept as a retained round.

| Harness | Rounds | Scope | Result | Time |
|---|---|---|---|---|
| `RecoveryFuzzTests` | **30,000 per image**, 7 images (FAT12/16/32, exFAT, NTFS, MBR, GPT) — 210,000 damaged-image scans | boot sectors, tables, directory entries and MFT records, with a per-round allocation budget, a 20-second bound and a check that every offered item reads | 15 tests, **0 failed** | 11 m 56 s |
| `InspectorFuzzTests` | **30,000 per format** | damaged files must give reports with warnings, never exceptions | 18 tests, **0 failed** | 5 m 47 s |
| `ArchiveFuzzTests` | **2,000 per format** | damaged archives are refused or listed, never crashed on | 14 tests, **0 failed** | 57 s |
| `DiscoveryFuzzTests` | **50,000** | damaged answers from the network are ignored, never crashed on | 4 tests, **0 failed** | 4 s |

The defaults are 200 rounds for recovery, inspector and archive, so this is 150×, 150× and 10× their depth; discovery ran 250× its default. The archive harness writes real archives to disk and is I/O-bound: a 15,000-round attempt ran over an hour without finishing and was stopped in favour of the completed 2,000-round run.

## What it means

The recovery, inspector, archive and network-discovery parsers hold their contracts — a damage report or a smaller listing, bounded allocation, bounded time, no unexpected exception and no unreadable item offered as readable — at this depth, on this host, at this commit. It adds no new structures to the corpora, exercises no native decoder beyond what those harnesses reach, and repeats nothing on another platform: the hosted CI lanes run the same harnesses at their defaults on Linux and macOS.

Retained rounds from earlier findings (I28's negative `$Bitmap` size and nameless root record; I37's declared cluster counts and compressed sizes; the exFAT review round) ran again and still pass.

## Retained private evidence

`E:\FileCat\artifacts\release-evidence\fuzz-campaign-20261015-v1`, seven files, self-excluding manifest `sha256.json` SHA-256 `05b884939c2a138ceed53f1bd96e8937cfe42a0f63a8e71c9f2e41a015486d94`: each harness's run log and the two campaign scripts, including the round counts each stage was given.
