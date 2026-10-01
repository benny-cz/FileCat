# E-V01-S1 — a planned operation keeps its scope and destination

V01 passes when queued jobs keep their exact original scope and destination, a selection does not grow with later
arrivals, and hidden marks are disclosed; its oracle is the recorded source references and destination against direct
before-and-after manifests of the folders.

## The test (`OperationScopeTests`, every run, `49570df`)

Through the window as a user drives it (the headless app, its own copy dialog):

| Case | What happens | Result |
|---|---|---|
| A queued copy while the window changes | two folders hold `x.txt` of different content; `x.txt` and `y.txt` marked in the first and copied with F5 to the second; the copy asks about the second's own `x.txt`, and while it waits two files arrive beside the marked ones, the target panel goes to a third folder, the panels swap and a panel is added; then Replace | the job's recorded sources are exactly the two marked files and its destination the folder it was planned for; that folder ends with exactly those two files, with their content; the third folder stays empty; the source folder gains only the two arrivals; the marks stay on the two files after the arrivals |
| A mark the filter hides | `x.txt` and `y.txt` marked, then the filter `x*` hides `y.txt` | F5's dialog says "Include 1 marked item hidden by the filter", ticked; left ticked, both are copied; unticked, only `x.txt` |

## Not covered here

- Locked and return-to-root tabs, partial listings, long tab strips, non-file locations, moving the last tab, docking by
  pointer dragging and its Esc and release outside the strips, quick view overlays, target-panel bookmarks, and saved
  workspaces restarted with a job pending; the docking and target tests (`DockingTests`, `TargetPanelTests`) cover
  their own parts.
- "No operation follows a replacement row merely because it occupies an old index": not exercised here.
- No mutation was put into the copy's planning to show the test fails with a late destination; the third folder's
  manifest is what would show it.
