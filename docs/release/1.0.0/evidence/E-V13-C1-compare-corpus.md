# E-V13-C1 — file comparison against a generated corpus

V13 asks to compare identical, shifted, repetitive, unrelated, giant-line and truncated data, to reach every claimed
difference beyond the display list's limit, and passes on "no false equality; exact/heuristic/approximate/incomplete
labels correct". The unit tests already took the named cases one at a time (`FileCompareTests`, `AlignedBinaryDiffTests`);
this adds what found I74 in content search: many generated pairs checked against references written in the test.

## The corpus (`CompareCorpusTests`, every run)

`FILECAT_COMPARE_CORPUS_CASES` (default 2,000) and `FILECAT_COMPARE_CORPUS_SEED` vary it; `FILECAT_COMPARE_CORPUS_DUMP`
writes the worst unlabelled shortfall out as two files, to look at with other tools.

- **Text** (`TextDiff`): pairs of up to 200 lines in eight kinds (identical, shifted, repetitive, unrelated, truncated,
  case and spaces, a giant line longer than within-line detail goes, random edits), vocabularies of 3, 40 and 100,000
  distinct lines, with whitespace or case ignored or a tight work budget. For every pair: the blocks cover both sides in
  order, each of its kind's shape; every line called equal is equal under the options, written again from their meaning;
  "identical" exactly when the sides are; the count of differences is the count of blocks that are not equal;
  "approximate" exactly when a block was left unaligned; never more lines paired than the longest common subsequence by
  the textbook table, and **exactly as many unless the result is labelled approximate or heuristic**.
- **Positional binary** (`BinaryDiff`): twice as many differing runs as the list holds (10,000): the first 10,000
  listed, "more not listed" said, and next and previous reach all 20,000, forwards and backwards, as placed.
- **Aligned binary** (`AlignedBinaryDiff`): random and repetitive inputs up to 200 KB, identical, shifted, truncated,
  a block moved, random edits: the ranges cover both inputs in order, every equal range equal byte for byte, and
  "differences" exactly when the bytes differ.

## What it found

| | Before | After (`db2e9b4`) |
|---|---|---|
| **I81**, the text comparison's alignment | anchored first on lines that occur once on each side whenever there were any: case 6295 of seed 1309, 150 lines against 149 with a few edits, paired 104 lines of the 144 the sides share, one line 42 lines from its true place taken as an anchor, so 84 lines showed as only left or only right; `git diff --no-index` with Myers, patience and histogram alike shows 5 insertions and 6 deletions. 40 of 20,000 pairs were short like this, unlabelled | regions of up to 20,000 lines aligned exactly (Myers, the fewest differences) when the work budget allows; larger ones split on unique lines as before and then labelled heuristic, unless the result pairs as many lines as the sides could share at all (for each distinct line, the fewer of its occurrences), which proves it the best. 0 of 20,000 unlabelled pairs short |
| **I82**, the count of differences | one per run of changes: changed lines followed by lines only on one side counted 1, while the window listed and stepped through 2 ("1 difference" over two entries) | one per block, as the result type documented and as the window lists them |

The old alignment, put back with its label taken away, fails the test (case 119: 37 lines paired of 44 in common, and
not labelled).

## Runs

| Run | Result |
|---|---|
| 20,000 text pairs, seed 1309 | passed: 1,039 approximate and 197 heuristic, all labelled (the heuristic ones from the tight budgets: the corpus's own files are small); 398 short of the fewest differences, every one of them labelled; 0 unlabelled |
| 2,000 aligned binary pairs, seed 1311 | passed: all complete, 400 with a moved block |
| 20,000 differing runs, positional | passed: 10,000 listed, the rest said; all 20,000 reached both ways |
| The million-line benchmark (`FILECAT_COMPARE_BENCH=1`) before and after | the same speed within this host's noise (identical 0.87 / 0.88–0.91 s, shifted 1.44 / 1.36–1.49 s, 1,000 scattered edits 1.37 / 1.50–1.67 s, unrelated and repetitive 0.8–1.3 s, cancelling within 20 ms); shifted and scattered edits not labelled heuristic: split on unique lines, and provably the best |
| Core suite 722, App suite 209 | 0 failed |

## Not covered here

- V13's search parts other than content search (masks, attributes, size and time, ignored paths, whole words, regex
  timeout, hex, refine and append, saved criteria, archive member names), result sets, directory comparison's timestamp
  precision and case collisions, and Synchronize with the target changed before the run: next.
- The window's own wording of "heuristic" is checked by reading the code, not by a UI test.
