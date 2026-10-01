# E-I06-P1 — one page-cache budget for every view

I06 (plan §7: "per-reader 16 MiB cache; no established shared 64 MiB controller; … measure reachable aggregate
workloads; fix only demonstrated/contractual violations or record approved target change") and §9's content-cache row
("per-reader bounds alone do not prove aggregate policy"). The architecture plan (§21.2) sets an initial shared content
cache budget of 64 MiB, configurable, because "per-view caches cannot multiply without global accounting".

## What was there

Every view that shows content reads it through a `PagedReader`, a cache of 64 KiB pages bounded by its own limit only:
256 pages (16 MiB) for a viewer, its Info view, a hex editor and a report window; 64 pages (4 MiB) for the quick view and
for each side of a comparison's byte view. Nothing added them up, so the views open at once had no bound together.

**Measured** (`PageCacheBudgetTests.Open_viewers_together_stay_within_the_shared_budget`, first half, which keeps the old
arrangement by giving each reader a budget of its own): five viewers and three hex editors, two comparisons and the
quick view, each read through 40 MiB, hold **148 MiB** of pages together.

## The fix (`61b028f`)

- `PageCacheBudget`: every reader charges its pages to one budget (the application's shared one unless given another),
  64 MiB by default and set by `ContentCacheMiB` in the settings file (4 to 4,096). Past the limit, the least recently
  used page of all goes first, whichever reader holds it; each page carries a stamp from the budget's clock when it is
  used, and a reader's oldest page is the end of its own LRU list.
- Each reader keeps its four most recent pages (256 KiB, more than a screen of text or hex), so a scan through one file
  (a search) cannot empty the views of others. Only those floors can take the total past the limit, when every open
  reader is down to four pages: about 256 open readers at the default limit.
- A reader's own limit still applies inside the budget.
- Charges are made under the reader's lock, as it changes its cache; disposal empties the cache under the same lock and
  then ends the charge, so a page load finishing during disposal can neither keep a page nor leave a charge behind.
  Trimming takes the budget's lock, then one reader's at a time; no reader waits for the budget while holding its own.
- A reader dropped without disposal is written off once collected (the budget holds it weakly, its charge separately).
- Views dispose readers they replace or leave behind: the viewer's Info reader (each time Info is rebuilt, and on
  closing), report windows (on refresh and closing), a comparison's byte-view readers (on comparing again and closing;
  their sources there are views whose disposal is left to the window, as before).

## How it was checked

| Check | Result |
|---|---|
| The same 13 readers, one shared budget | **64 MiB** exactly; the reader used last kept its whole limit (64 pages); all disposed: 0 bytes, 0 readers |
| Least recently used first, across readers (budget 12 pages; A and B read 6 pages each; A's pages 2–5 used again; C reads 4) | A's pages 0 and 1 went first, then B's 0 and 1: A and B hold 2–5, C holds 0–3 |
| Floor (budget 1 page, five readers of 10 pages) | each keeps 4 pages; the total is the floors' 20 pages |
| Lowering the limit to 10 pages | trimmed at once to 10; a limit under one page refused |
| A reader dropped undisposed | its 20 pages written off after collection: 0 bytes, 0 readers |
| Six readers, two seconds of mixed waiting reads, background reads and refreshes after the content changed, two disposed while being read | after the background loads settle, the budget equals the pages the readers hold, within the limit; the disposed hold none; all disposed: 0 |
| Core suite / App suite | 734 / 219, 0 failed |

Negative controls (each a temporary edit, then restored and rebuilt):

| Control | Caught by |
|---|---|
| No trim when a charge passes the limit | four of the six tests |
| `Refresh` drops its pages without releasing their charge | the concurrency test: expected 1,048,576 bytes, the budget said 3,374,907,392 |
| A collected reader's charge never written off | the dropped-reader test: expected 0, found 1,310,720 |

A first version guarded the disposal race with a flag checked after the charge; with that guard removed, the concurrency
test passed five runs out of five: it cannot provoke so narrow a window. The guard was replaced by the construction
above (charges under the reader's lock), which leaves no window.

## Not covered here

- Other memory that grows with what is open: archive indexes (eight kept), pictures the quick view decodes (one at a
  time, from files up to 64 MiB), icon caches, materialized child lists and payloads (I06's last clause): V12.
- The budget's effect on a view while another scans: evicted pages are read again in the background when a view needs
  them, as before when its own limit evicted them; not timed here.
- No setting in the Options dialog; the limit is in the settings file, like the history sizes.
