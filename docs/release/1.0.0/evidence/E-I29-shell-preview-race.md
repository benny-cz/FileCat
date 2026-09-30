# E-I29 — a shell picture asked for again while the helper worked on it was asked twice

Issue: [I29](../FILECAT_1_0_RELEASE_ISSUES.md#i29--a-shell-picture-asked-for-while-the-helper-already-worked-on-it-was-asked-again).

## E-I29-D1 — discovery

CI run 36778104838 on `2197074` (the I24 change, unrelated), Windows ARM64:
`ShellHostTests.Previews_share_identical_requests_cache_answers_and_never_start_the_helper_for_refused_items` failed
`Assert.Same()` — "Values are not the same instance": two identical icon requests made one after the other got equal but
separate answers. The test had passed on that lane for the three commits before.

## E-I29-M1 — mechanism (source)

`ShellPreviews` queues requests and a worker thread serves them newest first. `Next()` takes a request off the pending
list before the worker asks the out-of-process helper. An identical request arriving while the helper works on the first
looked only at the cache (not filled yet) and the pending list (no longer holding it), so it queued a second request:
the helper was asked twice, the two callers got different instances, and the second answer replaced the first in the
cache. Whether the second request came before or after the worker took the first decided the outcome — timing.

## E-I29-R1 / V1 — reproduction and fix `7175a41`

- New test `A_request_made_while_the_same_one_is_with_the_helper_shares_its_answer` holds the worker between taking the
  request and asking the helper (an internal test hook) while the second request is made. On the unchanged logic it
  **failed** with CI's message; with the fix it passes.
- **Fix:** the request the helper works on stays joinable (`_running`) until its answer is cached; a request for the same
  picture meanwhile waits for that answer.
- **Regression:** the six `ShellHostTests` three times; the whole Platform.Windows suite (111 tests, 0 failed); CI run
  36779059604 on `7175a41`: all four lanes green.

Impact outside tests: duplicate work in the helper and a replaced cache entry when a view asks for the same picture twice
in quick succession (scrolling); no wrong picture was shown.
