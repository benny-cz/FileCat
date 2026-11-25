# E-I679 — The Remote tests' owned sshd could count another fixture's server as its own

2026-11-25. Found by main's CI [run 45696160627](https://github.com/benny-cz/FileCat/actions/runs/45696160627) on `ed0e317c`, the merge of PR #64.

## Finding

- **Failed test:** `ubuntu-24.04 (build, portable tests)` failed `UploadLocalLinkEvidenceTests.A_local_upload_keeps_links_out_of_implicit_content_scope(length: 65537, behavior: "selected-dangling-link", moving: False, replace: False, readBack: True, nativeServer: True)`.
- **What happened:** 36 ms after `TestSshd.TryStart` returned, the upload could not reach the test's owned loopback OpenSSH server: "Could not reach runner@127.0.0.1:45561: Connection refused".
- **The rest of the job:** every other Remote test in the job passed, 2,742 in all. None of the day's changes touch the Remote code.

## Cause

The fixture, not the product.
- **How it picked a port:** `TestSshd` took a free port from a probe listener, released it, started sshd on it, and counted the server ready at the first TCP connect.
- **The race:** the Remote classes start their servers in parallel, so another fixture could take the port first. This sshd then failed to bind and exited, while the connect still reached the other fixture's server. When that server exited, the test's upload was refused.

## Fix

[PR #66](https://github.com/benny-cz/FileCat/pull/66) (`7255268`).
- **Readiness:** a listener counts as this sshd's only once sshd has written its pid file with its own process id. OpenSSH writes that file after binding its listen sockets.
- **A lost port:** it is tried again with another, up to five, instead of skipping the test.
- **A test hook** names the first port tried, for this test flow only.
- **Scope:** test-only change.

## Tests (Ubuntu 24.04 VM, OpenSSH sshd)

- **New `TestSshdTests.A_port_another_server_holds_is_given_up_for_one_this_sshd_binds`:** another listener holds the first port the fixture tries.
  - **With the old readiness logic** (and the hook): it fails, because the fixture accepted the held port: `Expected: Not 39913, Actual: 39913`.
  - **With the fix:** it passes.
- **The seven classes that use the fixture** (329 tests, 35 environment skips) passed twice in a row with their default parallelism.
