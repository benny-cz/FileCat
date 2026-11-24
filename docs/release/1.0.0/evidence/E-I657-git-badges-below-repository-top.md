# I657 — Git badges are wrong in every folder below a repository's top

**Found:** 2026-11-24 by I647's fix ([PR #38](https://github.com/benny-cz/FileCat/pull/38), its "Not verified" list), and reproduced the same day with Git alone; the code is that of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

For a folder in a Git work tree, `GitStatusReader` runs two commands in that folder (`GitStatusReader.cs:115-117`):
- `git status --porcelain=v1 -z --untracked-files=normal --ignore-submodules=all -- .`
- `git ls-files --cached -z -- .`

It then files each record under its first path component (`GitStatusSnapshot.Parse`, `GitStatusReader.cs:31-39`).

`ls-files` names paths from the folder, but porcelain status always names them from the top of the work tree: Git's documentation says the porcelain format does not respect `status.relativePaths`, which FileCat sets to true. Reproduced with Git for Windows 2.54.0, in `repo/sub` with a committed `sub/a.txt` changed and a new `sub/b.txt`:

```
status:   " M sub/a.txt", "?? sub/b.txt"
ls-files: "a.txt"
```

So in `repo/sub`:
- `a.txt` reads "Git clean", because `ls-files` lists it and no status record is filed under it;
- the new `b.txt` gets no badge;
- the change is charged to a child named `sub`, which this folder does not have, or which is another folder if it does.

The repository's top folder, and a repository's badge in its parent, are right.

## Why it matters

The badges are wrong, in a way that looks right, in every folder below a repository's top: changed files read clean and new files are unmarked. This is the same kind of fault as I647, in more places.

## Remedy (proposed) and its cost

Make the status paths relative to the folder before filing them. One way is to strip the folder's prefix, which `git rev-parse --show-prefix` gives. Another is to run status from the top and keep only the folder's records. Both cost one short Git run per folder read or none, change product source, and reach users in rc.3.

## Finite check

In a repository's subfolder, a changed tracked file reads "Git changed", a new file "Git untracked", a clean file "Git clean", and a subfolder holding a change reads changed; the same at the top and in a sibling folder.
