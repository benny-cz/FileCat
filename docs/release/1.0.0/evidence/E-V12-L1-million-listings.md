# E-V12-L1 — million-entry and long-name listings

V12 lists million-entry and long-name listings among its stress cases; §9 sets "≤500 ms for the first batch in a
million-entry fixture". Measured with `eng/ListingScale` (Release; a synthetic provider handing out a million entries
in batches of 512; `782a3e9`'s optional name length), on this machine (the regression profile of E-V16-H1, not the
§21.2 reference). One run each.

| Run | First rows | Complete | Spill on disk | Index reserved | Peak private / managed |
|---|---|---|---|---|---|
| 1,000,000 entries, names of 29 characters, one panel | **71 ms** | 1.48 s | 93.5 MiB | 38.1 MiB | 144 / 110 MiB |
| 1,000,000 entries, names of **240** characters, one panel | **72 ms** | 1.73 s | **503.5 MiB** | 38.1 MiB | 217 / 160 MiB |
| 1,000,000 entries each in **four** panels at once, 29 characters | 621 ms (all four) | 1.83 s | 373.8 MiB | 152.6 MiB | 401 / 364 MiB |

Whole-listing commands on the first listing (five runs each, ms):

| Command | 29-character names | 240-character names |
|---|---|---|
| Walk the visible rows | 21–28 | 21–27 |
| Quick search, no match (ordinal) | 43–58 | 175–192 |
| Quick search, no match (culture-aware) | 271–298 | 562–587 |
| Find a name, no match | 18–21 | 18–23 |
| Mark by mask | 72–173 | 308–372 |

## Read

- The first batch of one million-entry listing is at 71–72 ms whatever the name length, well within §9's 500 ms; four
  such listings loading at once show rows in all four at 621 ms (§9's budget is for a listing, not four at once).
- Names live in the spill file: a million 240-character names take half a gibibyte of temporary disk space, written
  while the listing loads (the private memory stays at 217 MiB). That is the price of keeping such a listing at all.
- A quick search that misses walks every name: about 0.6 s for a million long names with the culture-aware comparison.
  Whether the panel's key handling waits for that walk is not measured here.

## Not covered here

- Real folders of a million entries (the synthetic provider hands out names as fast as it can).
- The window's own rendering of such listings (the window's benchmark, E-V16-H1).
