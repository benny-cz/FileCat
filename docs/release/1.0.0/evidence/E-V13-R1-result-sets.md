# E-V13-R1 — refining and appending result sets

V13 asks for result sets, refine and append to keep exactly the items they mean. Read in the code, then tested where a
doubt remained; found **I90**.

## What the code does (read)

- **Identity of an item** (`ItemRef`): its folder, exact name, kind and duplicate ordinal; size and time are evidence
  taken when found and are left out of equality on purpose. A file changed between two searches is still the same
  item, so it is neither dropped by "keep matching" nor listed twice by "append".
- **Keep / remove matching** (`RefineMode.Intersect`, `Subtract`): the set is combined with a new search's finds
  (`Refine.Combine`). A Find window opened to search within earlier results (`SearchQuery.WithinResults`) compares the
  very same references; otherwise the new search walks its roots again and its finds match the set's only where the
  paths do, so a root spelled otherwise had the same trouble as append below. A search that did not finish leaves the
  list as it was and says so.
- **Append**: new finds join the set as they arrive (`ResultSet.AddRange`), and only references not in it yet.

## What was wrong (I90)

Identity compares folders by their exact path. On Windows a root typed in another letter case than the disk's
(`c:\projects\app` for `C:\Projects\App`) names the same folders, but its finds carried the root as typed, so they were
other items: **appending such a search to one from the disk's spelling listed every common file twice** (two files
found through `...\Projects` and again through `...\projects\app`: four items, `SearchRootCaseTests`), and keeping or
removing matches by such a search would have missed them (read, not tested). A copy or a deletion of the appended set
would then act on each file twice.

## The fix (`54c33de`)

A search walks each root as the disk spells it (`PathUtil.WithDiskCase`): each existing name in its own letter case,
one lookup per name (a short 8.3 name should come back as its long name, as the lookup answers with it: not tested
here); past a name that does not exist, the rest stays as
written; other systems and `\\?\` paths unchanged.

| Check | Result |
|---|---|
| `SearchRootCaseTests`: two files appended through two spellings of their folder | 4 items before, 2 after |
| The same: names in other cases, a missing tail, a lower-case drive letter | spelled as the disk has them; the tail kept as written; drive in upper case |
| `SearchCriteriaCorpusTests`, `FindWindowTests` | built their expected paths from the temporary folder as the environment writes it (`C:\WINDOWS\Temp`; the disk has `C:\Windows\Temp`): adjusted to the disk's spelling, then 0 failed |
| Core / App / Windows platform suites | 743 / 222 / 164, 0 failed |

The commit message also says the paths a search reported "differed from the ones the panels show": not established. A
panel keeps the spelling it was navigated with, so before the fix a search from a panel matched it, and now a search
reports the disk's spelling whatever the panel shows.

## Not covered here

- Saved criteria round trips (E-V13-F1 covers the time fields; the rest not re-read here).
- Duplicates found among a set (`DuplicateFinder`), and result sets of archive members.
