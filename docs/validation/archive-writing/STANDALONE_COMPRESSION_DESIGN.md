# AW7: standalone GZ, BZ2 and ZST (release R3)

Plan §1.2 R3 and AW7: one-file compression and explicit single-payload replacement or edit commit for standalone `.gz`,
`.bz2` and `.zst` files. XZ stays out (AW8). This note fixes the rules before the code; the implementation status records
evidence.

## What the user can do

| Context | Action | Wording |
|---|---|---|
| One regular file selected | Create `name.gz` / `.bz2` / `.zst` next to it (or elsewhere) | "Compress file…" in the create dialog's format list |
| Several items selected | The standalone formats are not offered; the dialog suggests TAR.GZ / TAR.BZ2 / TAR.ZST | "Several items need an archive; choose TAR.GZ (or another archive format)." |
| Inside a standalone file (its one virtual member) | F5 a single file onto it, or F4 edit and commit | "Replace compressed contents" |
| Inside a standalone file | F7, F8, F2, add a second file, move out | Refused with the reason "A compressed file holds exactly one file." |

The virtual member's name comes from the outer file name (`report.txt.gz` shows `report.txt`) and is display-only. The
plan and manifest use one fixed synthetic member, so renaming the outer file never changes a plan, and a name stored
inside the stream (gzip FNAME) is never confused with it.

## Format rules

- **gzip:** exactly one member, the same metadata-neutral header as TAR.GZ (no FTEXT/FHCRC/FEXTRA/FNAME/FCOMMENT, MTIME 0,
  XFL 0/2/4, operating system byte preserved on replacement), explicit DEFLATE end, CRC, ISIZE and physical EOF. A file
  with a stored name or timestamp (the `gzip` command's default) stays read-only with the plan's metadata explanation;
  FileCat never drops those fields to make it writable.
- **bzip2:** one stream, block size preserved on replacement, combined CRC and physical EOF, as TAR.BZ2.
- **zstd:** one frame with content checksum, no dictionary, no skippable frames, as TAR.ZST.
- Zero-byte payloads produce valid streams in every format.

## Pipeline

The standalone formats use the shared native job route: assessment and fresh verification decode the whole stream
strictly in their own workers and report one member (synthetic name, length, SHA-256); the transform worker writes one
canonical stream from the single captured input; publication, discard, recovery, provenance and edit sessions are the
same code as for TAR. Planning accepts exactly one regular input (creation) or one replacement of the single member
(update); every other change is refused before any object exists.

## Evidence required

AT01/02/04–22 as applicable, standalone fuzz cases for FZ02 and FZ05–07, every synthetic-member/outer-name distinction,
and a check that no bulk action broadens scope.
