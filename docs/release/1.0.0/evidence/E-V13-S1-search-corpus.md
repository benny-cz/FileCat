# E-V13-S1 — content search against an independent reading of a corpus (V13, I74)

Plan V13 asks for search checked with "an independently enumerated corpus covering masks, attributes, size/time, ignored
paths, unreadable roots, whole words, regex timeout, hex, UTF-8, UTF-16 LE/BE at odd/even offsets, chunk boundaries,
embedded NULs, combining characters and invalid encodings", with "both positive and negative matches". This record is
the content half (text, regular expressions, whole words, case, the Unicode option); masks, attributes, sizes, times,
ignored paths and unreadable roots are not covered here.

## The test

`SearchCorpusTests.Content_search_finds_what_reading_each_whole_file_finds_and_nothing_else` (FileCat.Core.Tests; runs
in every lane with 16 files, `FILECAT_SEARCH_CORPUS_FILES` and `_SEED` for longer or other runs):

- **The corpus.** Files of every kind FileCat reads — ASCII, UTF-8 with and without a byte-order mark, UTF-16 LE with
  and without one, UTF-16 BE with one, the system code page, binary — made of text in that encoding (or random bytes),
  a quarter of them larger than FileCat's 1 MiB reads. Into each, one to three of five words of different scripts
  ("Quixotic", "žluťoučký", "Straße", "日本語", "naïve", the last also written with a separate combining diaeresis) are
  planted, in another case or not, in the file's own encoding or as UTF-8, UTF-16 LE or UTF-16 BE (so at odd and even
  offsets), with a neighbour on each side that is or is not a word character (a letter, a digit, an underscore, an
  accented letter, a combining mark, or a space, a full stop, a line break, nothing). In files larger than one read the
  first word is placed where reads meet: across the end of the first read, where the second read's carried text
  starts, or ending exactly where the first read ends.
- **The queries.** Per word: the text; with match case; with Unicode; whole words with Unicode; whole words with match
  case; the regular expressions `^word`, `word$` (with Unicode) and `(?<![ .])word`. Forty queries.
- **The oracle.** Each file decoded whole, at once, in the readings `SearchQuery` documents: the encoding its start
  suggests (FileCat's own detection, `TextDecoding.Detect`, the one part shared), and with Unicode also UTF-16 in
  either byte order from even and odd offsets and UTF-8 ("also found where a file keeps it as UTF-16 (either byte
  order, at any offset) or UTF-8"). Matched there with the documented rules: ordinal for binary readings, linguistic
  ignoring case otherwise; whole words and regular expressions as the documented regular expression. What the oracle
  does not share with the engine is the reading in 1 MiB windows with carried text — the part under test — and the
  choice of readings.
- **The comparison.** Every query's results, found and not found alike, against the oracle for every file.

## What it found (I74, fixed `b0a2313`)

Before the fix, with the default corpus, 2 of 640 answers differed; with three larger corpora (48 files, seeds 7, 42,
99), 5, 4 and 2 of 1,920:

| Answer | Cause |
|---|---|
| `word$` found where the word ends exactly where the first 1 MiB read ends and the file goes on with `_`, `.` or `x` (UTF-8, UTF-16 LE and BE, binary files) | a regular expression without whole words was matched against each window as though its edges were the file's: `$` matched the window's end |
| `^word` and `(?<![ .])word` found where the word starts the second read's carried text after a full stop | `^` and the look-behind matched the window's start |
| "naïve" (case ignored) found where the file says "naïvë", its last letter given a combining diaeresis that began the next read | a linguistic match that ended with the window was accepted before the next read showed the mark that changes its last letter |
| "Quixotic" with Unicode not found in a UTF-16 file holding it as UTF-8 | the UTF-8 reading was left out for ASCII text, which is right only when the file's own reading is 8-bit |

The first three are false positives at read boundaries (rare per file: a candidate must sit at the boundary), the last
a false negative for one layout. **Fix (`b0a2313`):** every regular expression follows the rule whole words already
did — a match at a window's first character is left to the window before, which saw what precedes it, and a match
that ends with the window waits for the next read (it counts if the file ends there); a linguistic match that ends
with the window waits likewise; the UTF-8 reading is added for a file read as UTF-16.

**After:** 0 of 640 and 0, 0, 0 of 1,920. The existing content search tests pass; core suite 714, 0 failed.

## Not covered here

Masks, attributes, size and time filters, ignored folders, unreadable roots, hex search, the regex timeout, archive
member names, result sets and working sets (V13's other parts), comparison and synchronization.
