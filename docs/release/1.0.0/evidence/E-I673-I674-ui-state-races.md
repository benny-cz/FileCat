# E-I673-I674 — Two UI state races found by PR #57's full-matrix CI

2026-11-25. PR #57 (I672's fix) ran the whole CI matrix ([run 45615496168](https://github.com/benny-cz/FileCat/actions/runs/45615496168), attempt 1). Two App tests that neither I672 nor rc.3 touched failed once each, while the same tests had passed in all three of rc.3's runs of the same code (integration 45570809782, sync PR 45575294166, tag 45579976088). Both failures are real races in the product, not in the tests.

## I673 — A finished comparison could keep showing "Comparing… 100%"

- **Seen:** ubuntu-24.04 App tests, `CompareRevisionLifetimeTests.Closing_or_reopening_a_comparison_retains_its_active_revision_call(pageRefresh: False, reopen: False)`. After the comparison had finished (not comparing, its runs complete), the summary read "Comparing… 19 bytes of 19 bytes (100%)" instead of "Identical: every byte was compared". 3,723 other App tests passed.
- **Cause:** `CompareWindow` posts byte progress from the comparing thread to the window, at most every 250 ms, and the post checked only whether that comparison was cancelled. The comparison sets its result and then its not-comparing state on the window's thread. A progress post that reached the window after the result, which needs a comparison slower than 250 ms and a busy window thread, replaced the result, and nothing replaced it again: the window kept saying the files were still being compared.
- **Fix:** progress is shown only while its own comparison still runs and the summary still shows that comparison's byte progress (`CompareWindow.ShowCompareProgress`): a late post, a post from an earlier comparison, and a post during the text phase ("Comparing lines…") leave the summary alone.
- **Test:** `CompareRevisionLifetimeTests.Byte_progress_that_arrives_after_the_result_leaves_the_result_shown` delivers the late post of a finished comparison and a post of an earlier one; the result stays shown. The old code had no guard besides cancellation, which a finished comparison does not set.

## I674 — The storage call pages could enable Next on the last page

- **Seen:** macos-26 App tests (remainder), `StorageCallEvidenceUiTests.Exact_prefix_navigation_replaces_one_page_and_labels_pending_intent_without_a_crash_claim`. After paging to the last page, with the page's text shown and the read complete, "Next recorded call page" was still enabled. 2,235 other tests passed.
- **Cause:** the read posted its page and button update to the window's thread and returned at once. Navigation then set the buttons from the current page, which could still be the previous page (which had more), so Next stayed enabled until the posted update ran. A click in between read the same page again; nothing was lost.
- **Fix:** the read awaits its page update on the window's thread before it completes, so the page is current when navigation sets the buttons.
- **Test:** the failing test is the check: it asserts Next is disabled as soon as the last page's read completes.

## Runs

- **Windows 11 host, medium integrity** (explorer.exe, Debug build of this branch): `CompareRevisionLifetimeTests` and `StorageCallEvidenceUiTests` together, five times in a row: 9 of 9 passed each time.
- Both races need a busy window thread, so a passing run alone does not show them gone. The fixes remove the window in which the stale value could land: a late progress post is checked against the comparison's state, and the page update finishes before the read does.
- This branch's full-matrix CI runs on its pull request.
