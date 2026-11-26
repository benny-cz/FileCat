# E-I683 — A page-view test took WebView2's late favicon request as the page's last request

**Found:** 2026-11-26, by `v1.0.0-rc.8`'s tagged run 45835859037. **Severity:** Low (CI qualification; no product defect). **Disposition:** Must fix for 1.0.0, under the owner's rule of 2026-11-24 that every known issue is fixed.

## What happened

On the Windows ARM64 lane, `PageViewTests.A_markdown_page_whose_completion_never_comes_counts_as_drawn_within_the_bound` failed, the only failure among the platform tests' 2,618 cases: "Assert.InRange() Failure: Value not in range, Range: (3.95 - 14), Actual: -0.87".

The test checks I648's fix in the real WebView2 engine. It withholds WebView2's completion report from the page view, and the page must still count as drawn once it has been quiet for the view's 4 s bound, exactly once. The test's own timeline:

| Time | Event |
|---|---|
| 3.96 s | `/README.md` requested and answered |
| 4.07 s | `/pic.png` requested and answered |
| 6.38 s | WebView2's completion, withheld from the view |
| 10.83 s | "watch: no completion came, but the page's content loaded and every request it made of FileCat was answered: it counts as drawn" |
| 11.67 s | `/favicon.ico` requested, answered not found |

The product did what I648's fix intends. The page counted as drawn once, after its quiet, and the late request neither delayed nor repeated the report.

The test took the last request served at any time as the page's last one, so it measured 10.83 − 11.70 = −0.87 s. [E-I648](E-I648-markdown-page-navigation-never-completes.md) records that WebView2 asks for `/favicon.ico` right after it completes a page. On a busy ARM64 runner, that request came 5.3 s after the completion, past the end of the quiet.

- **The same tree passed this test:** PR #73's run 45828854943 of rc.8's tree did, as did rc.3's to rc.7's tagged runs.
- **rc.8 does not touch it:** rc.8's change, I682, is in Linux startup, which this test does not run.

## Fix

[PR #74](https://github.com/benny-cz/FileCat/pull/74) changes only the test:
- The quiet is measured from the last request served before the page counted as drawn.
- Any request after the report must be `/favicon.ico`. A later request for the page's own content would still fail the test.

Against the failing timeline, the measure is 10.83 − 4.07 = 6.76 s, within the range. On the x64 host, the test passes 3 of 3.

## For rc.8

- **No shipped file changes:** rc.8's packages are the same with or without the fix.
- **Getting rc.8's packages:** the tagged run's failed ARM64 job is run again, so the package and signing jobs, which need it, can run.
- **The owner's decision:** asked whether to keep rc.8 with the fix on main or cut rc.9 with it, the owner answered "keep rc.8". rc.8's source keeps the old test; main and every later release carry the fix.

## Merged (2026-11-26)

PR #74 merged into main as `ce85f6ac` after its full-matrix run 45839173625 passed all 19 jobs. The test passed on both Windows lanes, x64 and ARM64. I683 is remediated preliminarily on main.

rc.8's tagged run was run again for its failed jobs (attempt 2). Its source keeps the old test, whose late-favicon case is rare: it passed in PR #73's run and in rc.3's to rc.7's tagged runs.
