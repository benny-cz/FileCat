# E-V13-F1 — Find's criteria against a generated tree

V13 asks for an independently enumerated corpus covering masks, attributes, size and time, ignored paths and the rest
of Find's criteria, positive and negative matches alike. E-V13-S1 did the content search; this does the criteria that
decide which items are looked at and returned.

## The corpus (`SearchCriteriaCorpusTests`, every run)

- **The tree:** about 25 folders up to three deep and 150 files: names in case variants, with spaces, accented letters,
  leading dots, double extensions and none; sizes on the KB boundaries (1,023, 1,024, 1,025, 10,240 bytes and so on)
  and between; modified and created times over 400 days, set after the files were made, folders last; on Windows an
  eighth hidden and an eighth of the files read-only (on Linux and macOS, names starting with a dot are the hidden ones).
- **The queries:** names from masks, lists, exclusions after `|`, plain words (contained anywhere), wildcards and
  folders-only masks; subfolders on or off; hidden items on or off; sizes at least and at most, in bytes and in KB;
  modified and created times within the last N days or hours, or between two moments, either end open; attributes
  set and clear (read-only, hidden, folder); ignored folders by name, by relative path, anchored to the searched folder,
  and by full path.
- **The reference:** each criterion written again from its documentation, over the tree as the system lists it: what
  the walk reaches (hidden folders and ignored folders not entered, subfolders only when asked) and of that what matches;
  a query is refused exactly when its criteria cannot match (a range that ends before it starts, an attribute both set
  and clear).

`FILECAT_FIND_CORPUS_QUERIES` (default 400) and `FILECAT_FIND_CORPUS_SEED` vary it.

## Runs

| Run | Result |
|---|---|
| 400 queries, seed 1313 (every run) | passed: 168 items, 19 queries refused as impossible, 8,816 results |
| 5,000 queries each over three other trees (seeds 1, 2, 3) | passed: 15,000 queries, 847 refused as impossible, about 284,000 results, every one as meant and none missing |
| An exclusive minimum size put into the engine (mutation) | fails at query 13: two files of exactly 10 KB not found under "at least 10 KB" |

## Found beside it

**I83** (Low, `c67fa85`): Find's dialog turns a date without a time into the end of that day, 23:59:59.9999999, and
wrote it back into its box with minutes only ("31.03.2026 23:59"). Read again, that has a time, so a saved search
shown again and run lost the end day's last minute, and a start typed with seconds lost them. The ends now go through
one place that writes an end of day and a midnight start as the date alone and any other time with its seconds;
`FindTimeTextTests` checks the round trip in four cultures and fails with the old formatting.

## Not covered here

- Unreadable folders and links met during the walk (the engine logs the first and does not follow the second; not part
  of this corpus), archive member names, searching within results, and the hidden-data criterion (D-55, its own tests).
- Whole words, regular expressions with their timeout, and hex patterns as criteria of the content search: E-V13-S1
  covers the content search's reading; these options' own semantics are next.
